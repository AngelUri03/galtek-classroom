#include "../GaltekClassroom.CredentialProvider/BridgeClient.h"
#include "../GaltekClassroom.CredentialProvider/Credential.h"
#include "../GaltekClassroom.CredentialProvider/CredentialProvider.h"

#include <credentialprovider.h>
#include <cstring>
#include <iostream>
#include <new>

long g_objectCount = 0;

namespace
{
    int Fail(const wchar_t* message)
    {
        std::wcerr << L"FAILED: " << message << std::endl;
        return 1;
    }

    bool Failed(HRESULT hr)
    {
        return FAILED(hr);
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

    DWORD count = 99;
    DWORD defaultCredential = 0;
    BOOL autoLogon = TRUE;
    if (Failed(providerInterface->GetCredentialCount(&count, &defaultCredential, &autoLogon))
        || count != 0
        || defaultCredential != CREDENTIAL_PROVIDER_NO_DEFAULT
        || autoLogon != FALSE)
    {
        providerInterface->Release();
        provider->Release();
        return Fail(L"no activation should enumerate zero credentials");
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
        return Fail(L"SetSelected must not request auto-logon in 19G2");
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
