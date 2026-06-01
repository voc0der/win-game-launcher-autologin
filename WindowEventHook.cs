using System.Runtime.InteropServices;

namespace UbisoftAutoLogin;

internal sealed class WindowEventHook : IDisposable
{
    private static readonly NativeMethods.WinEventProc HookProc = OnWinEvent;
    private static WindowEventHook? _instance;

    private readonly Control _dispatcher;
    private readonly AppLogger _logger;
    private readonly List<IntPtr> _hooks = new();
    private bool _disposed;

    public event EventHandler<WindowEventArgs>? WindowEventReceived;

    public WindowEventHook(Control dispatcher, AppLogger logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public bool Start()
    {
        if (_disposed)
        {
            return false;
        }

        _instance = this;

        // Register discrete passive hooks for all processes/threads. WINEVENT_SKIPOWNPROCESS
        // prevents this tray app's hidden WinForms windows from producing callbacks.
        AddHook(NativeMethods.EVENT_OBJECT_CREATE);
        AddHook(NativeMethods.EVENT_OBJECT_SHOW);
        AddHook(NativeMethods.EVENT_SYSTEM_FOREGROUND);

        if (_hooks.Count == 0)
        {
            _logger.Warn("No WinEvent hooks could be installed.");
            return false;
        }

        _logger.Info($"Installed {_hooks.Count} WinEvent hooks.");
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();

        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private void AddHook(uint eventId)
    {
        var hook = NativeMethods.SetWinEventHook(
            eventId,
            eventId,
            IntPtr.Zero,
            HookProc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        if (hook == IntPtr.Zero)
        {
            _logger.Warn($"SetWinEventHook failed for event 0x{eventId:X}; Win32 error {Marshal.GetLastWin32Error()}.");
            return;
        }

        _hooks.Add(hook);
    }

    private static void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        // The delegate is rooted in the static HookProc field. Without that, the GC
        // could collect the callback while user32 still holds the function pointer.
        var instance = _instance;
        if (instance is null || instance._disposed || hwnd == IntPtr.Zero)
        {
            return;
        }

        if ((eventType == NativeMethods.EVENT_OBJECT_CREATE || eventType == NativeMethods.EVENT_OBJECT_SHOW) &&
            idObject != NativeMethods.OBJID_WINDOW)
        {
            return;
        }

        try
        {
            if (instance._dispatcher.IsDisposed)
            {
                return;
            }

            instance._dispatcher.BeginInvoke((Action)(() =>
            {
                if (!instance._disposed)
                {
                    instance.WindowEventReceived?.Invoke(
                        instance,
                        new WindowEventArgs(eventType, hwnd, idObject, idChild, dwEventThread, dwmsEventTime));
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The message loop is closing; ignore late callbacks.
        }
    }
}

internal sealed class WindowEventArgs : EventArgs
{
    public WindowEventArgs(uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        EventType = eventType;
        Hwnd = hwnd;
        IdObject = idObject;
        IdChild = idChild;
        EventThread = eventThread;
        EventTime = eventTime;
    }

    public uint EventType { get; }
    public IntPtr Hwnd { get; }
    public int IdObject { get; }
    public int IdChild { get; }
    public uint EventThread { get; }
    public uint EventTime { get; }
}
