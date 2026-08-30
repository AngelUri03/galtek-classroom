using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Agent.Service.Network;

namespace GaltekClassroom.Agent.Service.Runtime;

public sealed class AgentRuntimeState
{
    private readonly object _sync = new();
    private InstallationIdentity? _installationIdentity;
    private NetworkIdentityMetadata? _networkIdentity;
    private readonly TaskCompletionSource<InstallationIdentity> _installationIdentityReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<NetworkIdentityMetadata> _networkIdentityReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AgentStartupPhase _startupPhase = AgentStartupPhase.Starting;
    private bool _previousShutdownWasUnclean;
    private bool _recoveryActive;
    private bool _operationReadyRequested;

    public AgentRuntimeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new AgentRuntimeSnapshot(
                    _startupPhase,
                    _previousShutdownWasUnclean,
                    _recoveryActive);
            }
        }
    }

    public void ObservePreviousShutdown(bool previousShutdownWasUnclean)
    {
        lock (_sync)
        {
            _previousShutdownWasUnclean = previousShutdownWasUnclean;
            _recoveryActive = previousShutdownWasUnclean;
            _startupPhase = previousShutdownWasUnclean
                ? AgentStartupPhase.Recovering
                : AgentStartupPhase.Starting;
        }
    }

    public void SetInstallationIdentity(InstallationIdentity installationIdentity)
    {
        ArgumentNullException.ThrowIfNull(installationIdentity);

        lock (_sync)
        {
            _installationIdentity = installationIdentity;
            AdvanceTo(AgentStartupPhase.MinimalReady);
        }

        _installationIdentityReady.TrySetResult(installationIdentity);
    }

    public InstallationIdentity GetInstallationIdentity()
    {
        lock (_sync)
        {
            return _installationIdentity
                ?? throw new InvalidOperationException("Installation identity has not been resolved yet.");
        }
    }

    public Task<InstallationIdentity> WaitForInstallationIdentityAsync(CancellationToken cancellationToken)
    {
        return _installationIdentityReady.Task.WaitAsync(cancellationToken);
    }

    public void SetNetworkIdentity(NetworkIdentityMetadata networkIdentity)
    {
        ArgumentNullException.ThrowIfNull(networkIdentity);

        lock (_sync)
        {
            _networkIdentity = networkIdentity;
            _recoveryActive = false;
            AdvanceTo(AgentStartupPhase.SecurityReady);
        }

        _networkIdentityReady.TrySetResult(networkIdentity);
    }

    public NetworkIdentityMetadata GetNetworkIdentity()
    {
        lock (_sync)
        {
            return _networkIdentity
                ?? throw new InvalidOperationException("Network identity has not been resolved yet.");
        }
    }

    public Task<NetworkIdentityMetadata> WaitForNetworkIdentityAsync(CancellationToken cancellationToken)
    {
        return _networkIdentityReady.Task.WaitAsync(cancellationToken);
    }

    public void MarkNetworkReady()
    {
        lock (_sync)
        {
            AdvanceTo(_operationReadyRequested
                ? AgentStartupPhase.OperationReady
                : AgentStartupPhase.NetworkReady);
        }
    }

    public void MarkOperationReady()
    {
        lock (_sync)
        {
            _operationReadyRequested = true;
            if (Rank(_startupPhase) >= Rank(AgentStartupPhase.NetworkReady))
            {
                _startupPhase = AgentStartupPhase.OperationReady;
            }
        }
    }

    public void MarkDegraded()
    {
        lock (_sync)
        {
            if (_startupPhase != AgentStartupPhase.OperationReady)
            {
                _startupPhase = AgentStartupPhase.Degraded;
            }
        }
    }

    private void AdvanceTo(AgentStartupPhase phase)
    {
        if (Rank(phase) > Rank(_startupPhase))
        {
            _startupPhase = phase;
        }
    }

    private static int Rank(AgentStartupPhase phase)
    {
        return phase switch
        {
            AgentStartupPhase.Starting => 0,
            AgentStartupPhase.Recovering => 1,
            AgentStartupPhase.MinimalReady => 2,
            AgentStartupPhase.SecurityReady => 3,
            AgentStartupPhase.Degraded => 3,
            AgentStartupPhase.NetworkReady => 4,
            AgentStartupPhase.OperationReady => 5,
            _ => 0
        };
    }
}
