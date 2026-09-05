#include "CredentialProvider.h"

#include "BridgeClient.h"
#include "Credential.h"

#include <windows.h>
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
      _events(nullptr),
      _adviseContext(0),
      _hasCredential(false)
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
    if (_events != nullptr)
    {
        _events->Release();
        _events = nullptr;
    }
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
    if (_events != nullptr)
    {
        _events->Release();
        _events = nullptr;
    }

    _adviseContext = adviseContext;
    if (events != nullptr)
    {
        events->AddRef();
        _events = events;
    }

    return S_OK;
}

HRESULT GaltekCredentialProvider::UnAdvise()
{
    if (_events != nullptr)
    {
        _events->Release();
        _events = nullptr;
    }

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
