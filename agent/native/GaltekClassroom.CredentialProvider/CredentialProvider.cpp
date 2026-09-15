#include "CredentialProvider.h"

#include "BridgeClient.h"
#include "Credential.h"
#include "NativeTrace.h"

#include <windows.h>
#include <objbase.h>
#include <cstring>
#include <new>
#include <mutex>
#include <string>

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

#ifdef GALTEK_CREDENTIAL_PROVIDER_TESTS
GaltekCredentialProvider::GaltekCredentialProvider(const std::wstring& bridgePipeName)
    : GaltekCredentialProvider()
{
    _bridgePipeName = bridgePipeName;
}
#endif

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
    StopNotificationWorker(L"destructor");
}

HRESULT GaltekCredentialProvider::SetUsageScenario(
    CREDENTIAL_PROVIDER_USAGE_SCENARIO usageScenario,
    DWORD flags)
{
    UNREFERENCED_PARAMETER(flags);

    if (usageScenario == CPUS_LOGON)
    {
        std::lock_guard<std::mutex> lock(_stateMutex);
        _usageScenario = usageScenario;
        return S_OK;
    }

    {
        std::lock_guard<std::mutex> lock(_stateMutex);
        _usageScenario = CPUS_INVALID;
        _hasCredential = false;
        _identity = BridgeActivationIdentity{};
    }

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
    StopNotificationWorker(L"readvise");

    _adviseContext = adviseContext;
    CREDENTIAL_PROVIDER_USAGE_SCENARIO usageScenario = CPUS_INVALID;
    {
        std::lock_guard<std::mutex> lock(_stateMutex);
        usageScenario = _usageScenario;
    }

    if (events != nullptr && usageScenario == CPUS_LOGON)
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
                eventsStream,
                adviseContext,
                stopEvent,
                _bridgePipeName);
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
    GaltekTraceEvent(L"CP_UNADVISE");
    StopNotificationWorker(L"unadvise");
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

    *count = 0;
    *defaultCredential = CREDENTIAL_PROVIDER_NO_DEFAULT;
    *autoLogonWithDefault = FALSE;
    GaltekTraceEvent(L"CP_GET_CREDENTIAL_COUNT_ENTERED");

    CREDENTIAL_PROVIDER_USAGE_SCENARIO usageScenario = CPUS_INVALID;
    {
        std::lock_guard<std::mutex> lock(_stateMutex);
        usageScenario = _usageScenario;
    }

    if (usageScenario != CPUS_LOGON)
    {
        std::lock_guard<std::mutex> lock(_stateMutex);
        _hasCredential = false;
        _identity = BridgeActivationIdentity{};
        GaltekTraceEvent(L"CP_GET_CREDENTIAL_COUNT_NO_IDENTITY");
        GaltekTraceLine(L"CP_GET_CREDENTIAL_COUNT_RETURN count=0 autoLogon=0");
        return S_OK;
    }

    BridgeClient bridge(_bridgePipeName);
    BridgeActivationIdentity identity;
    bool hasCredential = false;
    if (bridge.GetPendingActivationIdentity(250, &identity))
    {
        hasCredential = true;
        *count = 1;
        GaltekTraceEvent(L"CP_GET_CREDENTIAL_COUNT_IDENTITY_READY");
        if (identity.autoSubmitRequested)
        {
            *defaultCredential = 0;
            *autoLogonWithDefault = TRUE;
        }
    }
    else
    {
        GaltekTraceEvent(L"CP_GET_CREDENTIAL_COUNT_NO_IDENTITY");
    }

    {
        std::lock_guard<std::mutex> lock(_stateMutex);
        _identity = hasCredential ? identity : BridgeActivationIdentity{};
        _hasCredential = hasCredential;
    }

    if (hasCredential)
    {
        GaltekTraceEventWithAccount(L"CP_IDENTITY_SNAPSHOT_COMMITTED", identity.accountId);
    }

    wchar_t buffer[96]{};
    swprintf_s(
        buffer,
        L"CP_GET_CREDENTIAL_COUNT_RETURN count=%lu autoLogon=%lu",
        *count,
        *autoLogonWithDefault ? 1u : 0u);
    GaltekTraceLine(buffer);
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
    BridgeActivationIdentity identity;
    {
        std::lock_guard<std::mutex> lock(_stateMutex);
        if (!_hasCredential || credentialIndex != 0)
        {
            return E_INVALIDARG;
        }

        identity = _identity;
    }

    if (identity.activationId.empty()
        || identity.userSid.empty()
        || identity.domain.empty()
        || identity.username.empty())
    {
        return E_INVALIDARG;
    }

    GaltekCredential* value = new (std::nothrow) GaltekCredential(identity);
    if (value == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    *credential = static_cast<ICredentialProviderCredential*>(value);
    return S_OK;
}

void GaltekCredentialProvider::StopNotificationWorker(const wchar_t* reason)
{
    HANDLE stopEvent = _notificationStopEvent;
    if (stopEvent != nullptr)
    {
        SetEvent(stopEvent);
    }

    if (_notificationThread.joinable())
    {
        _notificationThread.join();
        std::wstring line = L"CP_WORKER_CANCEL reason=";
        line.append(reason == nullptr ? L"unknown" : reason);
        GaltekTraceLine(line);
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
    HANDLE stopEvent,
    std::wstring bridgePipeName)
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
        GaltekTraceLine(L"CP_WORKER_CANCEL reason=events_unavailable");
        if (SUCCEEDED(init))
        {
            CoUninitialize();
        }

        return;
    }

    BridgeClient bridge(bridgePipeName);
    long long observedGeneration = 0;
    DWORD reconnectDelay = 250;
    while (WaitForSingleObject(stopEvent, 0) == WAIT_TIMEOUT)
    {
        long long generation = observedGeneration;
        if (bridge.WaitForActivationChange(observedGeneration, stopEvent, &generation))
        {
            wchar_t waitBuffer[96]{};
            swprintf_s(waitBuffer, L"CP_WORKER_WAIT_SUCCESS generation=%lld", generation);
            GaltekTraceLine(waitBuffer);

            wchar_t callBuffer[96]{};
            swprintf_s(callBuffer, L"CP_CREDENTIALS_CHANGED_CALL generation=%lld", generation);
            GaltekTraceLine(callBuffer);
            const HRESULT changedHr = events->CredentialsChanged(adviseContext);
            wchar_t resultBuffer[128]{};
            swprintf_s(
                resultBuffer,
                L"CP_CREDENTIALS_CHANGED_RESULT hr=%ld",
                static_cast<long>(changedHr));
            GaltekTraceLine(resultBuffer);

            if (SUCCEEDED(changedHr))
            {
                observedGeneration = generation;
                reconnectDelay = 250;
                wchar_t rearmBuffer[96]{};
                swprintf_s(rearmBuffer, L"CP_WORKER_REARM generation=%lld", observedGeneration);
                GaltekTraceLine(rearmBuffer);
            }
            else
            {
                if (WaitForSingleObject(stopEvent, reconnectDelay) != WAIT_TIMEOUT)
                {
                    break;
                }

                if (reconnectDelay < 1000)
                {
                    reconnectDelay *= 2;
                }
            }

            continue;
        }

        if (WaitForSingleObject(stopEvent, 0) != WAIT_TIMEOUT)
        {
            break;
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
