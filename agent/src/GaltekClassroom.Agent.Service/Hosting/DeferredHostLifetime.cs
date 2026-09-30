namespace GaltekClassroom.Agent.Service.Hosting;

/// <summary>
/// The Windows Service bootstrap owns the SCM lifetime. The Generic Host must
/// therefore avoid installing a second Console/WindowsService lifetime.
/// </summary>
public sealed class DeferredHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
