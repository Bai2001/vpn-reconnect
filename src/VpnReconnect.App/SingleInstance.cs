namespace VpnReconnect.App;

public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\VpnReconnect";
    private const string ShowEventName = @"Local\VpnReconnect.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly Thread _watcher;
    private volatile bool _stop;
    private bool _disposed;

    public event EventHandler? ShowRequested;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
        _watcher = new Thread(Watch)
        {
            IsBackground = true,
            Name = "VpnReconnect.Show",
        };
        _watcher.Start();
    }

    /// <param name="waitForRelease">
    /// 等待旧实例释放单实例锁的时长；更新后重启时用，正常启动不等待。
    /// </param>
    public static bool TryBecomePrimary(out SingleInstance? gate, TimeSpan? waitForRelease = null)
    {
        var mutex = new Mutex(false, MutexName);
        var deadline = DateTime.UtcNow + (waitForRelease ?? TimeSpan.Zero);
        var acquired = false;
        while (true)
        {
            try
            {
                acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (acquired)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            Thread.Sleep(200);
        }

        if (!acquired)
        {
            mutex.Dispose();
            gate = null;
            return false;
        }

        var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        gate = new SingleInstance(mutex, show);
        return true;
    }

    public static void RequestShow()
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop = true;
        try
        {
            _showEvent.Set();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _mutex.Dispose();
        _showEvent.Dispose();
    }

    private void Watch()
    {
        while (!_stop)
        {
            var signaled = false;
            try
            {
                signaled = _showEvent.WaitOne(500);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (signaled && !_stop)
            {
                ShowRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
