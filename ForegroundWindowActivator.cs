using System.Diagnostics;

namespace UbisoftAutoLogin;

internal enum ForegroundActivationResult
{
    Activated,
    RetryableFailure,
    BlockedByOtherApplication
}

internal sealed class ForegroundWindowActivator
{
    private readonly AppLogger _logger;

    public ForegroundWindowActivator(AppLogger logger)
    {
        _logger = logger;
    }

    public ForegroundActivationResult TryActivateUbisoft(IntPtr targetRootHwnd)
    {
        if (UbisoftWindowDetector.IsForegroundWithinTarget(targetRootHwnd))
        {
            return ForegroundActivationResult.Activated;
        }

        var originalForeground = NativeMethods.GetForegroundWindow();
        var requestedNormally = NativeMethods.SetForegroundWindow(targetRootHwnd);
        if (WaitForTargetForeground(targetRootHwnd, 500))
        {
            _logger.Info($"Activated Ubisoft window normally. SetForegroundWindow returned {requestedNormally}.");
            return ForegroundActivationResult.Activated;
        }

        var blockingForeground = NativeMethods.GetForegroundWindow();
        if (!TryGetWindowOwner(blockingForeground, out var blockingRoot, out var processId, out var processName) ||
            !ForegroundOwnerPolicy.CanMinimizeForUbisoftLogin(processName))
        {
            _logger.Warn(
                $"Could not activate Ubisoft window. SetForegroundWindow returned {requestedNormally}; " +
                $"original foreground={DescribeWindow(originalForeground)}, current foreground={DescribeWindow(blockingForeground)}.");
            return ForegroundActivationResult.BlockedByOtherApplication;
        }

        _logger.Info(
            $"Playnite is blocking the Ubisoft login window; minimizing it. " +
            $"hwnd=0x{blockingRoot.ToInt64():X}, pid={processId}, process={processName}.");

        NativeMethods.ShowWindowAsync(blockingRoot, NativeMethods.SW_MINIMIZE);
        WaitForWindowToStopBlocking(blockingRoot, 750);

        var requestedAfterMinimize = NativeMethods.SetForegroundWindow(targetRootHwnd);
        if (WaitForTargetForeground(targetRootHwnd, 1000))
        {
            _logger.Info($"Activated Ubisoft window after minimizing Playnite. SetForegroundWindow returned {requestedAfterMinimize}.");
            return ForegroundActivationResult.Activated;
        }

        _logger.Warn(
            $"Could not activate Ubisoft after minimizing Playnite. SetForegroundWindow returned {requestedAfterMinimize}; " +
            $"current foreground={DescribeWindow(NativeMethods.GetForegroundWindow())}.");
        return ForegroundActivationResult.RetryableFailure;
    }

    private static bool WaitForTargetForeground(IntPtr targetRootHwnd, int timeoutMs)
    {
        return WaitUntil(
            () => UbisoftWindowDetector.IsForegroundWithinTarget(targetRootHwnd),
            timeoutMs);
    }

    private static void WaitForWindowToStopBlocking(IntPtr rootHwnd, int timeoutMs)
    {
        WaitUntil(
            () => NativeMethods.IsIconic(rootHwnd) || !IsWithinRoot(NativeMethods.GetForegroundWindow(), rootHwnd),
            timeoutMs);
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(50);
        }
        while (stopwatch.ElapsedMilliseconds < timeoutMs);

        return condition();
    }

    private static bool IsWithinRoot(IntPtr hwnd, IntPtr expectedRoot)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (hwnd == expectedRoot)
        {
            return true;
        }

        return NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) == expectedRoot;
    }

    private static bool TryGetWindowOwner(IntPtr hwnd, out IntPtr rootHwnd, out uint processId, out string processName)
    {
        rootHwnd = IntPtr.Zero;
        processId = 0;
        processName = string.Empty;

        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (rootHwnd == IntPtr.Zero)
        {
            rootHwnd = hwnd;
        }

        NativeMethods.GetWindowThreadProcessId(rootHwnd, out processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string DescribeWindow(IntPtr hwnd)
    {
        return TryGetWindowOwner(hwnd, out var rootHwnd, out var processId, out var processName)
            ? $"hwnd=0x{rootHwnd.ToInt64():X}, pid={processId}, process={processName}"
            : $"hwnd=0x{hwnd.ToInt64():X}, process=unknown";
    }
}
