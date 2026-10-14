namespace Ticktower.Core;

/// <summary>
/// Named-mutex single instance guard. A second launch calls <see cref="SignalExisting"/>, which wakes the
/// first instance's listener so it can focus its window.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly bool _owned;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _listener;

    public SingleInstance(string name)
    {
        _mutex = new Mutex(true, $"Local\\{name}.mutex", out _owned);
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\{name}.signal");
    }

    /// <summary>True when this process is the first instance.</summary>
    public bool IsPrimary => _owned;

    /// <summary>Asks the running instance to focus itself. Call from a secondary instance, then exit.</summary>
    public void SignalExisting() => _signal.Set();

    /// <summary>Primary only. Invokes <paramref name="onSignal"/> on a background thread for each second launch.</summary>
    public void Listen(Action onSignal)
    {
        if (!_owned) throw new InvalidOperationException("Only the primary instance listens.");
        _listener = new Thread(() =>
        {
            var handles = new[] { _signal, _cts.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0) onSignal();
        }) { IsBackground = true, Name = "Ticktower single-instance listener" };
        _listener.Start();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Join(1000);
        if (_owned) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _signal.Dispose();
        _cts.Dispose();
    }
}
