using GaltekClassroom.Agent.Service.Identity;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class MasterConnectionRetryLoop
{
    private readonly MasterConnectionOptions _options;
    private readonly IReconnectJitter _jitter;
    private readonly MasterConnectionStateTracker _stateTracker;
    private readonly ISystemClock _clock;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public MasterConnectionRetryLoop(
        MasterConnectionOptions options,
        IReconnectJitter jitter,
        MasterConnectionStateTracker stateTracker,
        ISystemClock clock,
        ILogger logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _options = options;
        _jitter = jitter;
        _stateTracker = stateTracker;
        _clock = clock;
        _logger = logger;
        _delay = delay ?? Task.Delay;
    }

    public async Task RunAsync(Func<CancellationToken, Task> connect, CancellationToken stoppingToken)
    {
        var backoff = new MasterConnectionBackoff(
            _options.ReconnectDelays, _jitter, _options.ReconnectJitterMax);
        var initialDelay = backoff.InitialDelay(_options.InitialConnectJitterMax);
        if (initialDelay > TimeSpan.Zero)
        {
            await _delay(initialDelay, stoppingToken);
        }

        var attempt = 0;
        var failureStreak = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;
            var stateBeforeAttempt = _stateTracker.Snapshot;
            if (failureStreak == 0)
            {
                _logger.LogInformation("MASTER_CONNECTION_ATTEMPT Attempt={Attempt} State={State}",
                    attempt, stateBeforeAttempt.State);
            }
            else
            {
                _logger.LogDebug("MASTER_CONNECTION_ATTEMPT Attempt={Attempt} State={State}",
                    attempt, stateBeforeAttempt.State);
            }
            try
            {
                await connect(stoppingToken);
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                throw new IOException("Master gRPC stream ended.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                var currentState = _stateTracker.Snapshot;
                var wasOnline = currentState.State == MasterConnectionState.Online;
                if (wasOnline)
                {
                    backoff.Reset();
                    failureStreak = 0;
                }

                var reason = !ReferenceEquals(currentState, stateBeforeAttempt)
                    && currentState.State == MasterConnectionState.Offline
                    ? currentState.ReasonCode ?? exception.GetType().Name
                    : exception.GetType().Name;
                _stateTracker.SetOffline(_options.MasterNetworkIdentityId, _clock.UtcNow, reason, null);
                if (wasOnline)
                {
                    _logger.LogWarning("MASTER_CONNECTION_LOST Attempt={Attempt} ReasonCategory={ReasonCategory}",
                        attempt, reason);
                }
                else if (failureStreak == 0)
                {
                    _logger.LogWarning("Master connection failed. Attempt={Attempt} ReasonCategory={ReasonCategory}",
                        attempt, reason);
                }
                else
                {
                    _logger.LogDebug("Master connection still failing. Attempt={Attempt} ReasonCategory={ReasonCategory}",
                        attempt, reason);
                }

                failureStreak++;
            }

            var retryDelay = backoff.NextDelay();
            if (failureStreak == 1)
            {
                _logger.LogInformation("MASTER_CONNECTION_RETRY_SCHEDULED Attempt={Attempt} DelayMs={DelayMs}",
                    attempt + 1, retryDelay.TotalMilliseconds);
            }
            else
            {
                _logger.LogDebug("MASTER_CONNECTION_RETRY_SCHEDULED Attempt={Attempt} DelayMs={DelayMs}",
                    attempt + 1, retryDelay.TotalMilliseconds);
            }

            await _delay(retryDelay, stoppingToken);
        }
    }
}
