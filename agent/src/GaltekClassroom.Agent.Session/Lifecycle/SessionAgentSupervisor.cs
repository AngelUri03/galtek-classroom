using GaltekClassroom.Agent.Session.Ipc;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Lifecycle;

public sealed class SessionAgentSupervisor
{
    private readonly ILocalAgentIpcClient _client;
    private readonly ISessionAgentDelay _delay;
    private readonly SessionAgentSupervisorOptions _options;
    private readonly object _sync = new();
    private SessionAgentLifecycleState _state = SessionAgentLifecycleState.Starting;
    private LocalDeviceStatus? _lastDeviceStatus;

    public SessionAgentSupervisor(
        ILocalAgentIpcClient client,
        ISessionAgentDelay delay,
        SessionAgentSupervisorOptions options)
    {
        _client = client;
        _delay = delay;
        _options = options;
    }

    public event Action<SessionAgentLifecycleState>? StateChanged;

    public SessionAgentLifecycleState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public LocalDeviceStatus? LastDeviceStatus
    {
        get
        {
            lock (_sync)
            {
                return _lastDeviceStatus;
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = new ReconnectBackoff(_options.ReconnectDelays);
        SetState(SessionAgentLifecycleState.Starting);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SetState(SessionAgentLifecycleState.WaitingForService);

                try
                {
                    var statusResult = await _client.TryGetDeviceStatusAsync(cancellationToken);
                    if (!statusResult.Succeeded || statusResult.Payload is null)
                    {
                        await _delay.DelayAsync(backoff.NextDelay(), cancellationToken);
                        continue;
                    }

                    SetState(SessionAgentLifecycleState.Connected);

                    SetLastDeviceStatus(statusResult.Payload);
                    SetState(SessionAgentLifecycleState.Ready);
                    backoff.Reset();

                    await PollWhileHealthyAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (IsRecoverableIpcFailure(exception))
                {
                    await _delay.DelayAsync(backoff.NextDelay(), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            SetState(SessionAgentLifecycleState.Stopping);
        }
    }

    private async Task PollWhileHealthyAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _delay.DelayAsync(_options.HealthyPollInterval, cancellationToken);

            try
            {
                var ping = await _client.TryPingAsync(cancellationToken);
                if (!ping.Succeeded)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableIpcFailure(exception))
            {
                return;
            }
        }
    }

    private void SetLastDeviceStatus(LocalDeviceStatus status)
    {
        lock (_sync)
        {
            _lastDeviceStatus = status;
        }
    }

    private void SetState(SessionAgentLifecycleState state)
    {
        var changed = false;

        lock (_sync)
        {
            if (_state != state)
            {
                _state = state;
                changed = true;
            }
        }

        if (changed)
        {
            StateChanged?.Invoke(state);
        }
    }

    private static bool IsRecoverableIpcFailure(Exception exception)
    {
        return exception is not OperationCanceledException;
    }
}
