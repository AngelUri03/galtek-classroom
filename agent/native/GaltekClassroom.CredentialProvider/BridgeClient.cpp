#include "BridgeClient.h"

#include <objbase.h>
#include <cstring>
#include <string>
#include <vector>

namespace
{
    constexpr wchar_t kPipeName[] = L"\\\\.\\pipe\\GaltekClassroom.CredentialProvider.v1";
    constexpr DWORD kMaxMessageBytes = 8u * 1024u;

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

    bool ReadFrame(HANDLE pipe, std::string* json)
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

        std::vector<BYTE> payload(length);
        if (!ReadAll(pipe, payload.data(), length))
        {
            return false;
        }

        json->assign(reinterpret_cast<const char*>(payload.data()), payload.size());
        return true;
    }
}

BridgeActivationStatus BridgeClient::GetPendingActivationMetadata(DWORD timeoutMilliseconds) const
{
    if (!WaitNamedPipeW(kPipeName, timeoutMilliseconds))
    {
        return BridgeActivationStatus::ServiceUnavailable;
    }

    HANDLE pipe = CreateFileW(
        kPipeName,
        GENERIC_READ | GENERIC_WRITE,
        0,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);

    if (pipe == INVALID_HANDLE_VALUE)
    {
        return BridgeActivationStatus::ServiceUnavailable;
    }

    BridgeActivationStatus status = BridgeActivationStatus::ServiceUnavailable;
    const std::string request =
        "{\"protocolVersion\":1,\"requestId\":\""
        + CreateRequestId()
        + "\",\"operation\":\"GET_PENDING_ACTIVATION_METADATA\"}";
    const std::vector<BYTE> frame = FrameJson(request);

    std::string response;
    if (WriteAll(pipe, frame.data(), static_cast<DWORD>(frame.size())) && ReadFrame(pipe, &response))
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

    CloseHandle(pipe);
    return status;
}
