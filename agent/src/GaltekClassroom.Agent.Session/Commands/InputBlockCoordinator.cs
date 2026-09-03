using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Commands;

public interface IWindowsInputBlockApi
{
    bool BlockInput(bool block);
}

public sealed class InputBlockResult
{
    private InputBlockResult(bool succeeded, string? errorCode)
    {
        Succeeded = succeeded;
        ErrorCode = errorCode;
    }

    public bool Succeeded { get; }

    public string? ErrorCode { get; }

    public static InputBlockResult Success()
    {
        return new InputBlockResult(true, null);
    }

    public static InputBlockResult LockFailed()
    {
        return new InputBlockResult(false, SessionCommandErrorCodes.InputLockFailed);
    }

    public static InputBlockResult UnlockFailed()
    {
        return new InputBlockResult(false, SessionCommandErrorCodes.InputUnlockFailed);
    }
}

public interface IInputBlockCoordinator
{
    Task<InputBlockResult> LockAsync(CancellationToken cancellationToken);

    Task<InputBlockResult> UnlockAsync(CancellationToken cancellationToken);

    Task<InputBlockResult> CleanupAsync(TimeSpan timeout);
}

public sealed class WindowsInputBlockCoordinator : IInputBlockCoordinator
{
    private readonly IWindowsInputBlockApi _inputBlockApi;
    private readonly object _sync = new();
    private InputBlockWorker? _worker;

    public WindowsInputBlockCoordinator(IWindowsInputBlockApi inputBlockApi)
    {
        _inputBlockApi = inputBlockApi;
    }

    public Task<InputBlockResult> LockAsync(CancellationToken cancellationToken)
    {
        return EnqueueAsync(InputBlockCommandKind.Lock, cancellationToken);
    }

    public Task<InputBlockResult> UnlockAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_worker is null)
            {
                return Task.FromResult(InputBlockResult.Success());
            }

            if (_worker.TryEnqueue(InputBlockCommandKind.Unlock, out var task))
            {
                return AwaitWorkAsync(task, cancellationToken);
            }

            _worker = null;
            return Task.FromResult(InputBlockResult.Success());
        }
    }

    public async Task<InputBlockResult> CleanupAsync(TimeSpan timeout)
    {
        Task<InputBlockResult> cleanupTask;
        lock (_sync)
        {
            if (_worker is null)
            {
                return InputBlockResult.Success();
            }

            if (!_worker.TryEnqueue(InputBlockCommandKind.Cleanup, out cleanupTask))
            {
                _worker = null;
                return InputBlockResult.Success();
            }
        }

        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            return await AwaitWorkAsync(cleanupTask, cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return InputBlockResult.UnlockFailed();
        }
    }

    private Task<InputBlockResult> EnqueueAsync(
        InputBlockCommandKind commandKind,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_sync)
            {
                var worker = _worker ?? CreateWorker();
                if (worker.TryEnqueue(commandKind, out var task))
                {
                    return AwaitWorkAsync(task, cancellationToken);
                }

                if (ReferenceEquals(_worker, worker))
                {
                    _worker = null;
                }
            }
        }
    }

    private InputBlockWorker CreateWorker()
    {
        var worker = new InputBlockWorker(_inputBlockApi, OnWorkerStopped);
        _worker = worker;
        worker.Start();
        return worker;
    }

    private void OnWorkerStopped(InputBlockWorker worker)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_worker, worker))
            {
                _worker = null;
            }
        }
    }

    private static async Task<InputBlockResult> AwaitWorkAsync(
        Task<InputBlockResult> task,
        CancellationToken cancellationToken)
    {
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private enum InputBlockCommandKind
    {
        Lock,
        Unlock,
        Cleanup
    }

    private sealed class InputBlockWorker
    {
        private readonly IWindowsInputBlockApi _inputBlockApi;
        private readonly Action<InputBlockWorker> _onStopped;
        private readonly BlockingCollection<InputBlockWorkItem> _queue = new();
        private readonly Thread _thread;
        private volatile bool _accepting = true;
        private bool _locked;

        public InputBlockWorker(
            IWindowsInputBlockApi inputBlockApi,
            Action<InputBlockWorker> onStopped)
        {
            _inputBlockApi = inputBlockApi;
            _onStopped = onStopped;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "Galtek Input Block"
            };
        }

        public void Start()
        {
            _thread.Start();
        }

        public bool TryEnqueue(
            InputBlockCommandKind commandKind,
            out Task<InputBlockResult> task)
        {
            var workItem = new InputBlockWorkItem(commandKind);
            try
            {
                if (!_accepting)
                {
                    task = Task.FromResult(InputBlockResult.UnlockFailed());
                    return false;
                }

                if (commandKind is InputBlockCommandKind.Unlock or InputBlockCommandKind.Cleanup)
                {
                    _accepting = false;
                }

                _queue.Add(workItem);
                task = workItem.Task;
                return true;
            }
            catch (InvalidOperationException)
            {
                task = Task.FromResult(InputBlockResult.UnlockFailed());
                return false;
            }
        }

        private void Run()
        {
            try
            {
                foreach (var workItem in _queue.GetConsumingEnumerable())
                {
                    var shouldStop = Process(workItem);
                    if (shouldStop)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _accepting = false;
                _queue.CompleteAdding();
                while (_queue.TryTake(out var pendingWorkItem))
                {
                    pendingWorkItem.Complete(InputBlockResult.UnlockFailed());
                }

                _queue.Dispose();
                _onStopped(this);
            }
        }

        private bool Process(InputBlockWorkItem workItem)
        {
            switch (workItem.CommandKind)
            {
                case InputBlockCommandKind.Lock:
                    return ProcessLock(workItem);
                case InputBlockCommandKind.Unlock:
                    return ProcessUnlock(workItem);
                case InputBlockCommandKind.Cleanup:
                    return ProcessCleanup(workItem);
                default:
                    workItem.Complete(InputBlockResult.UnlockFailed());
                    return true;
            }
        }

        private bool ProcessLock(InputBlockWorkItem workItem)
        {
            var accepted = _inputBlockApi.BlockInput(block: true);
            if (accepted || _locked)
            {
                _locked = true;
                workItem.Complete(InputBlockResult.Success());
                return false;
            }

            workItem.Complete(InputBlockResult.LockFailed());
            _accepting = false;
            return true;
        }

        private bool ProcessUnlock(InputBlockWorkItem workItem)
        {
            var accepted = _inputBlockApi.BlockInput(block: false);
            _locked = false;
            workItem.Complete(accepted
                ? InputBlockResult.Success()
                : InputBlockResult.UnlockFailed());
            return true;
        }

        private bool ProcessCleanup(InputBlockWorkItem workItem)
        {
            if (!_locked)
            {
                workItem.Complete(InputBlockResult.Success());
                return true;
            }

            var accepted = _inputBlockApi.BlockInput(block: false);
            _locked = false;
            workItem.Complete(accepted
                ? InputBlockResult.Success()
                : InputBlockResult.UnlockFailed());
            return true;
        }
    }

    private sealed class InputBlockWorkItem
    {
        private readonly TaskCompletionSource<InputBlockResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public InputBlockWorkItem(InputBlockCommandKind commandKind)
        {
            CommandKind = commandKind;
        }

        public InputBlockCommandKind CommandKind { get; }

        public Task<InputBlockResult> Task => _completion.Task;

        public void Complete(InputBlockResult result)
        {
            _completion.TrySetResult(result);
        }
    }
}

public sealed class UnavailableWindowsInputBlockApi : IWindowsInputBlockApi
{
    public bool BlockInput(bool block)
    {
        return false;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsInputBlockApi : IWindowsInputBlockApi
{
    public bool BlockInput(bool block)
    {
        return NativeMethods.BlockInput(block);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BlockInput([MarshalAs(UnmanagedType.Bool)] bool block);
    }
}
