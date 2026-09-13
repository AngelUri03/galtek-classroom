#include "../GaltekClassroom.CredentialProvider/BridgeClient.h"
#include "../GaltekClassroom.CredentialProvider/Credential.h"
#include "../GaltekClassroom.CredentialProvider/CredentialProvider.h"

#include <credentialprovider.h>
#include <windows.h>

#include <chrono>
#include <cstring>
#include <iostream>
#include <new>
#include <thread>

long g_objectCount = 0;

namespace
{
    constexpr wchar_t kBridgePipeName[] = L"\\\\.\\pipe\\GaltekClassroom.CredentialProvider.v1";
    constexpr DWORD kBoundedEnumerationMilliseconds = 2000;

    int Fail(const wchar_t* message)
    {
        std::wcerr << L"FAILED: " << message << std::endl;
        return 1;
    }

    bool Failed(HRESULT hr)
    {
        return FAILED(hr);
    }

    class HangingBridgeServer
    {
    public:
        HangingBridgeServer() = default;
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
                kBridgePipeName,
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
        HANDLE _stopEvent = nullptr;
        HANDLE _readyEvent = nullptr;
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
        HangingBridgeServer hangingServer;
        if (!hangingServer.Start())
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"hanging bridge server setup");
        }

        const auto started = std::chrono::steady_clock::now();
        failOpenResult = VerifyFailOpenCredentialCount(
            providerInterface,
            L"hanging bridge should enumerate zero credentials");
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - started);
        hangingServer.Stop();
        if (failOpenResult != 0)
        {
            providerInterface->Release();
            provider->Release();
            return failOpenResult;
        }

        if (elapsed.count() > kBoundedEnumerationMilliseconds)
        {
            providerInterface->Release();
            provider->Release();
            return Fail(L"hanging bridge should not block LogonUI enumeration");
        }
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

    BridgeClient bridge;
    BridgeActivationStatus bridgeStatus = bridge.GetPendingActivationMetadata(1);
    if (bridgeStatus != BridgeActivationStatus::ServiceUnavailable
        && bridgeStatus != BridgeActivationStatus::NoActivation
        && bridgeStatus != BridgeActivationStatus::PendingActivation)
    {
        return Fail(L"bridge status should be closed enum value");
    }

    std::wcout << L"Credential Provider self-test passed" << std::endl;
    return 0;
}
