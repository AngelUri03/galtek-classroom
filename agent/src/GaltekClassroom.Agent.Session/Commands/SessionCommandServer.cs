using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Commands;

public interface ISessionCommandServer
{
    Task RunAsync(int sessionId, CancellationToken cancellationToken);
}

public sealed class NoOpSessionCommandServer : ISessionCommandServer
{
    public async Task RunAsync(int sessionId, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}

public interface ISessionCommandPipeStreamFactory
{
    NamedPipeServerStream CreateServerStream(string pipeName);
}

public interface ISessionCommandCallerVerifier
{
    bool IsAuthorized(PipeStream pipe);
}

public sealed class SessionCommandServer : ISessionCommandServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISessionCommandPipeStreamFactory _pipeStreamFactory;
    private readonly ISessionCommandCallerVerifier _callerVerifier;
    private readonly IUrlLauncher _urlLauncher;
    private readonly ISessionApplicationResolver _applicationResolver;
    private readonly IWindowsApplicationLauncher _applicationLauncher;
    private readonly IInputBlockCoordinator _inputBlockCoordinator;

    public SessionCommandServer(
        ISessionCommandPipeStreamFactory pipeStreamFactory,
        ISessionCommandCallerVerifier callerVerifier,
        IUrlLauncher? urlLauncher = null,
        ISessionApplicationResolver? applicationResolver = null,
        IWindowsApplicationLauncher? applicationLauncher = null,
        IInputBlockCoordinator? inputBlockCoordinator = null)
    {
        _pipeStreamFactory = pipeStreamFactory;
        _callerVerifier = callerVerifier;
        _urlLauncher = urlLauncher ?? new UnavailableUrlLauncher();
        _applicationResolver = applicationResolver ?? new UnavailableSessionApplicationResolver();
        _applicationLauncher = applicationLauncher ?? new UnavailableWindowsApplicationLauncher();
        _inputBlockCoordinator = inputBlockCoordinator ?? new WindowsInputBlockCoordinator(new UnavailableWindowsInputBlockApi());
    }

    public async Task RunAsync(int sessionId, CancellationToken cancellationToken)
    {
        var pipeName = SessionCommandProtocol.PipeNameForSession(sessionId);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var pipe = _pipeStreamFactory.CreateServerStream(pipeName);

                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    await ProcessConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (IOException)
                {
                }
                catch (JsonException)
                {
                }
                catch (SessionCommandFramingException)
                {
                }
            }
        }
        finally
        {
            _ = await _inputBlockCoordinator.CleanupAsync(TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
        }
    }

    private async Task ProcessConnectionAsync(
        PipeStream pipe,
        CancellationToken cancellationToken)
    {
        if (!_callerVerifier.IsAuthorized(pipe))
        {
            await WriteResponseAsync(
                pipe,
                SessionCommandResponse.Error(
                    string.Empty,
                    SessionCommandErrorCodes.SessionChannelUnauthorized),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        SessionCommandResponse response;
        try
        {
            var requestJson = await SessionCommandFraming.ReadJsonAsync(pipe, cancellationToken)
                .ConfigureAwait(false);
            var request = JsonSerializer.Deserialize<SessionCommandRequest>(requestJson, JsonOptions);
            response = await HandleAsync(
                request,
                _urlLauncher,
                _applicationResolver,
                _applicationLauncher,
                _inputBlockCoordinator,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            response = SessionCommandResponse.Error(
                string.Empty,
                SessionCommandErrorCodes.SessionChannelMalformedRequest);
        }
        catch (SessionCommandFramingException)
        {
            response = SessionCommandResponse.Error(
                string.Empty,
                SessionCommandErrorCodes.SessionChannelMalformedRequest);
        }

        await WriteResponseAsync(pipe, response, cancellationToken).ConfigureAwait(false);
    }

    public static SessionCommandResponse Handle(SessionCommandRequest? request)
    {
        return HandleAsync(request, new UnavailableUrlLauncher(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public static async Task<SessionCommandResponse> HandleAsync(
        SessionCommandRequest? request,
        IUrlLauncher urlLauncher,
        CancellationToken cancellationToken)
    {
        return await HandleAsync(
            request,
            urlLauncher,
            new UnavailableSessionApplicationResolver(),
            new UnavailableWindowsApplicationLauncher(),
            new WindowsInputBlockCoordinator(new UnavailableWindowsInputBlockApi()),
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<SessionCommandResponse> HandleAsync(
        SessionCommandRequest? request,
        IUrlLauncher urlLauncher,
        ISessionApplicationResolver applicationResolver,
        IWindowsApplicationLauncher applicationLauncher,
        IInputBlockCoordinator inputBlockCoordinator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(urlLauncher);
        ArgumentNullException.ThrowIfNull(applicationResolver);
        ArgumentNullException.ThrowIfNull(applicationLauncher);
        ArgumentNullException.ThrowIfNull(inputBlockCoordinator);

        if (request is null)
        {
            return SessionCommandResponse.Error(
                string.Empty,
                SessionCommandErrorCodes.SessionChannelMalformedRequest);
        }

        var requestId = request.RequestId ?? string.Empty;

        if (request.ProtocolVersion != SessionCommandProtocol.ProtocolVersion)
        {
            return SessionCommandResponse.Error(
                requestId,
                SessionCommandErrorCodes.SessionChannelProtocolMismatch);
        }

        if (!Guid.TryParse(requestId, out _))
        {
            return SessionCommandResponse.Error(
                requestId,
                SessionCommandErrorCodes.SessionChannelMalformedRequest);
        }

        switch (request.CommandType)
        {
            case SessionCommandTypes.ChannelPing:
                return SessionCommandResponse.Success(requestId);
            case SessionCommandTypes.OpenUrl:
                return await HandleOpenUrlAsync(requestId, request.OpenUrl, urlLauncher, cancellationToken)
                    .ConfigureAwait(false);
            case SessionCommandTypes.OpenApplication:
                return await HandleOpenApplicationAsync(
                    requestId,
                    request.OpenApplication,
                    applicationResolver,
                    applicationLauncher,
                    cancellationToken).ConfigureAwait(false);
            case SessionCommandTypes.LockInput:
                return await HandleInputBlockAsync(requestId, inputBlockCoordinator.LockAsync, cancellationToken)
                    .ConfigureAwait(false);
            case SessionCommandTypes.UnlockInput:
                return await HandleInputBlockAsync(requestId, inputBlockCoordinator.UnlockAsync, cancellationToken)
                    .ConfigureAwait(false);
            default:
                return SessionCommandResponse.Error(
                    requestId,
                    SessionCommandErrorCodes.SessionCommandNotSupported);
        }
    }

    public static async Task<SessionCommandResponse> HandleAsync(
        SessionCommandRequest? request,
        IUrlLauncher urlLauncher,
        ISessionApplicationResolver applicationResolver,
        IWindowsApplicationLauncher applicationLauncher,
        CancellationToken cancellationToken)
    {
        return await HandleAsync(
            request,
            urlLauncher,
            applicationResolver,
            applicationLauncher,
            new WindowsInputBlockCoordinator(new UnavailableWindowsInputBlockApi()),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SessionCommandResponse> HandleInputBlockAsync(
        string requestId,
        Func<CancellationToken, Task<InputBlockResult>> command,
        CancellationToken cancellationToken)
    {
        var result = await command(cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? SessionCommandResponse.Success(requestId)
            : SessionCommandResponse.Error(
                requestId,
                result.ErrorCode ?? SessionCommandErrorCodes.SessionChannelInvalidResponse);
    }

    private static async Task<SessionCommandResponse> HandleOpenApplicationAsync(
        string requestId,
        SessionOpenApplicationCommand? command,
        ISessionApplicationResolver applicationResolver,
        IWindowsApplicationLauncher applicationLauncher,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            return SessionCommandResponse.Error(
                requestId,
                SessionCommandErrorCodes.SessionChannelMalformedRequest);
        }

        var idValidation = ApplicationBindingValidator.ValidateApplicationId(command.ApplicationId);
        if (!idValidation.IsValid)
        {
            return SessionCommandResponse.Error(
                requestId,
                SessionCommandErrorCodes.ApplicationBindingInvalid);
        }

        var resolution = await applicationResolver.ResolveAsync(
            idValidation.NormalizedValue!,
            cancellationToken).ConfigureAwait(false);
        if (!resolution.Succeeded || string.IsNullOrWhiteSpace(resolution.ExecutablePath))
        {
            return SessionCommandResponse.Error(
                requestId,
                resolution.ErrorCode ?? SessionCommandErrorCodes.ApplicationBindingInvalid,
                resolution.Message);
        }

        var launch = await applicationLauncher.LaunchAsync(
            resolution.ExecutablePath,
            cancellationToken).ConfigureAwait(false);
        return launch.Succeeded
            ? SessionCommandResponse.Success(requestId)
            : SessionCommandResponse.Error(
                requestId,
                SessionCommandErrorCodes.ApplicationLaunchFailed,
                launch.Message);
    }

    private static async Task<SessionCommandResponse> HandleOpenUrlAsync(
        string requestId,
        SessionOpenUrlCommand? command,
        IUrlLauncher urlLauncher,
        CancellationToken cancellationToken)
    {
        if (command is null || !Guid.TryParse(command.OperationId, out _))
        {
            return SessionCommandResponse.Error(
                requestId,
                SessionCommandErrorCodes.SessionChannelMalformedRequest);
        }

        var validation = new OpenUrlSafetyPolicy().Validate(command.Url);
        if (!validation.IsValid)
        {
            return SessionCommandResponse.Error(
                requestId,
                SessionCommandErrorCodes.InvalidUrl);
        }

        var result = await urlLauncher.LaunchAsync(command.Url, cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded
            ? SessionCommandResponse.Success(requestId)
            : SessionCommandResponse.Error(
                requestId,
                SessionCommandErrorCodes.UrlLaunchFailed,
                result.Message);
    }

    private static async Task WriteResponseAsync(
        Stream pipe,
        SessionCommandResponse response,
        CancellationToken cancellationToken)
    {
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        await SessionCommandFraming.WriteJsonAsync(pipe, responseJson, cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class UnavailableSessionCommandCallerVerifier : ISessionCommandCallerVerifier
{
    public bool IsAuthorized(PipeStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return false;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSessionCommandCallerVerifier : ISessionCommandCallerVerifier
{
    public bool IsAuthorized(PipeStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (pipe is not NamedPipeServerStream serverStream)
        {
            return false;
        }

        try
        {
            string? windowsSid = null;

            serverStream.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                windowsSid = identity.User?.Value;
            });

            return string.Equals(
                windowsSid,
                SessionCommandProtocol.LocalSystemSid,
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or SystemException)
        {
            return false;
        }
    }
}

public sealed class UnsupportedSessionCommandPipeStreamFactory : ISessionCommandPipeStreamFactory
{
    public NamedPipeServerStream CreateServerStream(string pipeName)
    {
        throw new PlatformNotSupportedException("Galtek Classroom Session Command uses Windows Named Pipes.");
    }
}

[SupportedOSPlatform("windows")]
public sealed class SessionCommandPipeStreamFactory : ISessionCommandPipeStreamFactory
{
    public static PipeOptions ProductivePipeOptions { get; } = PipeOptions.Asynchronous;

    public NamedPipeServerStream CreateServerStream(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            ProductivePipeOptions,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: CreatePipeSecurity());
    }

    public static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
            AccessControlType.Allow));

        return security;
    }
}
