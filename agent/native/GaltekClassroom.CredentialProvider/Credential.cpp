#include "Credential.h"

#include "Guid.h"
#include "NativeTrace.h"

#include <windows.h>
#include <ntsecapi.h>
#include <wincred.h>
#include <cstdio>
#include <cstring>
#include <new>
#include <vector>

extern long g_objectCount;

namespace
{
    constexpr DWORD kAcquireTimeoutMilliseconds = 750;
    LONG g_nextCredentialInstanceId = 0;

    class ScopedSecureWideBuffer
    {
    public:
        ScopedSecureWideBuffer() = default;
        ScopedSecureWideBuffer(const ScopedSecureWideBuffer&) = delete;
        ScopedSecureWideBuffer& operator=(const ScopedSecureWideBuffer&) = delete;
        ~ScopedSecureWideBuffer()
        {
            Reset();
        }

        HRESULT Allocate(DWORD charCount)
        {
            Reset();
            if (charCount == 0)
            {
                return E_INVALIDARG;
            }

            try
            {
                _buffer.assign(charCount, L'\0');
                return S_OK;
            }
            catch (const std::bad_alloc&)
            {
                return E_OUTOFMEMORY;
            }
        }

        PWSTR data()
        {
            return _buffer.empty() ? nullptr : _buffer.data();
        }

        DWORD capacity() const
        {
            return static_cast<DWORD>(_buffer.size());
        }

        void Reset()
        {
            if (!_buffer.empty())
            {
                SecureZeroMemory(_buffer.data(), _buffer.size() * sizeof(wchar_t));
                _buffer.clear();
            }
        }

    private:
        std::vector<wchar_t> _buffer;
    };

    HRESULT AllocCoTaskString(PCWSTR source, PWSTR* value)
    {
        if (value == nullptr)
        {
            return E_POINTER;
        }

        *value = nullptr;
        if (source == nullptr)
        {
            return E_INVALIDARG;
        }

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

    void TraceNativeReportResult(const std::string& accountId, NTSTATUS status, NTSTATUS substatus)
    {
        wchar_t buffer[160]{};
        swprintf_s(
            buffer,
            L"CP_REPORT_RESULT accountId=%S status=%ld substatus=%ld",
            accountId.c_str(),
            static_cast<long>(status),
            static_cast<long>(substatus));
        GaltekTraceLine(buffer);
    }

    void TraceCredentialInstance(const wchar_t* eventName, DWORD instanceId)
    {
        if (eventName == nullptr)
        {
            return;
        }

        wchar_t buffer[128]{};
        swprintf_s(
            buffer,
            L"%ls instance=%lu",
            eventName,
            instanceId);
        GaltekTraceLine(buffer);
    }

    void TraceReportResultEntered(DWORD instanceId, NTSTATUS status, NTSTATUS substatus)
    {
        wchar_t buffer[160]{};
        swprintf_s(
            buffer,
            L"CP_REPORT_RESULT_ENTERED instance=%lu status=%ld substatus=%ld",
            instanceId,
            static_cast<long>(status),
            static_cast<long>(substatus));
        GaltekTraceLine(buffer);
    }

    void TraceReportResultBridgeSent(DWORD instanceId, const char* outcome)
    {
        if (outcome == nullptr)
        {
            return;
        }

        wchar_t buffer[160]{};
        swprintf_s(
            buffer,
            L"CP_REPORT_RESULT_BRIDGE_SENT instance=%lu outcome=%S",
            instanceId,
            outcome);
        GaltekTraceLine(buffer);
    }

    HRESULT InitUnicodeString(PCWSTR value, UNICODE_STRING* target)
    {
        if (value == nullptr || target == nullptr)
        {
            return E_INVALIDARG;
        }

        const size_t charLength = wcslen(value);
        if (charLength > (USHRT_MAX / sizeof(wchar_t)))
        {
            return HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
        }

        const size_t byteLength = charLength * sizeof(wchar_t);
        target->Length = static_cast<USHORT>(byteLength);
        target->MaximumLength = target->Length;
        target->Buffer = const_cast<PWSTR>(value);
        return S_OK;
    }

    HRESULT ProtectPasswordForLogon(SecureWideBuffer& password, ScopedSecureWideBuffer* protectedPassword)
    {
        if (password.empty() || protectedPassword == nullptr)
        {
            return E_INVALIDARG;
        }

        DWORD required = 0;
        CRED_PROTECTION_TYPE protectionType = CredUnprotected;
        if (CredProtectW(
                TRUE,
                password.data(),
                password.length() + 1u,
                nullptr,
                &required,
                &protectionType)
            || GetLastError() != ERROR_INSUFFICIENT_BUFFER
            || required == 0)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        HRESULT hr = protectedPassword->Allocate(required);
        if (FAILED(hr))
        {
            return hr;
        }

        DWORD written = required;
        protectionType = CredUnprotected;
        if (!CredProtectW(
                TRUE,
                password.data(),
                password.length() + 1u,
                protectedPassword->data(),
                &written,
                &protectionType))
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            protectedPassword->Reset();
            return hr;
        }

        return S_OK;
    }

    HRESULT KerbInteractiveUnlockLogonInit(
        PCWSTR domain,
        PCWSTR username,
        PCWSTR password,
        KERB_INTERACTIVE_UNLOCK_LOGON* unlockLogon)
    {
        if (unlockLogon == nullptr)
        {
            return E_POINTER;
        }

        ZeroMemory(unlockLogon, sizeof(*unlockLogon));
        unlockLogon->Logon.MessageType = KerbInteractiveLogon;
        HRESULT hr = InitUnicodeString(domain, &unlockLogon->Logon.LogonDomainName);
        if (SUCCEEDED(hr))
        {
            hr = InitUnicodeString(username, &unlockLogon->Logon.UserName);
        }

        if (SUCCEEDED(hr))
        {
            hr = InitUnicodeString(password, &unlockLogon->Logon.Password);
        }

        return hr;
    }

    HRESULT AddDwordChecked(DWORD left, DWORD right, DWORD* result)
    {
        if (result == nullptr)
        {
            return E_POINTER;
        }

        if (MAXDWORD - left < right)
        {
            return HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
        }

        *result = left + right;
        return S_OK;
    }

    HRESULT CopyPackedUnicodeString(
        const UNICODE_STRING& source,
        BYTE* base,
        DWORD total,
        DWORD* offset,
        UNICODE_STRING* target)
    {
        if (base == nullptr || offset == nullptr || target == nullptr)
        {
            return E_POINTER;
        }

        target->Length = source.Length;
        target->MaximumLength = source.Length;
        target->Buffer = nullptr;

        if (source.Length == 0)
        {
            return S_OK;
        }

        if (source.Buffer == nullptr || source.MaximumLength < source.Length)
        {
            return E_INVALIDARG;
        }

        if ((*offset % sizeof(wchar_t)) != 0
            || total - *offset < source.Length)
        {
            return E_INVALIDARG;
        }

        memcpy(base + *offset, source.Buffer, source.Length);
        target->Buffer = reinterpret_cast<PWSTR>(static_cast<ULONG_PTR>(*offset));
        *offset += source.Length;
        return S_OK;
    }

    HRESULT KerbInteractiveUnlockLogonPack(
        const KERB_INTERACTIVE_UNLOCK_LOGON& unlockLogon,
        BYTE** buffer,
        DWORD* bufferSize)
    {
        if (buffer == nullptr || bufferSize == nullptr)
        {
            return E_POINTER;
        }

        *buffer = nullptr;
        *bufferSize = 0;

        const DWORD domainBytes = unlockLogon.Logon.LogonDomainName.Length;
        const DWORD usernameBytes = unlockLogon.Logon.UserName.Length;
        const DWORD passwordBytes = unlockLogon.Logon.Password.Length;
        DWORD total = sizeof(KERB_INTERACTIVE_UNLOCK_LOGON);
        HRESULT hr = AddDwordChecked(total, domainBytes, &total);
        if (SUCCEEDED(hr))
        {
            hr = AddDwordChecked(total, usernameBytes, &total);
        }

        if (SUCCEEDED(hr))
        {
            hr = AddDwordChecked(total, passwordBytes, &total);
        }

        if (FAILED(hr))
        {
            return hr;
        }

        BYTE* packed = static_cast<BYTE*>(CoTaskMemAlloc(total));
        if (packed == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        ZeroMemory(packed, total);
        auto* packedLogon = reinterpret_cast<KERB_INTERACTIVE_UNLOCK_LOGON*>(packed);
        packedLogon->Logon.MessageType = unlockLogon.Logon.MessageType;

        DWORD offset = sizeof(KERB_INTERACTIVE_UNLOCK_LOGON);
        hr = CopyPackedUnicodeString(
            unlockLogon.Logon.LogonDomainName,
            packed,
            total,
            &offset,
            &packedLogon->Logon.LogonDomainName);
        if (SUCCEEDED(hr))
        {
            hr = CopyPackedUnicodeString(
                unlockLogon.Logon.UserName,
                packed,
                total,
                &offset,
                &packedLogon->Logon.UserName);
        }

        if (SUCCEEDED(hr))
        {
            hr = CopyPackedUnicodeString(
                unlockLogon.Logon.Password,
                packed,
                total,
                &offset,
                &packedLogon->Logon.Password);
        }

        if (FAILED(hr) || offset != total)
        {
            SecureZeroMemory(packed, total);
            CoTaskMemFree(packed);
            return FAILED(hr) ? hr : E_FAIL;
        }

        *buffer = packed;
        *bufferSize = total;
        return S_OK;
    }

    HRESULT RetrieveNegotiateAuthPackage(ULONG* authPackage)
    {
        if (authPackage == nullptr)
        {
            return E_POINTER;
        }

        *authPackage = 0;
        HANDLE lsa = nullptr;
        NTSTATUS status = LsaConnectUntrusted(&lsa);
        if (status != 0)
        {
            return HRESULT_FROM_WIN32(LsaNtStatusToWinError(status));
        }

        LSA_STRING packageName{};
        packageName.Buffer = const_cast<PCHAR>("Negotiate");
        packageName.Length = static_cast<USHORT>(strlen(packageName.Buffer));
        packageName.MaximumLength = packageName.Length;

        status = LsaLookupAuthenticationPackage(lsa, &packageName, authPackage);
        LsaDeregisterLogonProcess(lsa);
        return status == 0 ? S_OK : HRESULT_FROM_WIN32(LsaNtStatusToWinError(status));
    }
}

GaltekCredential::GaltekCredential(const BridgeActivationIdentity& identity)
    : _referenceCount(1),
      _instanceId(static_cast<DWORD>(InterlockedIncrement(&g_nextCredentialInstanceId))),
      _events(nullptr),
      _activationId(identity.activationId),
      _accountId(identity.accountId),
      _userSid(identity.userSid),
      _domain(identity.domain),
      _username(identity.username),
      _autoSubmitRequested(identity.autoSubmitRequested),
      _autoSubmitConsumed(false),
      _acquireAttempted(false),
      _logonResultReported(false)
{
    InterlockedIncrement(&g_objectCount);
    TraceCredentialInstance(L"CP_CREDENTIAL_CREATED", _instanceId);
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

GaltekCredential::~GaltekCredential()
{
    TraceCredentialInstance(L"CP_CREDENTIAL_DESTROYED", _instanceId);
    if (_events != nullptr)
    {
        _events->Release();
        _events = nullptr;
    }
}

HRESULT GaltekCredential::Advise(ICredentialProviderCredentialEvents* events)
{
    if (_events != nullptr)
    {
        _events->Release();
        _events = nullptr;
    }

    if (events != nullptr)
    {
        events->AddRef();
        _events = events;
    }

    return S_OK;
}

HRESULT GaltekCredential::UnAdvise()
{
    if (_events != nullptr)
    {
        _events->Release();
        _events = nullptr;
    }

    return S_OK;
}

HRESULT GaltekCredential::SetSelected(BOOL* autoLogon)
{
    if (autoLogon == nullptr)
    {
        return E_POINTER;
    }

    *autoLogon = (_autoSubmitRequested && !_autoSubmitConsumed) ? TRUE : FALSE;
    _autoSubmitConsumed = true;
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
    if (fieldState == nullptr || fieldInteractiveState == nullptr)
    {
        return E_POINTER;
    }

    *fieldInteractiveState = CPFIS_NONE;
    switch (fieldId)
    {
    case GaltekFieldTitle:
    case GaltekFieldSubtitle:
        *fieldState = CPFS_DISPLAY_IN_BOTH;
        return S_OK;
    case GaltekFieldSubmit:
        *fieldState = CPFS_DISPLAY_IN_SELECTED_TILE;
        return S_OK;
    default:
        *fieldState = CPFS_HIDDEN;
        return E_INVALIDARG;
    }
}

HRESULT GaltekCredential::GetStringValue(DWORD fieldId, PWSTR* value)
{
    if (value == nullptr)
    {
        return E_POINTER;
    }

    *value = nullptr;
    switch (fieldId)
    {
    case GaltekFieldTitle:
        return AllocCoTaskString(L"Galtek Classroom", value);
    case GaltekFieldSubtitle:
        return AllocCoTaskString(L"Cuenta escolar", value);
    default:
        return E_INVALIDARG;
    }
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
    if (adjacentTo == nullptr)
    {
        return E_POINTER;
    }

    if (fieldId != GaltekFieldSubmit)
    {
        *adjacentTo = 0;
        return E_INVALIDARG;
    }

    *adjacentTo = GaltekFieldSubtitle;
    return S_OK;
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
    GaltekTraceEventWithAccount(L"CP_GET_SERIALIZATION_ENTERED", _accountId);

    if (_acquireAttempted
        || _activationId.empty()
        || _domain.empty()
        || _username.empty()
        || _userSid.empty())
    {
        return S_OK;
    }

    _acquireAttempted = true;
    GaltekTraceEventWithAccount(L"CP_IDENTITY_READY", _accountId);

    BridgeClient bridge;
    SecureWideBuffer password;
    if (bridge.AcquirePendingCredential(
            _activationId,
            kAcquireTimeoutMilliseconds,
            &password) != BridgeAcquireStatus::Acquired)
    {
        return S_OK;
    }
    GaltekTraceEventWithAccount(L"CP_CREDENTIAL_ACQUIRED", _accountId);

    ULONG authPackage = 0;
    HRESULT hr = RetrieveNegotiateAuthPackage(&authPackage);
    if (FAILED(hr))
    {
        ReportLocalSerializationFailed();
        return S_OK;
    }
    GaltekTraceEventWithAccount(L"CP_AUTH_PACKAGE_SUCCESS", _accountId);

    ScopedSecureWideBuffer protectedPassword;
    hr = ProtectPasswordForLogon(password, &protectedPassword);
    if (FAILED(hr))
    {
        ReportLocalSerializationFailed();
        return S_OK;
    }
    GaltekTraceEventWithAccount(L"CP_CREDPROTECT_SUCCESS", _accountId);

    KERB_INTERACTIVE_UNLOCK_LOGON unlockLogon{};
    hr = KerbInteractiveUnlockLogonInit(
        _domain.c_str(),
        _username.c_str(),
        protectedPassword.data(),
        &unlockLogon);
    if (FAILED(hr))
    {
        ReportLocalSerializationFailed();
        return S_OK;
    }

    BYTE* packed = nullptr;
    DWORD packedSize = 0;
    hr = KerbInteractiveUnlockLogonPack(unlockLogon, &packed, &packedSize);
    if (FAILED(hr) || packed == nullptr || packedSize == 0)
    {
        if (packed != nullptr)
        {
            SecureZeroMemory(packed, packedSize);
            CoTaskMemFree(packed);
        }

        ReportLocalSerializationFailed();
        return S_OK;
    }
    GaltekTraceEventWithAccount(L"CP_PACK_SUCCESS", _accountId);

    serialization->ulAuthenticationPackage = authPackage;
    serialization->clsidCredentialProvider = CLSID_GaltekClassroomCredentialProvider;
    serialization->cbSerialization = packedSize;
    serialization->rgbSerialization = packed;
    *response = CPGSR_RETURN_CREDENTIAL_FINISHED;
    TraceCredentialInstance(L"CP_SERIALIZATION_RETURNED", _instanceId);
    GaltekTraceEventWithAccount(L"CP_SERIALIZATION_RETURNED", _accountId);
    return S_OK;
}

HRESULT GaltekCredential::ReportResult(
    NTSTATUS status,
    NTSTATUS substatus,
    PWSTR* statusText,
    CREDENTIAL_PROVIDER_STATUS_ICON* statusIcon)
{
    if (statusText == nullptr || statusIcon == nullptr)
    {
        return E_POINTER;
    }

    *statusText = nullptr;
    *statusIcon = CPSI_NONE;
    TraceReportResultEntered(_instanceId, status, substatus);
    TraceNativeReportResult(_accountId, status, substatus);
    if (!_activationId.empty() && !_logonResultReported)
    {
        const char* outcome = status == 0 ? "SUCCESS" : "FAILED";
        BridgeClient bridge;
        if (bridge.ReportLogonResult(_activationId, outcome, 750))
        {
            TraceReportResultBridgeSent(_instanceId, outcome);
        }

        _logonResultReported = true;
    }

    return S_OK;
}

HRESULT GaltekCredential::GetUserSid(PWSTR* sid)
{
    if (sid == nullptr)
    {
        return E_POINTER;
    }

    *sid = nullptr;
    if (_userSid.empty())
    {
        return E_FAIL;
    }

    return AllocCoTaskString(_userSid.c_str(), sid);
}

void GaltekCredential::ReportLocalSerializationFailed()
{
    if (_activationId.empty() || _logonResultReported)
    {
        return;
    }

    BridgeClient bridge;
    if (bridge.ReportLogonResult(_activationId, "LOCAL_SERIALIZATION_FAILED", 750))
    {
        TraceReportResultBridgeSent(_instanceId, "LOCAL_SERIALIZATION_FAILED");
    }

    _logonResultReported = true;
}

#ifdef GALTEK_CREDENTIAL_PROVIDER_TESTS
HRESULT GaltekTestPackKerbInteractiveUnlockLogon(
    PCWSTR domain,
    PCWSTR username,
    PCWSTR password,
    BYTE** buffer,
    DWORD* bufferSize)
{
    KERB_INTERACTIVE_UNLOCK_LOGON unlockLogon{};
    HRESULT hr = KerbInteractiveUnlockLogonInit(domain, username, password, &unlockLogon);
    if (FAILED(hr))
    {
        if (buffer != nullptr)
        {
            *buffer = nullptr;
        }

        if (bufferSize != nullptr)
        {
            *bufferSize = 0;
        }

        return hr;
    }

    return KerbInteractiveUnlockLogonPack(unlockLogon, buffer, bufferSize);
}

HRESULT GaltekTestRetrieveNegotiateAuthPackage(ULONG* authPackage)
{
    return RetrieveNegotiateAuthPackage(authPackage);
}
#endif
