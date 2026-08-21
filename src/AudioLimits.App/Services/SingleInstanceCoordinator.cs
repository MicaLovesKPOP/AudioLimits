using AudioLimits.Core.Services;

namespace AudioLimits.App.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string EventName = @"Local\AudioLimits.ActivatePrimary.v1";
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _cts = new();
    private Task? _waitTask;

    public bool IsPrimary { get; }
    public event EventHandler? ActivationRequested;

    public SingleInstanceCoordinator()
    {
        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            EventName,
            out var createdNew);

        IsPrimary = createdNew;
    }

    public void StartListening()
    {
        if (!IsPrimary || _waitTask is not null)
            return;

        _waitTask = Task.Run(WaitLoop);
    }

    public void ActivatePrimary()
    {
        try { _activationEvent.Set(); }
        catch (Exception ex) { AppLog.Error("Could not signal existing Audio Limits instance", ex); }
    }

    private void WaitLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                _activationEvent.WaitOne();
                if (_cts.IsCancellationRequested)
                    break;
                ActivationRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("Single-instance activation listener failed", ex);
                break;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsPrimary)
        {
            try { _activationEvent.Set(); } catch { }
            try { _waitTask?.Wait(500); } catch { }
        }

        _activationEvent.Dispose();
        _cts.Dispose();
    }
}
