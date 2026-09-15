#pragma once

#include "BridgeClient.h"

#include <credentialprovider.h>
#include <thread>
#include <atomic>
#include <mutex>
#include <string>

class GaltekCredentialProvider final : public ICredentialProvider
{
public:
    GaltekCredentialProvider();
#ifdef GALTEK_CREDENTIAL_PROVIDER_TESTS
    explicit GaltekCredentialProvider(const std::wstring& bridgePipeName);
#endif

    IFACEMETHODIMP QueryInterface(REFIID riid, void** object) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;

    IFACEMETHODIMP SetUsageScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO usageScenario, DWORD flags) override;
    IFACEMETHODIMP SetSerialization(const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* serialization) override;
    IFACEMETHODIMP Advise(ICredentialProviderEvents* events, UINT_PTR adviseContext) override;
    IFACEMETHODIMP UnAdvise() override;
    IFACEMETHODIMP GetFieldDescriptorCount(DWORD* count) override;
    IFACEMETHODIMP GetFieldDescriptorAt(DWORD fieldId, CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** descriptor) override;
    IFACEMETHODIMP GetCredentialCount(DWORD* count, DWORD* defaultCredential, BOOL* autoLogonWithDefault) override;
    IFACEMETHODIMP GetCredentialAt(DWORD credentialIndex, ICredentialProviderCredential** credential) override;

private:
    ~GaltekCredentialProvider();
    void StopNotificationWorker(const wchar_t* reason);
    static void NotificationWorker(
        IStream* eventsStream,
        UINT_PTR adviseContext,
        HANDLE stopEvent,
        std::wstring bridgePipeName);

    LONG _referenceCount;
    CREDENTIAL_PROVIDER_USAGE_SCENARIO _usageScenario;
    UINT_PTR _adviseContext;
    bool _hasCredential;
    BridgeActivationIdentity _identity;
    std::mutex _stateMutex;
    HANDLE _notificationStopEvent;
    std::thread _notificationThread;
    std::atomic_bool _notificationRunning;
    std::wstring _bridgePipeName;
};
