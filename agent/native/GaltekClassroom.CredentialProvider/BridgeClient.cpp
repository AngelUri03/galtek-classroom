#include "BridgeClient.h"
#include "NativeTrace.h"

#include <objbase.h>
#include <cstring>
#include <string>
#include <vector>

namespace
{
    constexpr wchar_t kDefaultPipeName[] = L"\\\\.\\pipe\\GaltekClassroom.CredentialProvider.v1";
    constexpr DWORD kMaxMessageBytes = 8u * 1024u;
    constexpr BYTE kSecretMagic[] = { 'G', 'C', 'P', 'A', 'S' };
    constexpr DWORD kMaxPasswordBytes = 1024u * 2u;

    struct BridgeRequestDiagnostics
    {
        const wchar_t* stage = L"transport";
        const wchar_t* rejectReason = L"transport";
        DWORD win32 = ERROR_SUCCESS;
        bool timeout = false;
        bool traceIdentity = false;
    };

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

    ULONGLONG TimeoutDeadline(DWORD timeoutMilliseconds)
    {
        return GetTickCount64() + timeoutMilliseconds;
    }

    DWORD RemainingTimeout(ULONGLONG deadline)
    {
        if (deadline == MAXULONGLONG)
        {
            return INFINITE;
        }

        const ULONGLONG now = GetTickCount64();
        if (now >= deadline)
        {
            return 0;
        }

        const ULONGLONG remaining = deadline - now;
        return remaining > MAXDWORD ? MAXDWORD : static_cast<DWORD>(remaining);
    }

    DWORD ElapsedMilliseconds(ULONGLONG started)
    {
        const ULONGLONG elapsed = GetTickCount64() - started;
        return elapsed > MAXDWORD ? MAXDWORD : static_cast<DWORD>(elapsed);
    }

    void TraceIdentityTimeout(const BridgeRequestDiagnostics& diagnostics)
    {
        wchar_t buffer[128]{};
        swprintf_s(
            buffer,
            L"CP_IDENTITY_QUERY_TIMEOUT stage=%ls win32=%lu",
            diagnostics.stage,
            diagnostics.win32);
        GaltekTraceLine(buffer);
    }

    void TraceIdentityReject(const wchar_t* reason)
    {
        std::wstring line = L"CP_IDENTITY_QUERY_REJECT reason=";
        line.append(reason == nullptr ? L"unknown" : reason);
        GaltekTraceLine(line);
    }

    void TraceIdentityReject(const BridgeRequestDiagnostics& diagnostics)
    {
        if (diagnostics.timeout)
        {
            TraceIdentityTimeout(diagnostics);
            return;
        }

        wchar_t buffer[128]{};
        swprintf_s(
            buffer,
            L"CP_IDENTITY_QUERY_REJECT reason=%ls stage=%ls win32=%lu",
            diagnostics.rejectReason,
            diagnostics.stage,
            diagnostics.win32);
        GaltekTraceLine(buffer);
    }

    bool WaitForIo(
        HANDLE file,
        OVERLAPPED* overlapped,
        HANDLE cancelEvent,
        ULONGLONG deadline,
        DWORD* transferred)
    {
        HANDLE events[2] = { overlapped->hEvent, cancelEvent };
        const DWORD eventCount = cancelEvent == nullptr ? 1u : 2u;
        const DWORD wait = WaitForMultipleObjects(
            eventCount,
            events,
            FALSE,
            RemainingTimeout(deadline));
        if (wait != WAIT_OBJECT_0)
        {
            CancelIoEx(file, overlapped);
            DWORD ignored = 0;
            GetOverlappedResult(file, overlapped, &ignored, TRUE);
            return false;
        }

        return GetOverlappedResult(file, overlapped, transferred, FALSE) != FALSE;
    }

    bool TransferAll(
        HANDLE pipe,
        BYTE* data,
        DWORD length,
        HANDLE cancelEvent,
        ULONGLONG deadline,
        bool write)
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

            DWORD transferred = 0;
            BOOL ok = write
                ? WriteFile(pipe, data + offset, length - offset, nullptr, &overlapped)
                : ReadFile(pipe, data + offset, length - offset, nullptr, &overlapped);
            if (!ok && GetLastError() == ERROR_IO_PENDING)
            {
                ok = WaitForIo(pipe, &overlapped, cancelEvent, deadline, &transferred);
            }
            else if (ok)
            {
                ok = GetOverlappedResult(pipe, &overlapped, &transferred, FALSE);
            }

            CloseHandle(overlapped.hEvent);
            if (!ok || transferred == 0)
            {
                return false;
            }

            offset += transferred;
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

    bool WriteAllCancelable(
        HANDLE pipe,
        const BYTE* data,
        DWORD length,
        HANDLE cancelEvent,
        ULONGLONG deadline)
    {
        if (data == nullptr && length != 0)
        {
            return false;
        }

        return TransferAll(
            pipe,
            const_cast<BYTE*>(data),
            length,
            cancelEvent,
            deadline,
            true);
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

    bool OpenBridgePipe(
        PCWSTR pipeName,
        DWORD timeoutMilliseconds,
        bool overlapped,
        HANDLE* pipe,
        DWORD* windowsError = nullptr)
    {
        *pipe = INVALID_HANDLE_VALUE;
        if (pipeName == nullptr || !WaitNamedPipeW(pipeName, timeoutMilliseconds))
        {
            if (windowsError != nullptr)
            {
                *windowsError = pipeName == nullptr ? ERROR_INVALID_PARAMETER : GetLastError();
            }

            return false;
        }

        *pipe = CreateFileW(
            pipeName,
            GENERIC_READ | GENERIC_WRITE,
            0,
            nullptr,
            OPEN_EXISTING,
            overlapped ? FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED : FILE_ATTRIBUTE_NORMAL,
            nullptr);

        if (*pipe == INVALID_HANDLE_VALUE)
        {
            if (windowsError != nullptr)
            {
                *windowsError = GetLastError();
            }

            return false;
        }

        if (windowsError != nullptr)
        {
            *windowsError = ERROR_SUCCESS;
        }

        return true;
    }

    bool OpenBridgePipe(PCWSTR pipeName, DWORD timeoutMilliseconds, HANDLE* pipe)
    {
        return OpenBridgePipe(pipeName, timeoutMilliseconds, false, pipe);
    }

    bool ReadAllCancelable(
        HANDLE pipe,
        BYTE* data,
        DWORD length,
        HANDLE cancelEvent,
        ULONGLONG deadline)
    {
        return TransferAll(pipe, data, length, cancelEvent, deadline, false);
    }

    bool ReadFrameBytesCancelable(
        HANDLE pipe,
        HANDLE cancelEvent,
        ULONGLONG deadline,
        std::vector<BYTE>* payload,
        BridgeRequestDiagnostics* diagnostics = nullptr)
    {
        if (diagnostics != nullptr)
        {
            diagnostics->stage = L"read_length";
            diagnostics->rejectReason = L"transport";
        }

        BYTE lengthBuffer[sizeof(DWORD)]{};
        if (!ReadAllCancelable(pipe, lengthBuffer, sizeof(lengthBuffer), cancelEvent, deadline))
        {
            if (diagnostics != nullptr)
            {
                diagnostics->win32 = GetLastError();
                diagnostics->timeout = RemainingTimeout(deadline) == 0;
            }

            return false;
        }

        DWORD length = 0;
        if (!ReadBigEndianLength(lengthBuffer, &length))
        {
            if (diagnostics != nullptr)
            {
                diagnostics->stage = L"framing";
                diagnostics->rejectReason = L"framing";
                diagnostics->win32 = ERROR_INVALID_DATA;
            }

            return false;
        }

        payload->assign(length, 0);
        if (diagnostics != nullptr)
        {
            diagnostics->stage = L"read_payload";
            diagnostics->rejectReason = L"transport";
        }

        if (!ReadAllCancelable(pipe, payload->data(), length, cancelEvent, deadline))
        {
            if (diagnostics != nullptr)
            {
                diagnostics->win32 = GetLastError();
                diagnostics->timeout = RemainingTimeout(deadline) == 0;
            }

            return false;
        }

        return true;
    }

    bool ReadFrameCancelable(
        HANDLE pipe,
        HANDLE cancelEvent,
        ULONGLONG deadline,
        std::string* json)
    {
        std::vector<BYTE> payload;
        if (!ReadFrameBytesCancelable(pipe, cancelEvent, deadline, &payload))
        {
            return false;
        }

        json->assign(reinterpret_cast<const char*>(payload.data()), payload.size());
        SecureZeroMemory(payload.data(), payload.size());
        return true;
    }

    bool SendRequestReadBytes(
        PCWSTR pipeName,
        const std::string& request,
        DWORD timeoutMilliseconds,
        SecureByteBuffer* response,
        BridgeRequestDiagnostics* diagnostics = nullptr)
    {
        const ULONGLONG deadline = TimeoutDeadline(timeoutMilliseconds);
        const ULONGLONG started = GetTickCount64();

        HANDLE pipe = INVALID_HANDLE_VALUE;
        DWORD openError = ERROR_SUCCESS;
        if (diagnostics != nullptr)
        {
            diagnostics->stage = L"connect";
            diagnostics->rejectReason = L"transport";
        }

        if (!OpenBridgePipe(pipeName, RemainingTimeout(deadline), true, &pipe, &openError))
        {
            if (diagnostics != nullptr)
            {
                diagnostics->win32 = openError;
                diagnostics->timeout = openError == ERROR_SEM_TIMEOUT || RemainingTimeout(deadline) == 0;
            }

            return false;
        }

        if (diagnostics != nullptr && diagnostics->traceIdentity)
        {
            wchar_t buffer[96]{};
            swprintf_s(
                buffer,
                L"CP_IDENTITY_QUERY_PIPE_CONNECTED elapsedMs=%lu",
                ElapsedMilliseconds(started));
            GaltekTraceLine(buffer);
        }

        const std::vector<BYTE> frame = FrameJson(request);
        std::vector<BYTE> payload;
        if (diagnostics != nullptr)
        {
            diagnostics->stage = L"write";
            diagnostics->rejectReason = L"transport";
        }

        const bool wrote = WriteAllCancelable(
                pipe,
                frame.data(),
                static_cast<DWORD>(frame.size()),
                nullptr,
                deadline);
        if (!wrote)
        {
            if (diagnostics != nullptr)
            {
                diagnostics->win32 = GetLastError();
                diagnostics->timeout = RemainingTimeout(deadline) == 0;
            }

            CloseHandle(pipe);
            return false;
        }

        if (diagnostics != nullptr && diagnostics->traceIdentity)
        {
            wchar_t buffer[96]{};
            swprintf_s(
                buffer,
                L"CP_IDENTITY_QUERY_REQUEST_SENT elapsedMs=%lu",
                ElapsedMilliseconds(started));
            GaltekTraceLine(buffer);
        }

        const bool ok = ReadFrameBytesCancelable(pipe, nullptr, deadline, &payload, diagnostics);
        CloseHandle(pipe);

        if (!ok)
        {
            return false;
        }

        if (diagnostics != nullptr && diagnostics->traceIdentity)
        {
            wchar_t buffer[96]{};
            swprintf_s(
                buffer,
                L"CP_IDENTITY_QUERY_RESPONSE_RECEIVED elapsedMs=%lu",
                ElapsedMilliseconds(started));
            GaltekTraceLine(buffer);
        }

        *response = SecureByteBuffer(std::move(payload));
        return true;
    }

    bool SendRequestReadJson(
        PCWSTR pipeName,
        const std::string& request,
        DWORD timeoutMilliseconds,
        std::string* response,
        BridgeRequestDiagnostics* diagnostics = nullptr)
    {
        SecureByteBuffer payload;
        if (!SendRequestReadBytes(pipeName, request, timeoutMilliseconds, &payload, diagnostics))
        {
            return false;
        }

        response->assign(reinterpret_cast<const char*>(payload.data()), payload.size());
        return true;
    }

    bool ExtractJsonObject(
        const std::string& json,
        const char* key,
        std::string* objectJson)
    {
        if (key == nullptr || objectJson == nullptr)
        {
            return false;
        }

        const std::string prefix = std::string("\"") + key + "\":";
        size_t index = json.find(prefix);
        if (index == std::string::npos)
        {
            return false;
        }

        index += prefix.size();
        while (index < json.size()
            && (json[index] == ' ' || json[index] == '\t' || json[index] == '\r' || json[index] == '\n'))
        {
            index++;
        }

        if (index >= json.size() || json[index] != '{')
        {
            return false;
        }

        const size_t objectStart = index;
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (; index < json.size(); index++)
        {
            const char current = json[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '{')
            {
                depth++;
                continue;
            }

            if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    objectJson->assign(json.data() + objectStart, index - objectStart + 1u);
                    return true;
                }

                if (depth < 0)
                {
                    return false;
                }
            }
        }

        return false;
    }

    bool IsValidGuidText(const std::string& value)
    {
        if (value.size() != 36
            || value[8] != '-'
            || value[13] != '-'
            || value[18] != '-'
            || value[23] != '-')
        {
            return false;
        }

        for (size_t index = 0; index < value.size(); index++)
        {
            if (index == 8 || index == 13 || index == 18 || index == 23)
            {
                continue;
            }

            const char ch = value[index];
            const bool hex = (ch >= '0' && ch <= '9')
                || (ch >= 'a' && ch <= 'f')
                || (ch >= 'A' && ch <= 'F');
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }

    bool IsValidAccountId(const std::string& value)
    {
        return value == "PRIMARY" || value == "SECONDARY" || value == "ADMIN";
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

BridgeClient::BridgeClient()
    : _pipeName(kDefaultPipeName)
{
}

BridgeClient::BridgeClient(const std::wstring& pipeName)
    : _pipeName(pipeName.empty() ? kDefaultPipeName : pipeName)
{
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
    if (SendRequestReadJson(_pipeName.c_str(), request, timeoutMilliseconds, &response))
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
    BridgeRequestDiagnostics diagnostics;
    diagnostics.traceIdentity = true;
    GaltekTraceLine(L"CP_IDENTITY_QUERY_ENTER");
    if (!SendRequestReadJson(_pipeName.c_str(), request, timeoutMilliseconds, &response, &diagnostics))
    {
        TraceIdentityReject(diagnostics);
        return false;
    }

    long long protocolVersion = 0;
    if (!FindJsonNumber(response, "protocolVersion", &protocolVersion)
        || protocolVersion != 1)
    {
        TraceIdentityReject(L"protocol");
        return false;
    }

    std::string status;
    if (!FindJsonStringNarrow(response, "status", &status))
    {
        TraceIdentityReject(L"json");
        return false;
    }

    std::wstring statusLine = L"CP_IDENTITY_QUERY_RESPONSE_STATUS status=";
    statusLine.append(status.begin(), status.end());
    GaltekTraceLine(statusLine);
    if (status != "SUCCESS")
    {
        TraceIdentityReject(L"status");
        return false;
    }

    std::string activationStatus;
    if (!FindJsonStringNarrow(response, "activationStatus", &activationStatus)
        || activationStatus != "PENDING")
    {
        TraceIdentityReject(L"status");
        return false;
    }

    std::string identityJson;
    if (!ExtractJsonObject(response, "pendingIdentity", &identityJson))
    {
        TraceIdentityReject(L"json");
        return false;
    }

    BridgeActivationIdentity parsed;
    if (!FindJsonStringNarrow(identityJson, "activationId", &parsed.activationId)
        || !FindJsonStringNarrow(identityJson, "accountId", &parsed.accountId)
        || !FindJsonStringWide(identityJson, "userSid", &parsed.userSid)
        || !FindJsonStringWide(identityJson, "domain", &parsed.domain)
        || !FindJsonStringWide(identityJson, "username", &parsed.username)
        || !FindJsonBool(identityJson, "autoSubmitRequested", &parsed.autoSubmitRequested))
    {
        TraceIdentityReject(L"json");
        return false;
    }

    GaltekTraceLine(L"CP_IDENTITY_QUERY_PARSE_OK");

    if (!IsValidGuidText(parsed.activationId))
    {
        TraceIdentityReject(L"identity_validation");
        return false;
    }

    if (!IsValidAccountId(parsed.accountId))
    {
        TraceIdentityReject(L"account");
        return false;
    }

    if (parsed.userSid.empty()
        || parsed.domain.empty()
        || parsed.username.empty())
    {
        TraceIdentityReject(L"identity_validation");
        return false;
    }

    *identity = parsed;
    return true;
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
    if (!SendRequestReadBytes(_pipeName.c_str(), request, timeoutMilliseconds, &response))
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
    if (!OpenBridgePipe(_pipeName.c_str(), 250, true, &pipe))
    {
        return false;
    }

    const std::vector<BYTE> frame = FrameJson(request);
    std::string response;
    const bool ok = WriteAllCancelable(
            pipe,
            frame.data(),
            static_cast<DWORD>(frame.size()),
            cancelEvent,
            TimeoutDeadline(250))
        && ReadFrameCancelable(pipe, cancelEvent, MAXULONGLONG, &response);
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
    return SendRequestReadJson(_pipeName.c_str(), request, timeoutMilliseconds, &response)
        && response.find("\"status\":\"SUCCESS\"") != std::string::npos;
}
