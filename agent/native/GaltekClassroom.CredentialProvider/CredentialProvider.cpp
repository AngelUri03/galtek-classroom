#include "CredentialProvider.h"

#include "BridgeClient.h"
#include "Credential.h"

#include <windows.h>
#include <objbase.h>
#include <cstring>
#include <new>

extern long g_objectCount;

namespace
{
    struct GaltekFieldDescriptorDefinition
    {
        DWORD fieldId;
        CREDENTIAL_PROVIDER_FIELD_TYPE fieldType;
        PCWSTR label;
    };

    constexpr GaltekFieldDescriptorDefinition kFieldDescriptors[] =
    {
        { GaltekFieldTitle, CPFT_LARGE_TEXT, L"Galtek Classroom" },
        { GaltekFieldSubtitle, CPFT_SMALL_TEXT, L"Cuenta escolar" },
        { GaltekFieldSubmit, CPFT_SUBMIT_BUTTON, L"Iniciar sesion" }
    };

    HRESULT AllocCoTaskString(PCWSTR source, PWSTR* value)
    {
        if (value == nullptr)
        {
            return E_POINTER;
        }

        *value = nullptr;
        const size_t charCount = wcslen(source) + 1u;
        const size_t byteCount = charCount * sizeof(wchar_t);
        PWSTR copy = static_cast<PWSTR>(CoTaskMemAlloc(byteCount));
        if (copy == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        memcpy(copy, source, byteCount);
        *value = copy;
        return S_OK;
    }
}

GaltekCredentialProvider::GaltekCredentialProvider()
    : _referenceCount(1),
      _usageScenario(CPUS_INVALID),
      _adviseContext(0),
      _hasCredential(false),
      _notificationStopEvent(nullptr),
      _notificationRunning(false)
{
    InterlockedIncrement(&g_objectCount);
}

HRESULT GaltekCredentialProvider::QueryInterface(REFIID riid, void** object)
{
    if (object == nullptr)
    {
        return E_POINTER;
    }

    *object = nullptr;
    if (riid == IID_IUnknown || riid == IID_ICredentialProvider)
    {
        *object = static_cast<ICredentialProvider*>(this);
        AddRef();
        return S_OK;
    }

    return E_NOINTERFACE;
}

ULONG GaltekCredentialProvider::AddRef()
{
    return static_cast<ULONG>(InterlockedIncrement(&_referenceCount));
}

ULONG GaltekCredentialProvider::Release()
{
    const LONG count = InterlockedDecrement(&_referenceCount);
    if (count == 0)
    {
        InterlockedDecrement(&g_objectCount);
        delete this;
    }

    return static_cast<ULONG>(count);
}

GaltekCredentialProvider::~GaltekCredentialProvider()
{
    StopNotificationWorker();
}

HRESULT GaltekCredentialProvider::SetUsageScenario(
    CREDENTIAL_PROVIDER_USAGE_SCENARIO usageScenario,
    DWORD flags)
{
    UNREFERENCED_PARAMETER(flags);

    if (usageScenario == CPUS_LOGON)
    {
        _usageScenario = usageScenario;
        return S_OK;
    }

    _usageScenario = CPUS_INVALID;
    return E_NOTIMPL;
}

HRESULT GaltekCredentialProvider::SetSerialization(
    const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* serialization)
{
    UNREFERENCED_PARAMETER(serialization);
    return E_NOTIMPL;
}

HRESULT GaltekCredentialProvider::Advise(ICredentialProviderEvents* events, UINT_PTR adviseContext)
{
    StopNotificationWorker();

    _adviseContext = adviseContext;
    if (events != nullptr && _usageScenario == CPUS_LOGON)
    {
        IStream* eventsStream = nullptr;
        HRESULT hr = CoMarshalInterThreadInterfaceInStream(
            IID_ICredentialProviderEvents,
            events,
            &eventsStream);
        if (FAILED(hr))
        {
            return hr;
        }

        HANDLE stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (stopEvent == nullptr)
        {
            eventsStream->Release();
            return HRESULT_FROM_WIN32(GetLastError());
        }

        _notificationStopEvent = stopEvent;
        _notificationRunning = true;
        try
        {
            _notificationThread = std::thread(
                &GaltekCredentialProvider::NotificationWorker,
                this,
                eventsStream,
                adviseContext,
                stopEvent);
        }
        catch (const std::bad_alloc&)
        {
            _notificationRunning = false;
            _notificationStopEvent = nullptr;
            CloseHandle(stopEvent);
            eventsStream->Release();
            return E_OUTOFMEMORY;
        }
        catch (...)
        {
            _notificationRunning = false;
            _notificationStopEvent = nullptr;
            CloseHandle(stopEvent);
            eventsStream->Release();
            return E_FAIL;
        }
    }

    return S_OK;
}

HRESULT GaltekCredentialProvider::UnAdvise()
{
    StopNotificationWorker();
    _adviseContext = 0;
    return S_OK;
}

HRESULT GaltekCredentialProvider::GetFieldDescriptorCount(DWORD* count)
{
    if (count == nullptr)
    {
        return E_POINTER;
    }

    *count = GaltekFieldCount;
    return S_OK;
}

HRESULT GaltekCredentialProvider::GetFieldDescriptorAt(
    DWORD fieldId,
    CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** descriptor)
{
    if (descriptor == nullptr)
    {
        return E_POINTER;
    }

    *descriptor = nullptr;
    if (fieldId >= GaltekFieldCount)
    {
        return E_INVALIDARG;
    }

    CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR* copy =
        static_cast<CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR*>(
            CoTaskMemAlloc(sizeof(CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR)));
    if (copy == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    ZeroMemory(copy, sizeof(*copy));
    copy->dwFieldID = kFieldDescriptors[fieldId].fieldId;
    copy->cpft = kFieldDescriptors[fieldId].fieldType;
    copy->guidFieldType = GUID_NULL;
    HRESULT hr = AllocCoTaskString(kFieldDescriptors[fieldId].label, &copy->pszLabel);
    if (FAILED(hr))
    {
        CoTaskMemFree(copy);
        return hr;
    }

    *descriptor = copy;
    return S_OK;
}

HRESULT GaltekCredentialProvider::GetCredentialCount(
    DWORD* count,
    DWORD* defaultCredential,
    BOOL* autoLogonWithDefault)
{
    if (count == nullptr || defaultCredential == nullptr || autoLogonWithDefault == nullptr)
    {
        return E_POINTER;
    }

    _hasCredential = false;
    _identity = BridgeActivationIdentity{};
    *count = 0;
    *defaultCredential = CREDENTIAL_PROVIDER_NO_DEFAULT;
    *autoLogonWithDefault = FALSE;

    if (_usageScenario != CPUS_LOGON)
    {
        return S_OK;
    }

    BridgeClient bridge;
    BridgeActivationIdentity identity;
    if (bridge.GetPendingActivationIdentity(250, &identity))
    {
        _identity = identity;
        _hasCredential = true;
        *count = 1;
        if (identity.autoSubmitRequested)
        {
            *defaultCredential = 0;
            *autoLogonWithDefault = TRUE;
        }
    }

    return S_OK;
}

HRESULT GaltekCredentialProvider::GetCredentialAt(
    DWORD credentialIndex,
    ICredentialProviderCredential** credential)
{
    if (credential == nullptr)
    {
        return E_POINTER;
    }

    *credential = nullptr;
    if (!_hasCredential || credentialIndex != 0)
    {
        return E_INVALIDARG;
    }

    GaltekCredential* value = new (std::nothrow) GaltekCredential(_identity);
    if (value == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    *credential = static_cast<ICredentialProviderCredential*>(value);
    return S_OK;
}

void GaltekCredentialProvider::StopNotificationWorker()
{
    HANDLE stopEvent = _notificationStopEvent;
    if (stopEvent != nullptr)
    {
        SetEvent(stopEvent);
    }

    if (_notificationThread.joinable())
    {
        _notificationThread.join();
    }

    if (stopEvent != nullptr)
    {
        CloseHandle(stopEvent);
    }

    _notificationStopEvent = nullptr;
    _notificationRunning = false;
}

void GaltekCredentialProvider::NotificationWorker(
    IStream* eventsStream,
    UINT_PTR adviseContext,
    HANDLE stopEvent)
{
    HRESULT init = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    ICredentialProviderEvents* events = nullptr;
    if ((SUCCEEDED(init) || init == RPC_E_CHANGED_MODE) && eventsStream != nullptr)
    {
        CoGetInterfaceAndReleaseStream(
            eventsStream,
            IID_ICredentialProviderEvents,
            reinterpret_cast<void**>(&events));
        eventsStream = nullptr;
    }

    if (eventsStream != nullptr)
    {
        eventsStream->Release();
    }

    if (events == nullptr)
    {
        if (SUCCEEDED(init))
        {
            CoUninitialize();
        }

        return;
    }

    BridgeClient bridge;
    long long observedGeneration = 0;
    DWORD reconnectDelay = 250;
    while (WaitForSingleObject(stopEvent, 0) == WAIT_TIMEOUT)
    {
        long long generation = observedGeneration;
        if (bridge.WaitForActivationChange(observedGeneration, stopEvent, &generation))
        {
            observedGeneration = generation;
            reconnectDelay = 250;
            events->CredentialsChanged(adviseContext);
            continue;
        }

        if (WaitForSingleObject(stopEvent, reconnectDelay) != WAIT_TIMEOUT)
        {
            break;
        }

        if (reconnectDelay < 1000)
        {
            reconnectDelay *= 2;
        }
    }

    events->Release();
    if (SUCCEEDED(init))
    {
        CoUninitialize();
    }
}
