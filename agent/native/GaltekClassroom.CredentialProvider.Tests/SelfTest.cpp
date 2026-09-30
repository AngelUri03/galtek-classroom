#include "../GaltekClassroom.CredentialProvider/BridgeClient.h"
#include "../GaltekClassroom.CredentialProvider/Credential.h"
#include "../GaltekClassroom.CredentialProvider/CredentialProvider.h"

#include <credentialprovider.h>
#include <ntsecapi.h>
#include <windows.h>

#include <chrono>
#include <cstring>
#include <iostream>
#include <new>
#include <atomic>
#include <condition_variable>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

long g_objectCount = 0;

namespace
{
    constexpr DWORD kBoundedEnumerationMilliseconds = 2000;
    constexpr NTSTATUS kStatusSuccess = static_cast<NTSTATUS>(0x00000000);
    constexpr NTSTATUS kStatusLogonFailure = static_cast<NTSTATUS>(0xC000006D);

    int Fail(const wchar_t* message)
    {
        std::wcerr << L"FAILED: " << message << std::endl;
        return 1;
    }

    bool Failed(HRESULT hr)
    {
        return FAILED(hr);
    }

    bool WideBytesEqual(const BYTE* source, DWORD byteLength, PCWSTR expected)
    {
        const DWORD expectedBytes = static_cast<DWORD>(wcslen(expected) * sizeof(wchar_t));
        return byteLength == expectedBytes
            && memcmp(source, expected, expectedBytes) == 0;
    }

    bool ValidatePackedString(
        const BYTE* blob,
        DWORD blobSize,
        const UNICODE_STRING& value,
        DWORD expectedOffset,
        PCWSTR expectedText)
    {
        const DWORD expectedBytes = static_cast<DWORD>(wcslen(expectedText) * sizeof(wchar_t));
        const ULONG_PTR offset = reinterpret_cast<ULONG_PTR>(value.Buffer);
        if (value.Length != expectedBytes
            || value.MaximumLength != value.Length
            || offset != expectedOffset
            || offset < sizeof(KERB_INTERACTIVE_UNLOCK_LOGON)
            || offset > blobSize
            || blobSize - static_cast<DWORD>(offset) < value.Length
            || (offset % sizeof(wchar_t)) != 0)
        {
            return false;
        }

        return WideBytesEqual(blob + offset, value.Length, expectedText);
    }

    bool ValidatePackedKerbInteractiveUnlockLogon(
        const BYTE* blob,
        DWORD blobSize,
        PCWSTR expectedDomain,
        PCWSTR expectedUsername,
        PCWSTR expectedPassword)
    {
        if (blob == nullptr || blobSize < sizeof(KERB_INTERACTIVE_UNLOCK_LOGON))
        {
            return false;
        }

        const DWORD domainBytes = static_cast<DWORD>(wcslen(expectedDomain) * sizeof(wchar_t));
        const DWORD usernameBytes = static_cast<DWORD>(wcslen(expectedUsername) * sizeof(wchar_t));
        const DWORD passwordBytes = static_cast<DWORD>(wcslen(expectedPassword) * sizeof(wchar_t));
        const DWORD expectedSize = sizeof(KERB_INTERACTIVE_UNLOCK_LOGON)
            + domainBytes
            + usernameBytes
            + passwordBytes;
        if (blobSize != expectedSize)
        {
            return false;
        }

        const auto* packed = reinterpret_cast<const KERB_INTERACTIVE_UNLOCK_LOGON*>(blob);
        if (packed->Logon.MessageType != KerbInteractiveLogon
            || packed->LogonId.HighPart != 0
            || packed->LogonId.LowPart != 0)
        {
            return false;
        }

        DWORD offset = sizeof(KERB_INTERACTIVE_UNLOCK_LOGON);
        if (!ValidatePackedString(blob, blobSize, packed->Logon.LogonDomainName, offset, expectedDomain))
        {
            return false;
        }

        offset += domainBytes;
        if (!ValidatePackedString(blob, blobSize, packed->Logon.UserName, offset, expectedUsername))
        {
            return false;
        }

        offset += usernameBytes;
        if (!ValidatePackedString(blob, blobSize, packed->Logon.Password, offset, expectedPassword))
        {
            return false;
        }

        return true;
    }

    std::wstring CreateTestPipeName()
    {
        GUID id{};
        if (FAILED(CoCreateGuid(&id)))
        {
            return L"\\\\.\\pipe\\GaltekClassroom.CredentialProvider.Tests.Fallback";
        }

        wchar_t idText[39]{};
        if (StringFromGUID2(id, idText, ARRAYSIZE(idText)) == 0)
        {
            return L"\\\\.\\pipe\\GaltekClassroom.CredentialProvider.Tests.Fallback";
        }

        std::wstring value = L"\\\\.\\pipe\\GaltekClassroom.CredentialProvider.Tests.";
        value.append(idText);
        return value;
    }

    void WriteBigEndianLength(BYTE* target, DWORD length)
    {
        target[0] = static_cast<BYTE>((length >> 24) & 0xFF);
        target[1] = static_cast<BYTE>((length >> 16) & 0xFF);
        target[2] = static_cast<BYTE>((length >> 8) & 0xFF);
        target[3] = static_cast<BYTE>(length & 0xFF);
    }

    std::vector<BYTE> FrameJson(const std::string& json)
    {
        std::vector<BYTE> frame(sizeof(DWORD) + json.size());
        WriteBigEndianLength(frame.data(), static_cast<DWORD>(json.size()));
        memcpy(frame.data() + sizeof(DWORD), json.data(), json.size());
        return frame;
    }

    bool ReadExact(HANDLE pipe, BYTE* data, DWORD length)
    {
        DWORD offset = 0;
        while (offset < length)
        {
            DWORD transferred = 0;
            if (!ReadFile(pipe, data + offset, length - offset, &transferred, nullptr)
                || transferred == 0)
            {
                return false;
            }

            offset += transferred;
        }

        return true;
    }

    bool WriteExact(HANDLE pipe, const BYTE* data, DWORD length)
    {
        DWORD offset = 0;
        while (offset < length)
        {
            DWORD transferred = 0;
            if (!WriteFile(pipe, data + offset, length - offset, &transferred, nullptr)
                || transferred == 0)
            {
                return false;
            }

            offset += transferred;
        }

        return true;
    }

    bool ReadFrameJson(HANDLE pipe, std::string* json)
    {
        if (json == nullptr)
        {
            return false;
        }

        BYTE lengthBuffer[sizeof(DWORD)]{};
        if (!ReadExact(pipe, lengthBuffer, sizeof(lengthBuffer)))
        {
            return false;
        }

        DWORD length = (static_cast<DWORD>(lengthBuffer[0]) << 24)
            | (static_cast<DWORD>(lengthBuffer[1]) << 16)
            | (static_cast<DWORD>(lengthBuffer[2]) << 8)
            | static_cast<DWORD>(lengthBuffer[3]);
        if (length == 0 || length > 8192)
        {
            return false;
        }

        std::vector<BYTE> request(length);
        if (!ReadExact(pipe, request.data(), length))
        {
            return false;
        }

        json->assign(reinterpret_cast<const char*>(request.data()), request.size());
        return true;
    }

    bool ParseJsonNumber(const std::string& json, const char* key, long long* value)
    {
        if (key == nullptr || value == nullptr)
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
        long long parsed = 0;
        bool foundDigit = false;
        while (index < json.size() && json[index] >= '0' && json[index] <= '9')
        {
            parsed = parsed * 10 + (json[index] - '0');
            foundDigit = true;
            index++;
        }

        if (!foundDigit)
        {
            return false;
        }

        *value = parsed;
        return true;
    }

    class FakeCredentialProviderEvents final : public ICredentialProviderEvents
    {
    public:
        FakeCredentialProviderEvents()
            : _referenceCount(1),
              _result(S_OK),
              _calls(0),
              _lastContext(0),
              _marshaller(nullptr)
        {
            _calledEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            CoCreateFreeThreadedMarshaler(
                static_cast<IUnknown*>(static_cast<ICredentialProviderEvents*>(this)),
                &_marshaller);
        }

        FakeCredentialProviderEvents(const FakeCredentialProviderEvents&) = delete;
        FakeCredentialProviderEvents& operator=(const FakeCredentialProviderEvents&) = delete;

        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** object) override
        {
            if (object == nullptr)
            {
                return E_POINTER;
            }

            *object = nullptr;
            if (riid == IID_IUnknown || riid == IID_ICredentialProviderEvents)
            {
                *object = static_cast<ICredentialProviderEvents*>(this);
                AddRef();
                return S_OK;
            }

            if (riid == IID_IMarshal && _marshaller != nullptr)
            {
                return _marshaller->QueryInterface(riid, object);
            }

            return E_NOINTERFACE;
        }

        ULONG STDMETHODCALLTYPE AddRef() override
        {
            return static_cast<ULONG>(InterlockedIncrement(&_referenceCount));
        }

        ULONG STDMETHODCALLTYPE Release() override
        {
            const LONG count = InterlockedDecrement(&_referenceCount);
            if (count == 0)
            {
                delete this;
            }

            return static_cast<ULONG>(count);
        }

        HRESULT STDMETHODCALLTYPE CredentialsChanged(UINT_PTR adviseContext) override
        {
            _lastContext = adviseContext;
            InterlockedIncrement(&_calls);
            SetEvent(_calledEvent);
            return _result.load();
        }

        void SetResult(HRESULT result)
        {
            _result = result;
        }

        LONG Calls() const
        {
            return _calls;
        }

        UINT_PTR LastContext() const
        {
            return _lastContext;
        }

        bool WaitForCalls(LONG expectedCalls, DWORD timeoutMilliseconds) const
        {
            const ULONGLONG deadline = GetTickCount64() + timeoutMilliseconds;
            while (GetTickCount64() <= deadline)
            {
                if (_calls >= expectedCalls)
                {
                    return true;
                }

                WaitForSingleObject(_calledEvent, 25);
            }

            return _calls >= expectedCalls;
        }

    private:
        ~FakeCredentialProviderEvents()
        {
            if (_marshaller != nullptr)
            {
                _marshaller->Release();
                _marshaller = nullptr;
            }

            if (_calledEvent != nullptr)
            {
                CloseHandle(_calledEvent);
                _calledEvent = nullptr;
            }
        }

        LONG _referenceCount;
        std::atomic<HRESULT> _result;
        volatile LONG _calls;
        UINT_PTR _lastContext;
        HANDLE _calledEvent;
        IUnknown* _marshaller;
    };

    struct WaitBridgeResponse
    {
        long long generation;
        DWORD delayMilliseconds;
    };

    class ScriptedBridgeServer
    {
    public:
        explicit ScriptedBridgeServer(const std::wstring& pipeName)
            : _pipeName(pipeName)
        {
            _stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        }

        ScriptedBridgeServer(const ScriptedBridgeServer&) = delete;
        ScriptedBridgeServer& operator=(const ScriptedBridgeServer&) = delete;

        ~ScriptedBridgeServer()
        {
            Stop();
            if (_stopEvent != nullptr)
            {
                CloseHandle(_stopEvent);
                _stopEvent = nullptr;
            }
        }

        void AddWaitResponse(long long generation, DWORD delayMilliseconds = 0)
        {
            _waitResponses.push_back({ generation, delayMilliseconds });
        }

        void SetIdentityAvailable(bool available)
        {
            _identityAvailable = available;
            _identityResponseJson.clear();
        }

        void SetIdentityResponseJson(const std::string& json)
        {
            _identityAvailable = true;
            _identityResponseJson = json;
        }

        void SetIdentityResponseDelay(DWORD delayMilliseconds)
        {
            _identityResponseDelayMilliseconds = delayMilliseconds;
        }

        bool Start()
        {
            _readyEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (_stopEvent == nullptr || _readyEvent == nullptr)
            {
                return false;
            }

            try
            {
                _thread = std::thread(&ScriptedBridgeServer::Run, this);
            }
            catch (...)
            {
                return false;
            }

            return WaitForSingleObject(_readyEvent, 1000) == WAIT_OBJECT_0;
        }

        void Stop()
        {
            if (_stopEvent != nullptr)
            {
                SetEvent(_stopEvent);
            }

            if (_thread.joinable())
            {
                CancelSynchronousIo(_thread.native_handle());
            }

            HANDLE client = CreateFileW(
                _pipeName.c_str(),
                GENERIC_READ | GENERIC_WRITE,
                0,
                nullptr,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                nullptr);
            if (client != INVALID_HANDLE_VALUE)
            {
                CloseHandle(client);
            }

            if (_thread.joinable())
            {
                _thread.join();
            }

            if (_readyEvent != nullptr)
            {
                CloseHandle(_readyEvent);
                _readyEvent = nullptr;
            }
        }

        bool WaitForRequestCount(size_t expected, DWORD timeoutMilliseconds)
        {
            std::unique_lock<std::mutex> lock(_mutex);
            return _condition.wait_for(
                lock,
                std::chrono::milliseconds(timeoutMilliseconds),
                [&]() { return _requests.size() >= expected; });
        }

        bool WaitForWaitRequestCount(size_t expected, DWORD timeoutMilliseconds)
        {
            std::unique_lock<std::mutex> lock(_mutex);
            return _condition.wait_for(
                lock,
                std::chrono::milliseconds(timeoutMilliseconds),
                [&]() { return _waitObservedGenerations.size() >= expected; });
        }

        std::vector<long long> WaitObservedGenerations()
        {
            std::lock_guard<std::mutex> lock(_mutex);
            return _waitObservedGenerations;
        }

        DWORD MillisecondsBetweenRequests(size_t first, size_t second)
        {
            std::lock_guard<std::mutex> lock(_mutex);
            if (_requestTicks.size() <= first || _requestTicks.size() <= second)
            {
                return 0;
            }

            const ULONGLONG delta = _requestTicks[second] - _requestTicks[first];
            return delta > MAXDWORD ? MAXDWORD : static_cast<DWORD>(delta);
        }

    private:
        void Run()
        {
            bool readySignaled = false;
            while (WaitForSingleObject(_stopEvent, 0) == WAIT_TIMEOUT)
            {
                HANDLE pipe = CreateNamedPipeW(
                    _pipeName.c_str(),
                    PIPE_ACCESS_DUPLEX,
                    PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                    1,
                    4096,
                    4096,
                    0,
                    nullptr);
                if (pipe == INVALID_HANDLE_VALUE)
                {
                    return;
                }

                // Stop can be signaled between the loop condition and pipe
                // creation. Recheck before entering the blocking connect.
                if (WaitForSingleObject(_stopEvent, 0) != WAIT_TIMEOUT)
                {
                    CloseHandle(pipe);
                    break;
                }

                if (!readySignaled)
                {
                    SetEvent(_readyEvent);
                    readySignaled = true;
                }

                BOOL connected = ConnectNamedPipe(pipe, nullptr);
                if (!connected && GetLastError() != ERROR_PIPE_CONNECTED)
                {
                    CloseHandle(pipe);
                    continue;
                }

                std::string request;
                if (ReadFrameJson(pipe, &request))
                {
                    HandleRequest(pipe, request);
                }

                DisconnectNamedPipe(pipe);
                CloseHandle(pipe);
            }
        }

        void HandleRequest(HANDLE pipe, const std::string& request)
        {
            {
                std::lock_guard<std::mutex> lock(_mutex);
                _requests.push_back(request);
                _requestTicks.push_back(GetTickCount64());
                if (request.find("\"operation\":\"WAIT_FOR_ACTIVATION_CHANGE\"") != std::string::npos)
                {
                    long long observed = -1;
                    ParseJsonNumber(request, "observedGeneration", &observed);
                    _waitObservedGenerations.push_back(observed);
                }
            }
            _condition.notify_all();

            if (request.find("\"operation\":\"WAIT_FOR_ACTIVATION_CHANGE\"") != std::string::npos)
            {
                WaitBridgeResponse response{};
                bool hasResponse = false;
                {
                    std::lock_guard<std::mutex> lock(_mutex);
                    if (_waitResponseIndex < _waitResponses.size())
                    {
                        response = _waitResponses[_waitResponseIndex++];
                        hasResponse = true;
                    }
                }

                if (!hasResponse)
                {
                    WaitForSingleObject(_stopEvent, INFINITE);
                    return;
                }

                if (response.delayMilliseconds > 0
                    && WaitForSingleObject(_stopEvent, response.delayMilliseconds) != WAIT_TIMEOUT)
                {
                    return;
                }

                const std::string json =
                    "{\"protocolVersion\":1,\"requestId\":\"11111111-2222-3333-4444-555555555555\","
                    "\"status\":\"SUCCESS\",\"generation\":"
                    + std::to_string(response.generation)
                    + "}";
                const std::vector<BYTE> frame = FrameJson(json);
                WriteExact(pipe, frame.data(), static_cast<DWORD>(frame.size()));
                FlushFileBuffers(pipe);
                return;
            }

            if (request.find("\"operation\":\"GET_PENDING_ACTIVATION_IDENTITY\"") != std::string::npos)
            {
                if (_identityResponseDelayMilliseconds > 0
                    && WaitForSingleObject(_stopEvent, _identityResponseDelayMilliseconds) != WAIT_TIMEOUT)
                {
                    return;
                }

                const std::string json = !_identityResponseJson.empty()
                    ? _identityResponseJson
                    : _identityAvailable
                    ? "{\"protocolVersion\":1,\"requestId\":\"11111111-2222-3333-4444-555555555555\","
                        "\"status\":\"SUCCESS\",\"activationStatus\":\"PENDING\","
                        "\"pendingIdentity\":{"
                            "\"activationId\":\"11111111-2222-3333-4444-555555555555\","
                            "\"accountId\":\"PRIMARY\","
                            "\"userSid\":\"S-1-5-21-1000000000-1000000000-1000000000-1004\","
                            "\"domain\":\"AULA\","
                            "\"username\":\"Primaria\","
                            "\"autoSubmitRequested\":true}}"
                    : "{\"protocolVersion\":1,\"requestId\":\"11111111-2222-3333-4444-555555555555\","
                        "\"status\":\"SUCCESS\",\"activationStatus\":\"NONE\"}";
                const std::vector<BYTE> frame = FrameJson(json);
                WriteExact(pipe, frame.data(), static_cast<DWORD>(frame.size()));
                FlushFileBuffers(pipe);
            }
        }

        HANDLE _stopEvent = nullptr;
        HANDLE _readyEvent = nullptr;
        std::wstring _pipeName;
        std::thread _thread;
        std::mutex _mutex;
        std::condition_variable _condition;
        std::vector<WaitBridgeResponse> _waitResponses;
        std::vector<std::string> _requests;
        std::vector<long long> _waitObservedGenerations;
        std::vector<ULONGLONG> _requestTicks;
        size_t _waitResponseIndex = 0;
        bool _identityAvailable = false;
        std::string _identityResponseJson;
        DWORD _identityResponseDelayMilliseconds = 0;
    };

    class HangingBridgeServer
    {
    public:
        explicit HangingBridgeServer(const std::wstring& pipeName)
            : _pipeName(pipeName)
        {
        }

        HangingBridgeServer(const HangingBridgeServer&) = delete;
        HangingBridgeServer& operator=(const HangingBridgeServer&) = delete;

        ~HangingBridgeServer()
        {
            Stop();
        }

        bool Start()
        {
            _stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            _readyEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (_stopEvent == nullptr || _readyEvent == nullptr)
            {
                Stop();
                return false;
            }

            _pipe = CreateNamedPipeW(
                _pipeName.c_str(),
                PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                1,
                4096,
                4096,
                0,
                nullptr);
            if (_pipe == INVALID_HANDLE_VALUE)
            {
                Stop();
                return false;
            }

            try
            {
                _thread = std::thread(&HangingBridgeServer::Run, this);
            }
            catch (...)
            {
                Stop();
                return false;
            }

            return WaitForSingleObject(_readyEvent, 1000) == WAIT_OBJECT_0;
        }

        void Stop()
        {
            if (_stopEvent != nullptr)
            {
                SetEvent(_stopEvent);
            }

            if (_pipe != INVALID_HANDLE_VALUE)
            {
                CancelIoEx(_pipe, nullptr);
            }

            if (_thread.joinable())
            {
                _thread.join();
            }

            if (_pipe != INVALID_HANDLE_VALUE)
            {
                CloseHandle(_pipe);
                _pipe = INVALID_HANDLE_VALUE;
            }

            if (_readyEvent != nullptr)
            {
                CloseHandle(_readyEvent);
                _readyEvent = nullptr;
            }

            if (_stopEvent != nullptr)
            {
                CloseHandle(_stopEvent);
                _stopEvent = nullptr;
            }
        }

    private:
        void Run()
        {
            OVERLAPPED overlapped{};
            overlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (overlapped.hEvent == nullptr)
            {
                SetEvent(_readyEvent);
                return;
            }

            BOOL connected = ConnectNamedPipe(_pipe, &overlapped);
            DWORD error = connected ? ERROR_SUCCESS : GetLastError();
            if (!connected && error == ERROR_IO_PENDING)
            {
                SetEvent(_readyEvent);
                HANDLE waits[] = { overlapped.hEvent, _stopEvent };
                DWORD wait = WaitForMultipleObjects(ARRAYSIZE(waits), waits, FALSE, INFINITE);
                connected = wait == WAIT_OBJECT_0;
            }
            else
            {
                SetEvent(_readyEvent);
                connected = connected || error == ERROR_PIPE_CONNECTED;
            }

            if (connected)
            {
                WaitForSingleObject(_stopEvent, INFINITE);
                DisconnectNamedPipe(_pipe);
            }

            CloseHandle(overlapped.hEvent);
        }

        HANDLE _pipe = INVALID_HANDLE_VALUE;
        std::wstring _pipeName;
        HANDLE _stopEvent = nullptr;
        HANDLE _readyEvent = nullptr;
        std::thread _thread;
    };

    class DelayedActivationChangeBridgeServer
    {
    public:
        explicit DelayedActivationChangeBridgeServer(const std::wstring& pipeName)
            : _pipeName(pipeName)
        {
        }

        DelayedActivationChangeBridgeServer(const DelayedActivationChangeBridgeServer&) = delete;
        DelayedActivationChangeBridgeServer& operator=(const DelayedActivationChangeBridgeServer&) = delete;

        ~DelayedActivationChangeBridgeServer()
        {
            Stop();
        }

        bool Start(DWORD responseDelayMilliseconds)
        {
            _responseDelayMilliseconds = responseDelayMilliseconds;
            _readyEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            _requestReadEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (_readyEvent == nullptr || _requestReadEvent == nullptr)
            {
                Stop();
                return false;
            }

            _pipe = CreateNamedPipeW(
                _pipeName.c_str(),
                PIPE_ACCESS_DUPLEX,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                1,
                4096,
                4096,
                0,
                nullptr);
            if (_pipe == INVALID_HANDLE_VALUE)
            {
                Stop();
                return false;
            }

            try
            {
                _thread = std::thread(&DelayedActivationChangeBridgeServer::Run, this);
            }
            catch (...)
            {
                Stop();
                return false;
            }

            return WaitForSingleObject(_readyEvent, 1000) == WAIT_OBJECT_0;
        }

        bool WaitForRequestRead(DWORD timeoutMilliseconds) const
        {
            return WaitForSingleObject(_requestReadEvent, timeoutMilliseconds) == WAIT_OBJECT_0;
        }

        void Stop()
        {
            if (_pipe != INVALID_HANDLE_VALUE)
            {
                CancelIoEx(_pipe, nullptr);
            }

            if (_thread.joinable())
            {
                _thread.join();
            }

            if (_pipe != INVALID_HANDLE_VALUE)
            {
                CloseHandle(_pipe);
                _pipe = INVALID_HANDLE_VALUE;
            }

            if (_requestReadEvent != nullptr)
            {
                CloseHandle(_requestReadEvent);
                _requestReadEvent = nullptr;
            }

            if (_readyEvent != nullptr)
            {
                CloseHandle(_readyEvent);
                _readyEvent = nullptr;
            }
        }

    private:
        void Run()
        {
            SetEvent(_readyEvent);
            BOOL connected = ConnectNamedPipe(_pipe, nullptr);
            if (!connected && GetLastError() != ERROR_PIPE_CONNECTED)
            {
                return;
            }

            BYTE lengthBuffer[sizeof(DWORD)]{};
            if (!ReadExact(_pipe, lengthBuffer, sizeof(lengthBuffer)))
            {
                DisconnectNamedPipe(_pipe);
                return;
            }

            DWORD length = (static_cast<DWORD>(lengthBuffer[0]) << 24)
                | (static_cast<DWORD>(lengthBuffer[1]) << 16)
                | (static_cast<DWORD>(lengthBuffer[2]) << 8)
                | static_cast<DWORD>(lengthBuffer[3]);
            if (length == 0 || length > 8192)
            {
                DisconnectNamedPipe(_pipe);
                return;
            }

            std::vector<BYTE> request(length);
            if (!ReadExact(_pipe, request.data(), length))
            {
                DisconnectNamedPipe(_pipe);
                return;
            }

            SetEvent(_requestReadEvent);
            Sleep(_responseDelayMilliseconds);

            const std::vector<BYTE> response = FrameJson(
                "{\"protocolVersion\":1,\"requestId\":\"11111111-2222-3333-4444-555555555555\","
                "\"status\":\"SUCCESS\",\"generation\":2}");
            WriteExact(_pipe, response.data(), static_cast<DWORD>(response.size()));
            FlushFileBuffers(_pipe);
            DisconnectNamedPipe(_pipe);
        }

        HANDLE _pipe = INVALID_HANDLE_VALUE;
        std::wstring _pipeName;
        HANDLE _readyEvent = nullptr;
        HANDLE _requestReadEvent = nullptr;
        DWORD _responseDelayMilliseconds = 0;
        std::thread _thread;
    };

    int VerifyFailOpenCredentialCount(ICredentialProvider* providerInterface, const wchar_t* message)
    {
        DWORD count = 99;
        DWORD defaultCredential = 0;
        BOOL autoLogon = TRUE;
        if (Failed(providerInterface->GetCredentialCount(&count, &defaultCredential, &autoLogon))
            || count != 0
            || defaultCredential != CREDENTIAL_PROVIDER_NO_DEFAULT
            || autoLogon != FALSE)
        {
            return Fail(message);
        }

        return 0;
    }
}

int wmain()
{
    HRESULT comInit = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    const bool shouldUninitializeCom = SUCCEEDED(comInit);
    if (FAILED(comInit) && comInit != RPC_E_CHANGED_MODE)
    {
        return Fail(L"COM initialization for self-test");
    }

    GaltekCredentialProvider* provider = new (std::nothrow) GaltekCredentialProvider();
    if (provider == nullptr)
    {
        return Fail(L"provider allocation");
    }

    ICredentialProvider* providerInterface = nullptr;
    if (Failed(provider->QueryInterface(IID_ICredentialProvider, reinterpret_cast<void**>(&providerInterface)))
        || providerInterface == nullptr)
    {
        provider->Release();
        return Fail(L"ICredentialProvider construction");
    }

    ICredentialProviderFilter* filter = nullptr;
    if (provider->QueryInterface(IID_ICredentialProviderFilter, reinterpret_cast<void**>(&filter)) != E_NOINTERFACE)
    {
        if (filter != nullptr)
        {
            filter->Release();
        }

        providerInterface->Release();
        provider->Release();
        return Fail(L"provider unexpectedly implements ICredentialProviderFilter");
    }

    if (providerInterface->SetUsageScenario(CPUS_LOGON, 0) != S_OK)
    {
        providerInterface->Release();
        provider->Release();
        return Fail(L"CPUS_LOGON should be accepted");
    }

    DWORD fieldCount = 0;
    if (Failed(providerInterface->GetFieldDescriptorCount(&fieldCount))
        || fieldCount != GaltekFieldCount)
    {
        providerInterface->Release();
        provider->Release();
        return Fail(L"provider should expose Galtek tile field descriptors");
    }

    for (DWORD field = 0; field < fieldCount; field++)
    {
        CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR* descriptor = nullptr;
        if (Failed(providerInterface->GetFieldDescriptorAt(field, &descriptor)) || descriptor == nullptr)
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"field descriptor should allocate");
        }

        if (descriptor->cpft == CPFT_PASSWORD_TEXT)
        {
            CoTaskMemFree(descriptor->pszLabel);
            CoTaskMemFree(descriptor);
            providerInterface->Release();
            provider->Release();
            return Fail(L"Galtek tile must not expose a password field");
        }

        CoTaskMemFree(descriptor->pszLabel);
        CoTaskMemFree(descriptor);
    }

    int failOpenResult = VerifyFailOpenCredentialCount(
        providerInterface,
        L"no activation should enumerate zero credentials");
    if (failOpenResult != 0)
    {
        providerInterface->Release();
        provider->Release();
        return failOpenResult;
    }

    if (Failed(providerInterface->Advise(nullptr, 7))
        || Failed(providerInterface->UnAdvise()))
    {
        providerInterface->Release();
        provider->Release();
        return Fail(L"Advise/UnAdvise without events should be fail-open");
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        HangingBridgeServer hangingServer(pipeName);
        if (!hangingServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"hanging bridge server setup");
        }

        const auto started = std::chrono::steady_clock::now();
        BridgeClient hangingBridge(pipeName);
        BridgeActivationIdentity ignoredIdentity;
        const bool foundIdentity = hangingBridge.GetPendingActivationIdentity(250, &ignoredIdentity);
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - started);
        hangingServer.Stop();
        if (foundIdentity)
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"hanging bridge should not return activation identity");
        }

        if (elapsed.count() > kBoundedEnumerationMilliseconds)
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"hanging bridge should not block LogonUI enumeration");
        }
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        ScriptedBridgeServer scriptedServer(pipeName);
        scriptedServer.SetIdentityAvailable(true);
        if (!scriptedServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"scripted bridge setup for pendingIdentity parse");
        }

        BridgeClient bridge(pipeName);
        BridgeActivationIdentity bridgeIdentity;
        if (!bridge.GetPendingActivationIdentity(1000, &bridgeIdentity)
            || bridgeIdentity.activationId != "11111111-2222-3333-4444-555555555555"
            || bridgeIdentity.accountId != "PRIMARY"
            || bridgeIdentity.userSid != L"S-1-5-21-1000000000-1000000000-1000000000-1004"
            || bridgeIdentity.domain != L"AULA"
            || bridgeIdentity.username != L"Primaria"
            || !bridgeIdentity.autoSubmitRequested)
        {
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"Agent SUCCESS pendingIdentity response should parse and validate");
        }

        GaltekCredentialProvider* bridgeProvider = new (std::nothrow) GaltekCredentialProvider(pipeName);
        ICredentialProvider* bridgeProviderInterface = nullptr;
        if (bridgeProvider == nullptr
            || Failed(bridgeProvider->QueryInterface(
                IID_ICredentialProvider,
                reinterpret_cast<void**>(&bridgeProviderInterface)))
            || bridgeProviderInterface == nullptr
            || bridgeProviderInterface->SetUsageScenario(CPUS_LOGON, 0) != S_OK)
        {
            if (bridgeProviderInterface != nullptr)
            {
                bridgeProviderInterface->Release();
            }

            if (bridgeProvider != nullptr)
            {
                bridgeProvider->Release();
            }

            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"provider setup for pendingIdentity snapshot");
        }

        DWORD count = 0;
        DWORD defaultCredential = CREDENTIAL_PROVIDER_NO_DEFAULT;
        BOOL autoLogon = FALSE;
        if (Failed(bridgeProviderInterface->GetCredentialCount(&count, &defaultCredential, &autoLogon))
            || count != 1
            || defaultCredential != 0
            || autoLogon != TRUE)
        {
            bridgeProviderInterface->Release();
            bridgeProvider->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"Agent SUCCESS pendingIdentity response should commit credential snapshot");
        }

        bridgeProviderInterface->Release();
        bridgeProvider->Release();
        scriptedServer.SetIdentityResponseJson(
            "{\"protocolVersion\":1,\"requestId\":\"11111111-2222-3333-4444-555555555555\","
            "\"status\":\"SUCCESS\",\"activationStatus\":\"PENDING\","
            "\"pendingIdentity\":{\"activationId\":\"11111111-2222-3333-4444-555555555555\","
            "\"accountId\":\"ADMIN\","
            "\"userSid\":\"S-1-5-21-1000000000-1000000000-1000000000-1007\","
            "\"domain\":\"AULA\",\"username\":\"Admin\",\"autoSubmitRequested\":true}}");
        BridgeActivationIdentity adminIdentity;
        if (!bridge.GetPendingActivationIdentity(1000, &adminIdentity)
            || adminIdentity.accountId != "ADMIN"
            || adminIdentity.username != L"Admin"
            || !adminIdentity.autoSubmitRequested)
        {
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"ADMIN pendingIdentity must roundtrip through the Credential Provider");
        }

        scriptedServer.Stop();
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        ScriptedBridgeServer scriptedServer(pipeName);
        scriptedServer.SetIdentityAvailable(true);
        scriptedServer.SetIdentityResponseDelay(750);
        if (!scriptedServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"scripted bridge setup for delayed identity response");
        }

        BridgeClient bridge(pipeName);
        BridgeActivationIdentity delayedIdentity;
        if (!bridge.GetPendingActivationIdentity(1000, &delayedIdentity)
            || delayedIdentity.accountId != "PRIMARY")
        {
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"identity response received before deadline should not be discarded");
        }

        scriptedServer.Stop();
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        ScriptedBridgeServer scriptedServer(pipeName);
        scriptedServer.SetIdentityAvailable(true);
        scriptedServer.SetIdentityResponseDelay(1000);
        if (!scriptedServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"scripted bridge setup for mutex-free identity I/O");
        }

        GaltekCredentialProvider* lockProvider = new (std::nothrow) GaltekCredentialProvider(pipeName);
        ICredentialProvider* lockProviderInterface = nullptr;
        if (lockProvider == nullptr
            || Failed(lockProvider->QueryInterface(
                IID_ICredentialProvider,
                reinterpret_cast<void**>(&lockProviderInterface)))
            || lockProviderInterface == nullptr
            || lockProviderInterface->SetUsageScenario(CPUS_LOGON, 0) != S_OK)
        {
            if (lockProviderInterface != nullptr)
            {
                lockProviderInterface->Release();
            }

            if (lockProvider != nullptr)
            {
                lockProvider->Release();
            }

            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"provider setup for mutex-free identity I/O");
        }

        HRESULT countHr = E_FAIL;
        DWORD count = 0;
        DWORD defaultCredential = CREDENTIAL_PROVIDER_NO_DEFAULT;
        BOOL autoLogon = FALSE;
        std::thread countThread([&]()
        {
            countHr = lockProviderInterface->GetCredentialCount(&count, &defaultCredential, &autoLogon);
        });

        if (!scriptedServer.WaitForRequestCount(1, 1000))
        {
            countThread.join();
            lockProviderInterface->Release();
            lockProvider->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"identity I/O request should be observable for mutex test");
        }

        const auto usageStarted = std::chrono::steady_clock::now();
        const HRESULT usageHr = lockProviderInterface->SetUsageScenario(CPUS_LOGON, 0);
        const auto usageElapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - usageStarted);

        countThread.join();
        lockProviderInterface->Release();
        lockProvider->Release();
        scriptedServer.Stop();

        if (usageHr != S_OK
            || Failed(countHr)
            || usageElapsed.count() > 150)
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"GetCredentialCount must not hold provider mutex during pipe I/O");
        }
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        ScriptedBridgeServer scriptedServer(pipeName);
        scriptedServer.SetIdentityResponseJson(
            "{\"protocolVersion\":1,\"requestId\":\"11111111-2222-3333-4444-555555555555\","
            "\"status\":\"SUCCESS\",\"activationStatus\":\"PENDING\",\"pendingIdentity\":{");
        if (!scriptedServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"scripted bridge setup for malformed identity response");
        }

        BridgeClient bridge(pipeName);
        BridgeActivationIdentity malformedIdentity;
        if (bridge.GetPendingActivationIdentity(1000, &malformedIdentity))
        {
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"malformed identity response should fail closed");
        }

        scriptedServer.Stop();
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        ScriptedBridgeServer scriptedServer(pipeName);
        scriptedServer.SetIdentityResponseJson(
            "{\"protocolVersion\":2,\"requestId\":\"11111111-2222-3333-4444-555555555555\","
            "\"status\":\"SUCCESS\",\"activationStatus\":\"PENDING\","
            "\"pendingIdentity\":{\"activationId\":\"11111111-2222-3333-4444-555555555555\","
            "\"accountId\":\"PRIMARY\","
            "\"userSid\":\"S-1-5-21-1000000000-1000000000-1000000000-1004\","
            "\"domain\":\"AULA\",\"username\":\"Primaria\",\"autoSubmitRequested\":true}}");
        if (!scriptedServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"scripted bridge setup for protocol mismatch");
        }

        BridgeClient bridge(pipeName);
        BridgeActivationIdentity protocolIdentity;
        if (bridge.GetPendingActivationIdentity(1000, &protocolIdentity))
        {
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"protocol mismatch should fail closed");
        }

        scriptedServer.Stop();
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        ScriptedBridgeServer scriptedServer(pipeName);
        scriptedServer.SetIdentityResponseJson(
            "{\"protocolVersion\":1,\"requestId\":\"11111111-2222-3333-4444-555555555555\","
            "\"status\":\"SUCCESS\",\"activationStatus\":\"PENDING\","
            "\"pendingIdentity\":{\"activationId\":\"11111111-2222-3333-4444-555555555555\","
            "\"accountId\":\"TERTIARY\","
            "\"userSid\":\"S-1-5-21-1000000000-1000000000-1000000000-1004\","
            "\"domain\":\"AULA\",\"username\":\"Primaria\",\"autoSubmitRequested\":true}}");
        if (!scriptedServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"scripted bridge setup for invalid accountId");
        }

        BridgeClient bridge(pipeName);
        BridgeActivationIdentity invalidAccountIdentity;
        if (bridge.GetPendingActivationIdentity(1000, &invalidAccountIdentity))
        {
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"invalid accountId should fail closed");
        }

        scriptedServer.Stop();
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        ScriptedBridgeServer scriptedServer(pipeName);
        scriptedServer.SetIdentityAvailable(false);
        if (!scriptedServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"scripted bridge setup for no identity response");
        }

        GaltekCredentialProvider* emptyProvider = new (std::nothrow) GaltekCredentialProvider(pipeName);
        ICredentialProvider* emptyProviderInterface = nullptr;
        if (emptyProvider == nullptr
            || Failed(emptyProvider->QueryInterface(
                IID_ICredentialProvider,
                reinterpret_cast<void**>(&emptyProviderInterface)))
            || emptyProviderInterface == nullptr
            || emptyProviderInterface->SetUsageScenario(CPUS_LOGON, 0) != S_OK
            || VerifyFailOpenCredentialCount(
                emptyProviderInterface,
                L"no identity bridge response should enumerate zero credentials") != 0)
        {
            if (emptyProviderInterface != nullptr)
            {
                emptyProviderInterface->Release();
            }

            if (emptyProvider != nullptr)
            {
                emptyProvider->Release();
            }

            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return 1;
        }

        emptyProviderInterface->Release();
        emptyProvider->Release();
        scriptedServer.Stop();
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        DelayedActivationChangeBridgeServer delayedServer(pipeName);
        if (!delayedServer.Start(2500))
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"delayed activation-change bridge setup");
        }

        HANDLE cancelEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (cancelEvent == nullptr)
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"activation-change cancel event allocation");
        }

        BridgeClient bridge(pipeName);
        std::atomic_bool completed = false;
        bool waitResult = false;
        long long generation = 0;
        std::thread waitThread([&]()
        {
            waitResult = bridge.WaitForActivationChange(0, cancelEvent, &generation);
            completed = true;
        });

        if (!delayedServer.WaitForRequestRead(1000))
        {
            SetEvent(cancelEvent);
            waitThread.join();
            CloseHandle(cancelEvent);
            providerInterface->Release();
            provider->Release();
            return Fail(L"activation-change request should be sent");
        }

        Sleep(1250);
        if (completed)
        {
            SetEvent(cancelEvent);
            waitThread.join();
            CloseHandle(cancelEvent);
            providerInterface->Release();
            provider->Release();
            return Fail(L"activation-change client returned near the short RPC timeout");
        }

        waitThread.join();
        CloseHandle(cancelEvent);
        delayedServer.Stop();
        if (!waitResult || generation != 2)
        {
            std::wcerr << L"activation-change waitResult="
                << (waitResult ? L"true" : L"false")
                << L" generation=" << generation << std::endl;
            providerInterface->Release();
            provider->Release();
            return Fail(L"activation-change client should read delayed response");
        }
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        DelayedActivationChangeBridgeServer delayedServer(pipeName);
        if (!delayedServer.Start(1500))
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"cancelled activation-change bridge setup");
        }

        HANDLE cancelEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (cancelEvent == nullptr)
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"cancelled activation-change cancel event allocation");
        }

        BridgeClient bridge(pipeName);
        std::atomic_bool completed = false;
        bool waitResult = true;
        long long generation = 0;
        std::thread waitThread([&]()
        {
            waitResult = bridge.WaitForActivationChange(0, cancelEvent, &generation);
            completed = true;
        });

        if (!delayedServer.WaitForRequestRead(1000))
        {
            SetEvent(cancelEvent);
            waitThread.join();
            CloseHandle(cancelEvent);
            providerInterface->Release();
            provider->Release();
            return Fail(L"cancelled activation-change request should be sent");
        }

        const auto cancelStarted = std::chrono::steady_clock::now();
        SetEvent(cancelEvent);
        waitThread.join();
        const auto cancelElapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - cancelStarted);
        CloseHandle(cancelEvent);
        delayedServer.Stop();

        if (!completed || waitResult || cancelElapsed.count() > 1000)
        {
            std::wcerr << L"cancelled activation-change completed="
                << (completed ? L"true" : L"false")
                << L" waitResult=" << (waitResult ? L"true" : L"false")
                << L" elapsedMs=" << cancelElapsed.count() << std::endl;
            providerInterface->Release();
            provider->Release();
            return Fail(L"activation-change cancellation should be bounded");
        }
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        ScriptedBridgeServer scriptedServer(pipeName);
        scriptedServer.AddWaitResponse(2);
        if (!scriptedServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"scripted bridge setup for callback success");
        }

        GaltekCredentialProvider* callbackProvider = new (std::nothrow) GaltekCredentialProvider(pipeName);
        FakeCredentialProviderEvents* events = new (std::nothrow) FakeCredentialProviderEvents();
        if (callbackProvider == nullptr || events == nullptr)
        {
            if (events != nullptr)
            {
                events->Release();
            }

            if (callbackProvider != nullptr)
            {
                callbackProvider->Release();
            }

            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"callback test allocation");
        }

        ICredentialProvider* callbackInterface = nullptr;
        if (Failed(callbackProvider->QueryInterface(
                IID_ICredentialProvider,
                reinterpret_cast<void**>(&callbackInterface)))
            || callbackInterface == nullptr
            || callbackInterface->SetUsageScenario(CPUS_LOGON, 0) != S_OK
            || Failed(callbackInterface->Advise(events, 42)))
        {
            events->Release();
            if (callbackInterface != nullptr)
            {
                callbackInterface->Release();
            }

            callbackProvider->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"callback provider setup");
        }

        if (!events->WaitForCalls(1, 2000)
            || events->Calls() != 1
            || events->LastContext() != 42)
        {
            callbackInterface->UnAdvise();
            events->Release();
            callbackInterface->Release();
            callbackProvider->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"WAIT success should call CredentialsChanged exactly once");
        }

        if (events->WaitForCalls(2, 300))
        {
            callbackInterface->UnAdvise();
            events->Release();
            callbackInterface->Release();
            callbackProvider->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"worker must rearm without rapid duplicate callback after success");
        }

        callbackInterface->UnAdvise();
        events->Release();
        callbackInterface->Release();
        callbackProvider->Release();
        scriptedServer.Stop();
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        ScriptedBridgeServer scriptedServer(pipeName);
        scriptedServer.AddWaitResponse(2);
        scriptedServer.AddWaitResponse(2);
        scriptedServer.SetIdentityAvailable(true);
        if (!scriptedServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"scripted bridge setup for callback failure");
        }

        GaltekCredentialProvider* retryProvider = new (std::nothrow) GaltekCredentialProvider(pipeName);
        FakeCredentialProviderEvents* events = new (std::nothrow) FakeCredentialProviderEvents();
        if (retryProvider == nullptr || events == nullptr)
        {
            if (events != nullptr)
            {
                events->Release();
            }

            if (retryProvider != nullptr)
            {
                retryProvider->Release();
            }

            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"callback retry allocation");
        }

        events->SetResult(E_FAIL);
        ICredentialProvider* retryInterface = nullptr;
        if (Failed(retryProvider->QueryInterface(
                IID_ICredentialProvider,
                reinterpret_cast<void**>(&retryInterface)))
            || retryInterface == nullptr
            || retryInterface->SetUsageScenario(CPUS_LOGON, 0) != S_OK
            || Failed(retryInterface->Advise(events, 84)))
        {
            events->Release();
            if (retryInterface != nullptr)
            {
                retryInterface->Release();
            }

            retryProvider->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"callback retry provider setup");
        }

        if (!events->WaitForCalls(1, 2000))
        {
            retryInterface->UnAdvise();
            events->Release();
            retryInterface->Release();
            retryProvider->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"callback failure should be observable");
        }

        events->SetResult(S_OK);
        if (!events->WaitForCalls(2, 3000)
            || !scriptedServer.WaitForWaitRequestCount(2, 1000))
        {
            retryInterface->UnAdvise();
            events->Release();
            retryInterface->Release();
            retryProvider->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"failed CredentialsChanged should preserve generation for retry");
        }

        const std::vector<long long> observed = scriptedServer.WaitObservedGenerations();
        if (observed.size() < 2
            || observed[0] != 0
            || observed[1] != 0
            || scriptedServer.MillisecondsBetweenRequests(0, 1) < 200)
        {
            retryInterface->UnAdvise();
            events->Release();
            retryInterface->Release();
            retryProvider->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"callback failure must not advance generation or rapid-poll");
        }

        if (Failed(retryInterface->UnAdvise()))
        {
            events->Release();
            retryInterface->Release();
            retryProvider->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"retry worker should cancel before identity snapshot check");
        }

        // The scripted long-poll server has a single pipe instance. Replace it
        // after worker cancellation so the identity request models the Agent's
        // independent bridge listener instead of racing a test-only blocked pipe.
        scriptedServer.Stop();
        ScriptedBridgeServer identityServer(pipeName);
        identityServer.SetIdentityAvailable(true);
        if (!identityServer.Start())
        {
            events->Release();
            retryInterface->Release();
            retryProvider->Release();
            providerInterface->Release();
            provider->Release();
            return Fail(L"identity server should start after callback worker cancellation");
        }

        DWORD count = 0;
        DWORD defaultCredential = CREDENTIAL_PROVIDER_NO_DEFAULT;
        BOOL autoLogon = FALSE;
        if (Failed(retryInterface->GetCredentialCount(&count, &defaultCredential, &autoLogon))
            || count != 1
            || defaultCredential != 0
            || autoLogon != TRUE)
        {
            retryInterface->UnAdvise();
            events->Release();
            retryInterface->Release();
            retryProvider->Release();
            identityServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"existing activation should survive callback/rearm race");
        }

        ICredentialProviderCredential* credentialFromSnapshot = nullptr;
        if (Failed(retryInterface->GetCredentialAt(0, &credentialFromSnapshot))
            || credentialFromSnapshot == nullptr)
        {
            retryInterface->UnAdvise();
            events->Release();
            retryInterface->Release();
            retryProvider->Release();
            identityServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"successful callback should allow identity snapshot credential");
        }

        credentialFromSnapshot->Release();
        events->Release();
        retryInterface->Release();
        retryProvider->Release();
        identityServer.Stop();
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        ScriptedBridgeServer scriptedServer(pipeName);
        scriptedServer.AddWaitResponse(3, 1000);
        if (!scriptedServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"scripted bridge setup for UnAdvise cancellation");
        }

        GaltekCredentialProvider* cancelProvider = new (std::nothrow) GaltekCredentialProvider(pipeName);
        FakeCredentialProviderEvents* events = new (std::nothrow) FakeCredentialProviderEvents();
        ICredentialProvider* cancelInterface = nullptr;
        if (cancelProvider == nullptr
            || events == nullptr
            || Failed(cancelProvider->QueryInterface(
                IID_ICredentialProvider,
                reinterpret_cast<void**>(&cancelInterface)))
            || cancelInterface == nullptr
            || cancelInterface->SetUsageScenario(CPUS_LOGON, 0) != S_OK
            || Failed(cancelInterface->Advise(events, 7))
            || !scriptedServer.WaitForWaitRequestCount(1, 1000)
            || Failed(cancelInterface->UnAdvise()))
        {
            if (events != nullptr)
            {
                events->Release();
            }

            if (cancelInterface != nullptr)
            {
                cancelInterface->Release();
            }

            if (cancelProvider != nullptr)
            {
                cancelProvider->Release();
            }

            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"UnAdvise cancellation setup");
        }

        Sleep(1100);
        if (events->Calls() != 0)
        {
            events->Release();
            cancelInterface->Release();
            cancelProvider->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"UnAdvise must cancel worker without later callback");
        }

        events->Release();
        cancelInterface->Release();
        cancelProvider->Release();
        scriptedServer.Stop();
    }

    {
        const std::wstring pipeName = CreateTestPipeName();
        ScriptedBridgeServer scriptedServer(pipeName);
        scriptedServer.AddWaitResponse(4, 1000);
        if (!scriptedServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"scripted bridge setup for provider destruction");
        }

        GaltekCredentialProvider* destroyProvider = new (std::nothrow) GaltekCredentialProvider(pipeName);
        FakeCredentialProviderEvents* events = new (std::nothrow) FakeCredentialProviderEvents();
        ICredentialProvider* destroyInterface = nullptr;
        if (destroyProvider == nullptr
            || events == nullptr
            || Failed(destroyProvider->QueryInterface(
                IID_ICredentialProvider,
                reinterpret_cast<void**>(&destroyInterface)))
            || destroyInterface == nullptr
            || destroyInterface->SetUsageScenario(CPUS_LOGON, 0) != S_OK
            || Failed(destroyInterface->Advise(events, 9))
            || !scriptedServer.WaitForWaitRequestCount(1, 1000))
        {
            if (events != nullptr)
            {
                events->Release();
            }

            if (destroyInterface != nullptr)
            {
                destroyInterface->Release();
            }

            if (destroyProvider != nullptr)
            {
                destroyProvider->Release();
            }

            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"provider destruction setup");
        }

        destroyInterface->Release();
        destroyProvider->Release();
        Sleep(1100);
        if (events->Calls() != 0)
        {
            events->Release();
            scriptedServer.Stop();
            providerInterface->Release();
            provider->Release();
            return Fail(L"provider destruction must not leave callback using stale pointer");
        }

        events->Release();
        scriptedServer.Stop();
    }

    for (DWORD index = 0; index < 25; index++)
    {
        GaltekCredentialProvider* repeatedProvider = new (std::nothrow) GaltekCredentialProvider();
        if (repeatedProvider == nullptr)
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"repeated provider allocation");
        }

        ICredentialProvider* repeatedInterface = nullptr;
        if (Failed(repeatedProvider->QueryInterface(
                IID_ICredentialProvider,
                reinterpret_cast<void**>(&repeatedInterface)))
            || repeatedInterface == nullptr)
        {
            repeatedProvider->Release();
            providerInterface->Release();
            provider->Release();
            return Fail(L"repeated provider QueryInterface");
        }

        if (repeatedInterface->SetUsageScenario(CPUS_LOGON, 0) != S_OK
            || VerifyFailOpenCredentialCount(
                repeatedInterface,
                L"repeated provider should stay fail-open") != 0)
        {
            repeatedInterface->Release();
            repeatedProvider->Release();
            providerInterface->Release();
            provider->Release();
            return 1;
        }

        repeatedInterface->Release();
        repeatedProvider->Release();
    }

    if (SUCCEEDED(providerInterface->SetUsageScenario(CPUS_CREDUI, 0)))
    {
        providerInterface->Release();
        provider->Release();
        return Fail(L"CPUS_CREDUI should be unsupported");
    }

    providerInterface->Release();
    provider->Release();

    BridgeActivationIdentity identity;
    identity.userSid = L"S-1-5-21-1000000000-1000000000-1000000000-1004";
    identity.domain = L"AULA";
    identity.username = L"Primaria";
    identity.autoSubmitRequested = false;

    GaltekCredential* credential = new (std::nothrow) GaltekCredential(identity);
    if (credential == nullptr)
    {
        return Fail(L"credential allocation");
    }

    ICredentialProviderCredential2* credential2 = nullptr;
    if (Failed(credential->QueryInterface(
            IID_ICredentialProviderCredential2,
            reinterpret_cast<void**>(&credential2)))
        || credential2 == nullptr)
    {
        credential->Release();
        return Fail(L"ICredentialProviderCredential2 construction");
    }

    CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE serializationResponse = CPGSR_RETURN_CREDENTIAL_FINISHED;
    CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION serialization{};
    PWSTR statusText = reinterpret_cast<PWSTR>(1);
    CREDENTIAL_PROVIDER_STATUS_ICON statusIcon = CPSI_ERROR;
    if (Failed(credential2->GetSerialization(
            &serializationResponse,
            &serialization,
            &statusText,
            &statusIcon))
        || serializationResponse != CPGSR_NO_CREDENTIAL_NOT_FINISHED
        || serialization.rgbSerialization != nullptr
        || serialization.cbSerialization != 0
        || statusText != nullptr
        || statusIcon != CPSI_NONE)
    {
        credential2->Release();
        credential->Release();
        return Fail(L"GetSerialization without activation id must not return a credential");
    }

    BOOL selectedAutoLogon = TRUE;
    if (Failed(credential2->SetSelected(&selectedAutoLogon)) || selectedAutoLogon != FALSE)
    {
        credential2->Release();
        credential->Release();
        return Fail(L"SetSelected must not request auto-logon without remote activation");
    }

    PWSTR sid = nullptr;
    if (Failed(credential2->GetUserSid(&sid))
        || sid == nullptr
        || wcscmp(sid, identity.userSid.c_str()) != 0)
    {
        if (sid != nullptr)
        {
            CoTaskMemFree(sid);
        }

        credential2->Release();
        credential->Release();
        return Fail(L"GetUserSid should return service-derived SID");
    }

    CoTaskMemFree(sid);

    credential2->Release();
    credential->Release();

    BridgeActivationIdentity autoSubmitIdentity;
    autoSubmitIdentity.activationId = "11111111-2222-3333-4444-555555555555";
    autoSubmitIdentity.accountId = "PRIMARY";
    autoSubmitIdentity.userSid = identity.userSid;
    autoSubmitIdentity.domain = identity.domain;
    autoSubmitIdentity.username = identity.username;
    autoSubmitIdentity.autoSubmitRequested = true;

    GaltekCredential* autoCredential = new (std::nothrow) GaltekCredential(autoSubmitIdentity);
    if (autoCredential == nullptr)
    {
        return Fail(L"auto-submit credential allocation");
    }

    ICredentialProviderCredential2* autoCredential2 = nullptr;
    if (Failed(autoCredential->QueryInterface(
            IID_ICredentialProviderCredential2,
            reinterpret_cast<void**>(&autoCredential2)))
        || autoCredential2 == nullptr)
    {
        autoCredential->Release();
        return Fail(L"auto-submit ICredentialProviderCredential2 construction");
    }

    BOOL firstAutoLogon = FALSE;
    BOOL secondAutoLogon = TRUE;
    if (Failed(autoCredential2->SetSelected(&firstAutoLogon)) || firstAutoLogon != TRUE
        || Failed(autoCredential2->SetSelected(&secondAutoLogon)) || secondAutoLogon != FALSE)
    {
        autoCredential2->Release();
        autoCredential->Release();
        return Fail(L"remote auto-submit must be requested exactly once");
    }

    autoCredential2->Release();
    autoCredential->Release();

    {
        BYTE* packed = nullptr;
        DWORD packedSize = 0;
        if (Failed(GaltekTestPackKerbInteractiveUnlockLogon(
                L"ICH11",
                L"ICH-PRIMARIA-14",
                L"ProtectedPasswordValue",
                &packed,
                &packedSize))
            || packed == nullptr
            || !ValidatePackedKerbInteractiveUnlockLogon(
                packed,
                packedSize,
                L"ICH11",
                L"ICH-PRIMARIA-14",
                L"ProtectedPasswordValue"))
        {
            if (packed != nullptr)
            {
                SecureZeroMemory(packed, packedSize);
                CoTaskMemFree(packed);
            }

            return Fail(L"packed KERB_INTERACTIVE_UNLOCK_LOGON must use in-blob relative offsets");
        }

        SecureZeroMemory(packed, packedSize);
        CoTaskMemFree(packed);
    }

    ULONG negotiatePackage = 0;
    if (Failed(GaltekTestRetrieveNegotiateAuthPackage(&negotiatePackage)))
    {
        return Fail(L"Negotiate authentication package must resolve successfully");
    }

    BridgeActivationIdentity retryIdentity;
    retryIdentity.activationId = "22222222-3333-4444-5555-666666666666";
    retryIdentity.accountId = "PRIMARY";
    retryIdentity.userSid = identity.userSid;
    retryIdentity.domain = L"ICH11";
    retryIdentity.username = L"ICH-PRIMARIA-14";
    retryIdentity.autoSubmitRequested = true;
    GaltekCredential* retryCredential = new (std::nothrow) GaltekCredential(retryIdentity);
    if (retryCredential == nullptr)
    {
        return Fail(L"retry credential allocation");
    }

    ICredentialProviderCredential2* retryCredential2 = nullptr;
    if (Failed(retryCredential->QueryInterface(
            IID_ICredentialProviderCredential2,
            reinterpret_cast<void**>(&retryCredential2)))
        || retryCredential2 == nullptr)
    {
        retryCredential->Release();
        return Fail(L"retry credential QueryInterface");
    }

    CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE firstResponse = CPGSR_RETURN_CREDENTIAL_FINISHED;
    CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION firstSerialization{};
    PWSTR firstStatusText = reinterpret_cast<PWSTR>(1);
    CREDENTIAL_PROVIDER_STATUS_ICON firstStatusIcon = CPSI_ERROR;
    CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE secondResponse = CPGSR_RETURN_CREDENTIAL_FINISHED;
    CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION secondSerialization{};
    PWSTR secondStatusText = reinterpret_cast<PWSTR>(1);
    CREDENTIAL_PROVIDER_STATUS_ICON secondStatusIcon = CPSI_ERROR;
    if (Failed(retryCredential2->GetSerialization(
            &firstResponse,
            &firstSerialization,
            &firstStatusText,
            &firstStatusIcon))
        || Failed(retryCredential2->GetSerialization(
            &secondResponse,
            &secondSerialization,
            &secondStatusText,
            &secondStatusIcon))
        || firstResponse != CPGSR_NO_CREDENTIAL_NOT_FINISHED
        || secondResponse != CPGSR_NO_CREDENTIAL_NOT_FINISHED
        || firstSerialization.rgbSerialization != nullptr
        || secondSerialization.rgbSerialization != nullptr
        || firstSerialization.cbSerialization != 0
        || secondSerialization.cbSerialization != 0
        || firstStatusText != nullptr
        || secondStatusText != nullptr
        || firstStatusIcon != CPSI_NONE
        || secondStatusIcon != CPSI_NONE)
    {
        retryCredential2->Release();
        retryCredential->Release();
        return Fail(L"GetSerialization must not reacquire or return stale serialization after a failed acquire");
    }

    if (Failed(retryCredential2->ReportResult(
            kStatusLogonFailure,
            kStatusSuccess,
            &firstStatusText,
            &firstStatusIcon))
        || firstStatusText != nullptr
        || firstStatusIcon != CPSI_NONE)
    {
        retryCredential2->Release();
        retryCredential->Release();
        return Fail(L"ReportResult must preserve local failure UI semantics");
    }

    retryCredential2->Release();
    retryCredential->Release();

    BridgeClient bridge;
    BridgeActivationStatus bridgeStatus = bridge.GetPendingActivationMetadata(1);
    if (bridgeStatus != BridgeActivationStatus::ServiceUnavailable
        && bridgeStatus != BridgeActivationStatus::NoActivation
        && bridgeStatus != BridgeActivationStatus::PendingActivation)
    {
        return Fail(L"bridge status should be closed enum value");
    }

    std::wcout << L"Credential Provider self-test passed" << std::endl;
    if (shouldUninitializeCom)
    {
        CoUninitialize();
    }

    return 0;
}
