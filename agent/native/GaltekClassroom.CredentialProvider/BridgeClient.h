#pragma once

#include <windows.h>

enum class BridgeActivationStatus
{
    ServiceUnavailable,
    NoActivation,
    PendingActivation
};

class BridgeClient
{
public:
    BridgeActivationStatus GetPendingActivationMetadata(DWORD timeoutMilliseconds) const;
};
