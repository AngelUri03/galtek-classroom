#pragma once

#include "BridgeClient.h"

#include <credentialprovider.h>
#include <thread>
#include <atomic>

class GaltekCredentialProvider final : public ICredentialProvider
{
public:
    GaltekCredentialProvider();

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
    void StopNotificationWorker();
    void NotificationWorker(IStream* eventsStream, UINT_PTR adviseContext, HANDLE stopEvent);

    LONG _referenceCount;
    CREDENTIAL_PROVIDER_USAGE_SCENARIO _usageScenario;
    UINT_PTR _adviseContext;
    bool _hasCredential;
    BridgeActivationIdentity _identity;
    HANDLE _notificationStopEvent;
    std::thread _notificationThread;
    std::atomic_bool _notificationRunning;
};
