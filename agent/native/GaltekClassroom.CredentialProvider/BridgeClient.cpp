#include "BridgeClient.h"

#include <objbase.h>
#include <cstring>
#include <string>
#include <vector>

namespace
{
    constexpr wchar_t kPipeName[] = L"\\\\.\\pipe\\GaltekClassroom.CredentialProvider.v1";
    constexpr DWORD kMaxMessageBytes = 8u * 1024u;
    constexpr BYTE kSecretMagic[] = { 'G', 'C', 'P', 'A', 'S' };
    constexpr DWORD kMaxPasswordBytes = 1024u * 2u;

    void WriteBigEndianLength(BYTE* target, DWORD length)
    {
        target[0] = static_cast<BYTE>((length >> 24) & 0xFF);
        target[1] = static_cast<BYTE>((length >> 16) & 0xFF);
        target[2] = static_cast<BYTE>((length >> 8) & 0xFF);
        target[3] = static_cast<BYTE>(length & 0xFF);
    }

    bool ReadBigEndianLength(const BYTE* source, DWORD* length)
    {
        if (length == nullptr)
        {
            return false;
        }

        *length = (static_cast<DWORD>(source[0]) << 24)
            | (static_cast<DWORD>(source[1]) << 16)
            | (static_cast<DWORD>(source[2]) << 8)
            | static_cast<DWORD>(source[3]);
        return *length > 0 && *length <= kMaxMessageBytes;
    }

    bool WriteAll(HANDLE pipe, const BYTE* data, DWORD length)
    {
        DWORD offset = 0;
        while (offset < length)
        {
            DWORD written = 0;
            if (!WriteFile(pipe, data + offset, length - offset, &written, nullptr) || written == 0)
            {
                return false;
            }

            offset += written;
        }

        return FlushFileBuffers(pipe) != FALSE;
    }

    bool ReadAll(HANDLE pipe, BYTE* data, DWORD length)
    {
        DWORD offset = 0;
        while (offset < length)
        {
            DWORD read = 0;
            if (!ReadFile(pipe, data + offset, length - offset, &read, nullptr) || read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    std::string CreateRequestId()
    {
        GUID requestId{};
        if (FAILED(CoCreateGuid(&requestId)))
        {
            return "00000000-0000-0000-0000-000000000000";
        }

        wchar_t buffer[39]{};
        if (StringFromGUID2(requestId, buffer, ARRAYSIZE(buffer)) == 0)
        {
            return "00000000-0000-0000-0000-000000000000";
        }

        char narrow[39]{};
        int converted = WideCharToMultiByte(
            CP_UTF8,
            0,
            buffer,
            -1,
            narrow,
            ARRAYSIZE(narrow),
            nullptr,
            nullptr);
        if (converted <= 0)
        {
            return "00000000-0000-0000-0000-000000000000";
        }

        return std::string(narrow);
    }

    std::vector<BYTE> FrameJson(const std::string& json)
    {
        std::vector<BYTE> frame(sizeof(DWORD) + json.size());
        WriteBigEndianLength(frame.data(), static_cast<DWORD>(json.size()));
        memcpy(frame.data() + sizeof(DWORD), json.data(), json.size());
        return frame;
    }

    bool ReadFrameBytes(HANDLE pipe, std::vector<BYTE>* payload)
    {
        BYTE lengthBuffer[sizeof(DWORD)]{};
        if (!ReadAll(pipe, lengthBuffer, sizeof(lengthBuffer)))
        {
            return false;
        }

        DWORD length = 0;
        if (!ReadBigEndianLength(lengthBuffer, &length))
        {
            return false;
        }

        payload->assign(length, 0);
        if (!ReadAll(pipe, payload->data(), length))
        {
            return false;
        }

        return true;
    }

    bool ReadFrame(HANDLE pipe, std::string* json)
    {
        std::vector<BYTE> payload;
        if (!ReadFrameBytes(pipe, &payload))
        {
            return false;
        }

        json->assign(reinterpret_cast<const char*>(payload.data()), payload.size());
        return true;
    }

    bool FlushUtf8Segment(const std::string& segment, std::wstring* value)
    {
        if (segment.empty())
        {
            return true;
        }

        const int required = MultiByteToWideChar(
            CP_UTF8,
            MB_ERR_INVALID_CHARS,
            segment.data(),
            static_cast<int>(segment.size()),
            nullptr,
            0);
        if (required <= 0)
        {
            return false;
        }

        const size_t start = value->size();
        value->resize(start + static_cast<size_t>(required));
        return MultiByteToWideChar(
            CP_UTF8,
            MB_ERR_INVALID_CHARS,
            segment.data(),
            static_cast<int>(segment.size()),
            value->data() + start,
            required) == required;
    }

    int HexValue(char value)
    {
        if (value >= '0' && value <= '9')
        {
            return value - '0';
        }

        if (value >= 'a' && value <= 'f')
        {
            return 10 + value - 'a';
        }

        if (value >= 'A' && value <= 'F')
        {
            return 10 + value - 'A';
        }

        return -1;
    }

    bool FindJsonStringWide(
        const std::string& json,
        const char* key,
        std::wstring* value)
    {
        value->clear();

        const std::string prefix = std::string("\"") + key + "\":\"";
        const size_t start = json.find(prefix);
        if (start == std::string::npos)
        {
            return false;
        }

        std::string utf8Segment;
        size_t index = start + prefix.size();
        while (index < json.size())
        {
            const char current = json[index++];
            if (current == '"')
            {
                return FlushUtf8Segment(utf8Segment, value);
            }

            if (current != '\\')
            {
                utf8Segment.push_back(current);
                continue;
            }

            if (index >= json.size() || !FlushUtf8Segment(utf8Segment, value))
            {
                return false;
            }

            utf8Segment.clear();
            const char escaped = json[index++];
            switch (escaped)
            {
            case '"':
            case '\\':
            case '/':
                value->push_back(static_cast<wchar_t>(escaped));
                break;
            case 'b':
                value->push_back(L'\b');
                break;
            case 'f':
                value->push_back(L'\f');
                break;
            case 'n':
                value->push_back(L'\n');
                break;
            case 'r':
                value->push_back(L'\r');
                break;
            case 't':
                value->push_back(L'\t');
                break;
            case 'u':
            {
                if (json.size() - index < 4)
                {
                    return false;
                }

                int code = 0;
                for (int digit = 0; digit < 4; digit++)
                {
                    const int hex = HexValue(json[index++]);
                    if (hex < 0)
                    {
                        return false;
                    }

                    code = (code << 4) | hex;
                }

                value->push_back(static_cast<wchar_t>(code));
                break;
            }
            default:
                return false;
            }
        }

        return false;
    }

    bool FindJsonStringNarrow(
        const std::string& json,
        const char* key,
        std::string* value)
    {
        std::wstring wide;
        if (!FindJsonStringWide(json, key, &wide))
        {
            return false;
        }

        value->clear();
        for (wchar_t ch : wide)
        {
            if (ch > 0x7F)
            {
                return false;
            }

            value->push_back(static_cast<char>(ch));
        }

        return !value->empty();
    }

    bool FindJsonBool(
        const std::string& json,
        const char* key,
        bool* value)
    {
        if (value == nullptr)
        {
            return false;
        }

        const std::string prefix = std::string("\"") + key + "\":";
        const size_t start = json.find(prefix);
        if (start == std::string::npos)
        {
            return false;
        }

        const size_t valueStart = start + prefix.size();
        if (json.compare(valueStart, 4, "true") == 0)
        {
            *value = true;
            return true;
        }

        if (json.compare(valueStart, 5, "false") == 0)
        {
            *value = false;
            return true;
        }

        return false;
    }

    bool FindJsonNumber(
        const std::string& json,
        const char* key,
        long long* value)
    {
        if (value == nullptr)
        {
            return false;
        }

        const std::string prefix = std::string("\"") + key + "\":";
        const size_t start = json.find(prefix);
        if (start == std::string::npos)
        {
            return false;
        }

        size_t index = start + prefix.size();
        long long result = 0;
        bool foundDigit = false;
        while (index < json.size() && json[index] >= '0' && json[index] <= '9')
        {
            foundDigit = true;
            result = result * 10 + (json[index] - '0');
            index++;
        }

        if (!foundDigit)
        {
            return false;
        }

        *value = result;
        return true;
    }

    bool OpenBridgePipe(DWORD timeoutMilliseconds, bool overlapped, HANDLE* pipe)
    {
        *pipe = INVALID_HANDLE_VALUE;
        if (!WaitNamedPipeW(kPipeName, timeoutMilliseconds))
        {
            return false;
        }

        *pipe = CreateFileW(
            kPipeName,
            GENERIC_READ | GENERIC_WRITE,
            0,
            nullptr,
            OPEN_EXISTING,
            overlapped ? FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED : FILE_ATTRIBUTE_NORMAL,
            nullptr);

        return *pipe != INVALID_HANDLE_VALUE;
    }

    bool OpenBridgePipe(DWORD timeoutMilliseconds, HANDLE* pipe)
    {
        return OpenBridgePipe(timeoutMilliseconds, false, pipe);
    }

    bool SendRequestReadBytes(
        const std::string& request,
        DWORD timeoutMilliseconds,
        SecureByteBuffer* response)
    {
        HANDLE pipe = INVALID_HANDLE_VALUE;
        if (!OpenBridgePipe(timeoutMilliseconds, &pipe))
        {
            return false;
        }

        const std::vector<BYTE> frame = FrameJson(request);
        std::vector<BYTE> payload;
        const bool ok = WriteAll(pipe, frame.data(), static_cast<DWORD>(frame.size()))
            && ReadFrameBytes(pipe, &payload);
        CloseHandle(pipe);

        if (!ok)
        {
            return false;
        }

        *response = SecureByteBuffer(std::move(payload));
        return true;
    }

    bool WaitForIo(
        HANDLE file,
        OVERLAPPED* overlapped,
        HANDLE cancelEvent,
        DWORD* transferred)
    {
        HANDLE events[] = { overlapped->hEvent, cancelEvent };
        const DWORD wait = WaitForMultipleObjects(ARRAYSIZE(events), events, FALSE, INFINITE);
        if (wait == WAIT_OBJECT_0 + 1)
        {
            CancelIoEx(file, overlapped);
            return false;
        }

        if (wait != WAIT_OBJECT_0)
        {
            CancelIoEx(file, overlapped);
            return false;
        }

        return GetOverlappedResult(file, overlapped, transferred, FALSE) != FALSE;
    }

    bool ReadAllCancelable(HANDLE pipe, BYTE* data, DWORD length, HANDLE cancelEvent)
    {
        DWORD offset = 0;
        while (offset < length)
        {
            OVERLAPPED overlapped{};
            overlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (overlapped.hEvent == nullptr)
            {
                return false;
            }

            DWORD read = 0;
            BOOL ok = ReadFile(pipe, data + offset, length - offset, nullptr, &overlapped);
            if (!ok && GetLastError() == ERROR_IO_PENDING)
            {
                ok = WaitForIo(pipe, &overlapped, cancelEvent, &read);
            }
            else if (ok)
            {
                ok = GetOverlappedResult(pipe, &overlapped, &read, FALSE);
            }

            CloseHandle(overlapped.hEvent);
            if (!ok || read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    bool ReadFrameCancelable(HANDLE pipe, HANDLE cancelEvent, std::string* json)
    {
        BYTE lengthBuffer[sizeof(DWORD)]{};
        if (!ReadAllCancelable(pipe, lengthBuffer, sizeof(lengthBuffer), cancelEvent))
        {
            return false;
        }

        DWORD length = 0;
        if (!ReadBigEndianLength(lengthBuffer, &length))
        {
            return false;
        }

        std::vector<BYTE> payload(length, 0);
        if (!ReadAllCancelable(pipe, payload.data(), length, cancelEvent))
        {
            return false;
        }

        json->assign(reinterpret_cast<const char*>(payload.data()), payload.size());
        SecureZeroMemory(payload.data(), payload.size());
        return true;
    }

    bool SendRequestReadJson(
        const std::string& request,
        DWORD timeoutMilliseconds,
        std::string* response)
    {
        SecureByteBuffer payload;
        if (!SendRequestReadBytes(request, timeoutMilliseconds, &payload))
        {
            return false;
        }

        response->assign(reinterpret_cast<const char*>(payload.data()), payload.size());
        return true;
    }

    bool ReadUInt16(const SecureByteBuffer& payload, DWORD* offset, WORD* value)
    {
        if (payload.size() - *offset < sizeof(WORD))
        {
            return false;
        }

        const BYTE* data = payload.data() + *offset;
        *value = static_cast<WORD>((static_cast<WORD>(data[0]) << 8) | data[1]);
        *offset += sizeof(WORD);
        return true;
    }

    bool ReadInt32(const SecureByteBuffer& payload, DWORD* offset, DWORD* value)
    {
        if (payload.size() - *offset < sizeof(DWORD))
        {
            return false;
        }

        const BYTE* data = payload.data() + *offset;
        *value = (static_cast<DWORD>(data[0]) << 24)
            | (static_cast<DWORD>(data[1]) << 16)
            | (static_cast<DWORD>(data[2]) << 8)
            | data[3];
        *offset += sizeof(DWORD);
        return true;
    }
}

SecureByteBuffer::SecureByteBuffer(std::vector<BYTE>&& value)
    : _buffer(std::move(value))
{
}

SecureByteBuffer::SecureByteBuffer(SecureByteBuffer&& other) noexcept
    : _buffer(std::move(other._buffer))
{
}

SecureByteBuffer& SecureByteBuffer::operator=(SecureByteBuffer&& other) noexcept
{
    if (this != &other)
    {
        Reset();
        _buffer = std::move(other._buffer);
    }

    return *this;
}

SecureByteBuffer::~SecureByteBuffer()
{
    Reset();
}

BYTE* SecureByteBuffer::data()
{
    return _buffer.data();
}

const BYTE* SecureByteBuffer::data() const
{
    return _buffer.data();
}

DWORD SecureByteBuffer::size() const
{
    return static_cast<DWORD>(_buffer.size());
}

bool SecureByteBuffer::empty() const
{
    return _buffer.empty();
}

void SecureByteBuffer::Reset()
{
    if (!_buffer.empty())
    {
        SecureZeroMemory(_buffer.data(), _buffer.size());
        _buffer.clear();
    }
}

SecureWideBuffer::SecureWideBuffer(SecureWideBuffer&& other) noexcept
    : _buffer(std::move(other._buffer)),
      _length(other._length)
{
    other._length = 0;
}

SecureWideBuffer& SecureWideBuffer::operator=(SecureWideBuffer&& other) noexcept
{
    if (this != &other)
    {
        Reset();
        _buffer = std::move(other._buffer);
        _length = other._length;
        other._length = 0;
    }

    return *this;
}

SecureWideBuffer::~SecureWideBuffer()
{
    Reset();
}

bool SecureWideBuffer::AssignUtf16LittleEndian(const BYTE* data, DWORD byteLength)
{
    Reset();
    if (data == nullptr || byteLength == 0 || (byteLength % sizeof(wchar_t)) != 0)
    {
        return false;
    }

    const DWORD chars = byteLength / sizeof(wchar_t);
    _buffer.assign(static_cast<size_t>(chars) + 1u, L'\0');
    memcpy(_buffer.data(), data, byteLength);
    _length = chars;
    return true;
}

PWSTR SecureWideBuffer::data()
{
    return _buffer.empty() ? nullptr : _buffer.data();
}

PCWSTR SecureWideBuffer::data() const
{
    return _buffer.empty() ? nullptr : _buffer.data();
}

DWORD SecureWideBuffer::length() const
{
    return _length;
}

bool SecureWideBuffer::empty() const
{
    return _length == 0;
}

void SecureWideBuffer::Reset()
{
    if (!_buffer.empty())
    {
        SecureZeroMemory(_buffer.data(), _buffer.size() * sizeof(wchar_t));
        _buffer.clear();
    }

    _length = 0;
}

BridgeActivationStatus BridgeClient::GetPendingActivationMetadata(DWORD timeoutMilliseconds) const
{
    BridgeActivationStatus status = BridgeActivationStatus::ServiceUnavailable;
    const std::string request =
        "{\"protocolVersion\":1,\"requestId\":\""
        + CreateRequestId()
        + "\",\"operation\":\"GET_PENDING_ACTIVATION_METADATA\"}";

    std::string response;
    if (SendRequestReadJson(request, timeoutMilliseconds, &response))
    {
        if (response.find("\"status\":\"SUCCESS\"") != std::string::npos
            && response.find("\"activationStatus\":\"PENDING\"") != std::string::npos)
        {
            status = BridgeActivationStatus::PendingActivation;
        }
        else if (response.find("\"status\":\"SUCCESS\"") != std::string::npos)
        {
            status = BridgeActivationStatus::NoActivation;
        }
    }

    return status;
}

bool BridgeClient::GetPendingActivationIdentity(
    DWORD timeoutMilliseconds,
    BridgeActivationIdentity* identity) const
{
    if (identity == nullptr)
    {
        return false;
    }

    *identity = BridgeActivationIdentity{};
    const std::string request =
        "{\"protocolVersion\":1,\"requestId\":\""
        + CreateRequestId()
        + "\",\"operation\":\"GET_PENDING_ACTIVATION_IDENTITY\"}";

    std::string response;
    if (!SendRequestReadJson(request, timeoutMilliseconds, &response))
    {
        return false;
    }

    if (response.find("\"status\":\"SUCCESS\"") == std::string::npos
        || response.find("\"activationStatus\":\"PENDING\"") == std::string::npos)
    {
        return false;
    }

    return FindJsonStringNarrow(response, "activationId", &identity->activationId)
        && FindJsonStringNarrow(response, "accountId", &identity->accountId)
        && FindJsonStringWide(response, "userSid", &identity->userSid)
        && FindJsonStringWide(response, "domain", &identity->domain)
        && FindJsonStringWide(response, "username", &identity->username)
        && FindJsonBool(response, "autoSubmitRequested", &identity->autoSubmitRequested)
        && !identity->userSid.empty()
        && !identity->domain.empty()
        && !identity->username.empty();
}

BridgeAcquireStatus BridgeClient::AcquirePendingCredential(
    const std::string& activationId,
    DWORD timeoutMilliseconds,
    SecureWideBuffer* password) const
{
    if (activationId.empty() || password == nullptr)
    {
        return BridgeAcquireStatus::NoCredential;
    }

    password->Reset();
    const std::string request =
        "{\"protocolVersion\":1,\"requestId\":\""
        + CreateRequestId()
        + "\",\"operation\":\"ACQUIRE_PENDING_CREDENTIAL\",\"activationId\":\""
        + activationId
        + "\"}";

    SecureByteBuffer response;
    if (!SendRequestReadBytes(request, timeoutMilliseconds, &response))
    {
        return BridgeAcquireStatus::ServiceUnavailable;
    }

    DWORD offset = 0;
    if (response.size() < ARRAYSIZE(kSecretMagic)
        || memcmp(response.data(), kSecretMagic, ARRAYSIZE(kSecretMagic)) != 0)
    {
        return BridgeAcquireStatus::NoCredential;
    }

    offset += ARRAYSIZE(kSecretMagic);

    WORD version = 0;
    WORD status = 0;
    WORD activationLength = 0;
    if (!ReadUInt16(response, &offset, &version)
        || version != 1
        || !ReadUInt16(response, &offset, &status)
        || !ReadUInt16(response, &offset, &activationLength)
        || activationLength == 0
        || response.size() - offset < activationLength)
    {
        return BridgeAcquireStatus::NoCredential;
    }

    std::string responseActivationId(
        reinterpret_cast<const char*>(response.data() + offset),
        activationLength);
    offset += activationLength;
    if (responseActivationId != activationId)
    {
        return BridgeAcquireStatus::NoCredential;
    }

    DWORD passwordByteLength = 0;
    if (!ReadInt32(response, &offset, &passwordByteLength)
        || passwordByteLength > kMaxPasswordBytes
        || (passwordByteLength % sizeof(wchar_t)) != 0
        || response.size() - offset != passwordByteLength)
    {
        return BridgeAcquireStatus::NoCredential;
    }

    if (status != 0)
    {
        return passwordByteLength == 0
            ? BridgeAcquireStatus::NoCredential
            : BridgeAcquireStatus::NoCredential;
    }

    if (passwordByteLength == 0
        || !password->AssignUtf16LittleEndian(response.data() + offset, passwordByteLength))
    {
        return BridgeAcquireStatus::NoCredential;
    }

    return BridgeAcquireStatus::Acquired;
}

bool BridgeClient::WaitForActivationChange(
    long long observedGeneration,
    HANDLE cancelEvent,
    long long* generation) const
{
    if (cancelEvent == nullptr || generation == nullptr)
    {
        return false;
    }

    *generation = observedGeneration;
    const std::string request =
        "{\"protocolVersion\":1,\"requestId\":\""
        + CreateRequestId()
        + "\",\"operation\":\"WAIT_FOR_ACTIVATION_CHANGE\",\"observedGeneration\":"
        + std::to_string(observedGeneration)
        + "}";

    HANDLE pipe = INVALID_HANDLE_VALUE;
    if (!OpenBridgePipe(250, true, &pipe))
    {
        return false;
    }

    const std::vector<BYTE> frame = FrameJson(request);
    std::string response;
    const bool ok = WriteAll(pipe, frame.data(), static_cast<DWORD>(frame.size()))
        && ReadFrameCancelable(pipe, cancelEvent, &response);
    CloseHandle(pipe);

    if (!ok
        || response.find("\"status\":\"SUCCESS\"") == std::string::npos)
    {
        return false;
    }

    return FindJsonNumber(response, "generation", generation);
}

bool BridgeClient::ReportLogonResult(
    const std::string& activationId,
    const char* outcome,
    DWORD timeoutMilliseconds) const
{
    if (activationId.empty() || outcome == nullptr)
    {
        return false;
    }

    const std::string request =
        "{\"protocolVersion\":1,\"requestId\":\""
        + CreateRequestId()
        + "\",\"operation\":\"REPORT_LOGON_RESULT\",\"activationId\":\""
        + activationId
        + "\",\"outcome\":\""
        + outcome
        + "\"}";

    std::string response;
    return SendRequestReadJson(request, timeoutMilliseconds, &response)
        && response.find("\"status\":\"SUCCESS\"") != std::string::npos;
}
