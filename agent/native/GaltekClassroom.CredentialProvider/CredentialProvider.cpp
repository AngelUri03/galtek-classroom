#include "CredentialProvider.h"

#include "BridgeClient.h"

#include <windows.h>

extern long g_objectCount;

GaltekCredentialProvider::GaltekCredentialProvider()
    : _referenceCount(1),
      _usageScenario(CPUS_INVALID)
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
    UNREFERENCED_PARAMETER(events);
    UNREFERENCED_PARAMETER(adviseContext);
    return S_OK;
}

HRESULT GaltekCredentialProvider::UnAdvise()
{
    return S_OK;
}

HRESULT GaltekCredentialProvider::GetFieldDescriptorCount(DWORD* count)
{
    if (count == nullptr)
    {
        return E_POINTER;
    }

    *count = 0;
    return S_OK;
}

HRESULT GaltekCredentialProvider::GetFieldDescriptorAt(
    DWORD fieldId,
    CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** descriptor)
{
    UNREFERENCED_PARAMETER(fieldId);

    if (descriptor == nullptr)
    {
        return E_POINTER;
    }

    *descriptor = nullptr;
    return E_INVALIDARG;
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

    if (_usageScenario != CPUS_LOGON)
    {
        return S_OK;
    }

    BridgeClient bridge;
    (void)bridge.GetPendingActivationMetadata(250);

    // 19G1 intentionally detects only bridge availability/metadata and never
    // enumerates a productive Galtek tile or serializes credentials.
    return S_OK;
}

HRESULT GaltekCredentialProvider::GetCredentialAt(
    DWORD credentialIndex,
    ICredentialProviderCredential** credential)
{
    UNREFERENCED_PARAMETER(credentialIndex);

    if (credential == nullptr)
    {
        return E_POINTER;
    }

    *credential = nullptr;
    return E_INVALIDARG;
}
