#pragma once

#include <windows.h>

#include <string>
#include <vector>

enum class BridgeActivationStatus
{
    ServiceUnavailable,
    NoActivation,
    PendingActivation
};

enum class BridgeAcquireStatus
{
    ServiceUnavailable,
    NoCredential,
    Acquired
};

struct BridgeActivationIdentity
{
    std::string activationId;
    std::string accountId;
    std::wstring userSid;
    std::wstring domain;
    std::wstring username;
    bool autoSubmitRequested = false;
};

class SecureByteBuffer
{
public:
    SecureByteBuffer() = default;
    explicit SecureByteBuffer(std::vector<BYTE>&& value);
    SecureByteBuffer(const SecureByteBuffer&) = delete;
    SecureByteBuffer& operator=(const SecureByteBuffer&) = delete;
    SecureByteBuffer(SecureByteBuffer&& other) noexcept;
    SecureByteBuffer& operator=(SecureByteBuffer&& other) noexcept;
    ~SecureByteBuffer();

    BYTE* data();
    const BYTE* data() const;
    DWORD size() const;
    bool empty() const;
    void Reset();

private:
    std::vector<BYTE> _buffer;
};

class SecureWideBuffer
{
public:
    SecureWideBuffer() = default;
    SecureWideBuffer(const SecureWideBuffer&) = delete;
    SecureWideBuffer& operator=(const SecureWideBuffer&) = delete;
    SecureWideBuffer(SecureWideBuffer&& other) noexcept;
    SecureWideBuffer& operator=(SecureWideBuffer&& other) noexcept;
    ~SecureWideBuffer();

    bool AssignUtf16LittleEndian(const BYTE* data, DWORD byteLength);
    PWSTR data();
    PCWSTR data() const;
    DWORD length() const;
    bool empty() const;
    void Reset();

private:
    std::vector<wchar_t> _buffer;
    DWORD _length = 0;
};

class BridgeClient
{
public:
    BridgeActivationStatus GetPendingActivationMetadata(DWORD timeoutMilliseconds) const;
    bool GetPendingActivationIdentity(
        DWORD timeoutMilliseconds,
        BridgeActivationIdentity* identity) const;
    BridgeAcquireStatus AcquirePendingCredential(
        const std::string& activationId,
        DWORD timeoutMilliseconds,
        SecureWideBuffer* password) const;
    bool WaitForActivationChange(
        long long observedGeneration,
        HANDLE cancelEvent,
        long long* generation) const;
    bool ReportLogonResult(
        const std::string& activationId,
        const char* outcome,
        DWORD timeoutMilliseconds) const;
};
