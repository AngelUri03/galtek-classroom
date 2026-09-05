#include "Credential.h"

#include <windows.h>

extern long g_objectCount;

GaltekCredential::GaltekCredential()
    : _referenceCount(1)
{
    InterlockedIncrement(&g_objectCount);
}

HRESULT GaltekCredential::QueryInterface(REFIID riid, void** object)
{
    if (object == nullptr)
    {
        return E_POINTER;
    }

    *object = nullptr;
    if (riid == IID_IUnknown
        || riid == IID_ICredentialProviderCredential
        || riid == IID_ICredentialProviderCredential2)
    {
        *object = static_cast<ICredentialProviderCredential2*>(this);
        AddRef();
        return S_OK;
    }

    return E_NOINTERFACE;
}

ULONG GaltekCredential::AddRef()
{
    return static_cast<ULONG>(InterlockedIncrement(&_referenceCount));
}

ULONG GaltekCredential::Release()
{
    const LONG count = InterlockedDecrement(&_referenceCount);
    if (count == 0)
    {
        InterlockedDecrement(&g_objectCount);
        delete this;
    }

    return static_cast<ULONG>(count);
}

HRESULT GaltekCredential::Advise(ICredentialProviderCredentialEvents* events)
{
    UNREFERENCED_PARAMETER(events);
    return S_OK;
}

HRESULT GaltekCredential::UnAdvise()
{
    return S_OK;
}

HRESULT GaltekCredential::SetSelected(BOOL* autoLogon)
{
    if (autoLogon == nullptr)
    {
        return E_POINTER;
    }

    *autoLogon = FALSE;
    return S_OK;
}

HRESULT GaltekCredential::SetDeselected()
{
    return S_OK;
}

HRESULT GaltekCredential::GetFieldState(
    DWORD fieldId,
    CREDENTIAL_PROVIDER_FIELD_STATE* fieldState,
    CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE* fieldInteractiveState)
{
    UNREFERENCED_PARAMETER(fieldId);

    if (fieldState == nullptr || fieldInteractiveState == nullptr)
    {
        return E_POINTER;
    }

    *fieldState = CPFS_HIDDEN;
    *fieldInteractiveState = CPFIS_NONE;
    return E_INVALIDARG;
}

HRESULT GaltekCredential::GetStringValue(DWORD fieldId, PWSTR* value)
{
    UNREFERENCED_PARAMETER(fieldId);

    if (value == nullptr)
    {
        return E_POINTER;
    }

    *value = nullptr;
    return E_INVALIDARG;
}

HRESULT GaltekCredential::GetBitmapValue(DWORD fieldId, HBITMAP* bitmap)
{
    UNREFERENCED_PARAMETER(fieldId);

    if (bitmap == nullptr)
    {
        return E_POINTER;
    }

    *bitmap = nullptr;
    return E_INVALIDARG;
}

HRESULT GaltekCredential::GetCheckboxValue(DWORD fieldId, BOOL* checked, PWSTR* label)
{
    UNREFERENCED_PARAMETER(fieldId);

    if (checked == nullptr || label == nullptr)
    {
        return E_POINTER;
    }

    *checked = FALSE;
    *label = nullptr;
    return E_INVALIDARG;
}

HRESULT GaltekCredential::GetSubmitButtonValue(DWORD fieldId, DWORD* adjacentTo)
{
    UNREFERENCED_PARAMETER(fieldId);

    if (adjacentTo == nullptr)
    {
        return E_POINTER;
    }

    *adjacentTo = 0;
    return E_INVALIDARG;
}

HRESULT GaltekCredential::GetComboBoxValueCount(DWORD fieldId, DWORD* itemCount, DWORD* selectedItem)
{
    UNREFERENCED_PARAMETER(fieldId);

    if (itemCount == nullptr || selectedItem == nullptr)
    {
        return E_POINTER;
    }

    *itemCount = 0;
    *selectedItem = 0;
    return E_INVALIDARG;
}

HRESULT GaltekCredential::GetComboBoxValueAt(DWORD fieldId, DWORD item, PWSTR* value)
{
    UNREFERENCED_PARAMETER(fieldId);
    UNREFERENCED_PARAMETER(item);

    if (value == nullptr)
    {
        return E_POINTER;
    }

    *value = nullptr;
    return E_INVALIDARG;
}

HRESULT GaltekCredential::SetStringValue(DWORD fieldId, PCWSTR value)
{
    UNREFERENCED_PARAMETER(fieldId);
    UNREFERENCED_PARAMETER(value);
    return E_INVALIDARG;
}

HRESULT GaltekCredential::SetCheckboxValue(DWORD fieldId, BOOL checked)
{
    UNREFERENCED_PARAMETER(fieldId);
    UNREFERENCED_PARAMETER(checked);
    return E_INVALIDARG;
}

HRESULT GaltekCredential::SetComboBoxSelectedValue(DWORD fieldId, DWORD selectedItem)
{
    UNREFERENCED_PARAMETER(fieldId);
    UNREFERENCED_PARAMETER(selectedItem);
    return E_INVALIDARG;
}

HRESULT GaltekCredential::CommandLinkClicked(DWORD fieldId)
{
    UNREFERENCED_PARAMETER(fieldId);
    return E_INVALIDARG;
}

HRESULT GaltekCredential::GetSerialization(
    CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE* response,
    CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* serialization,
    PWSTR* statusText,
    CREDENTIAL_PROVIDER_STATUS_ICON* statusIcon)
{
    if (response == nullptr || serialization == nullptr || statusText == nullptr || statusIcon == nullptr)
    {
        return E_POINTER;
    }

    *response = CPGSR_NO_CREDENTIAL_NOT_FINISHED;
    ZeroMemory(serialization, sizeof(*serialization));
    *statusText = nullptr;
    *statusIcon = CPSI_NONE;
    return S_OK;
}

HRESULT GaltekCredential::ReportResult(
    NTSTATUS status,
    NTSTATUS substatus,
    PWSTR* statusText,
    CREDENTIAL_PROVIDER_STATUS_ICON* statusIcon)
{
    UNREFERENCED_PARAMETER(status);
    UNREFERENCED_PARAMETER(substatus);

    if (statusText == nullptr || statusIcon == nullptr)
    {
        return E_POINTER;
    }

    *statusText = nullptr;
    *statusIcon = CPSI_NONE;
    return S_OK;
}

HRESULT GaltekCredential::GetUserSid(PWSTR* sid)
{
    if (sid == nullptr)
    {
        return E_POINTER;
    }

    *sid = nullptr;
    return E_NOTIMPL;
}
