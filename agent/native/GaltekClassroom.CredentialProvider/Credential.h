#pragma once

#include "BridgeClient.h"

#include <credentialprovider.h>

enum GaltekCredentialFieldId
{
    GaltekFieldTitle = 0,
    GaltekFieldSubtitle = 1,
    GaltekFieldSubmit = 2,
    GaltekFieldCount = 3
};

class GaltekCredential final : public ICredentialProviderCredential2
{
public:
    explicit GaltekCredential(const BridgeActivationIdentity& identity);

    IFACEMETHODIMP QueryInterface(REFIID riid, void** object) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;

    IFACEMETHODIMP Advise(ICredentialProviderCredentialEvents* events) override;
    IFACEMETHODIMP UnAdvise() override;
    IFACEMETHODIMP SetSelected(BOOL* autoLogon) override;
    IFACEMETHODIMP SetDeselected() override;
    IFACEMETHODIMP GetFieldState(
        DWORD fieldId,
        CREDENTIAL_PROVIDER_FIELD_STATE* fieldState,
        CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE* fieldInteractiveState) override;
    IFACEMETHODIMP GetStringValue(DWORD fieldId, PWSTR* value) override;
    IFACEMETHODIMP GetBitmapValue(DWORD fieldId, HBITMAP* bitmap) override;
    IFACEMETHODIMP GetCheckboxValue(DWORD fieldId, BOOL* checked, PWSTR* label) override;
    IFACEMETHODIMP GetSubmitButtonValue(DWORD fieldId, DWORD* adjacentTo) override;
    IFACEMETHODIMP GetComboBoxValueCount(DWORD fieldId, DWORD* itemCount, DWORD* selectedItem) override;
    IFACEMETHODIMP GetComboBoxValueAt(DWORD fieldId, DWORD item, PWSTR* value) override;
    IFACEMETHODIMP SetStringValue(DWORD fieldId, PCWSTR value) override;
    IFACEMETHODIMP SetCheckboxValue(DWORD fieldId, BOOL checked) override;
    IFACEMETHODIMP SetComboBoxSelectedValue(DWORD fieldId, DWORD selectedItem) override;
    IFACEMETHODIMP CommandLinkClicked(DWORD fieldId) override;
    IFACEMETHODIMP GetSerialization(
        CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE* response,
        CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* serialization,
        PWSTR* statusText,
        CREDENTIAL_PROVIDER_STATUS_ICON* statusIcon) override;
    IFACEMETHODIMP ReportResult(
        NTSTATUS status,
        NTSTATUS substatus,
        PWSTR* statusText,
        CREDENTIAL_PROVIDER_STATUS_ICON* statusIcon) override;
    IFACEMETHODIMP GetUserSid(PWSTR* sid) override;

private:
    ~GaltekCredential();

    LONG _referenceCount;
    ICredentialProviderCredentialEvents* _events;
    std::string _activationId;
    std::wstring _userSid;
    std::wstring _domain;
    std::wstring _username;
    bool _acquireAttempted;
};
