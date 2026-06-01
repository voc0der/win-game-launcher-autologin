using System.Diagnostics;
using System.Text;

namespace UbisoftAutoLogin;

internal sealed record UbisoftWindow(IntPtr RootHwnd, uint ProcessId, string ProcessName, NativeMethods.RECT Rect);

internal static class UbisoftWindowDetector
{
    private const int MinimumWidth = 500;
    private const int MinimumHeight = 350;

    private static readonly HashSet<string> TargetProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "upc",
        "upc.exe",
        "UbisoftConnect",
        "UbisoftConnect.exe",
        "UbisoftGameLauncher",
        "UbisoftGameLauncher.exe"
    };

    public static bool TryGetCandidate(IntPtr hwnd, out UbisoftWindow window)
    {
        window = default!;

        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
        {
            root = hwnd;
        }

        if (!NativeMethods.IsWindow(root) ||
            !NativeMethods.IsWindowVisible(root) ||
            NativeMethods.IsIconic(root) ||
            !NativeMethods.GetWindowRect(root, out var rect) ||
            rect.Width < MinimumWidth ||
            rect.Height < MinimumHeight)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(root, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return false;
        }

        var processName = GetProcessName(processId);
        if (processName is null || !TargetProcessNames.Contains(processName))
        {
            return false;
        }

        window = new UbisoftWindow(root, processId, processName, rect);
        return true;
    }

    public static bool IsForegroundWithinTarget(IntPtr targetRootHwnd)
    {
        if (!TryGetCandidate(targetRootHwnd, out _))
        {
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        if (foreground == targetRootHwnd)
        {
            return true;
        }

        var foregroundRoot = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        if (foregroundRoot == targetRootHwnd)
        {
            return true;
        }

        var foregroundRootOwner = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOTOWNER);
        if (foregroundRootOwner == targetRootHwnd)
        {
            return true;
        }

        return TryGetCandidate(foreground, out var foregroundWindow) && foregroundWindow.RootHwnd == targetRootHwnd;
    }

    public static string GetWindowTitle(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        var length = NativeMethods.GetWindowText(hwnd, builder, builder.Capacity);
        return length > 0 ? builder.ToString() : string.Empty;
    }

    private static string? GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
