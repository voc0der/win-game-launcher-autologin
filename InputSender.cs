using System.Runtime.InteropServices;

namespace UbisoftAutoLogin;

internal static class InputSender
{
    public static bool Click(IntPtr targetRootHwnd, int x, int y, AppLogger logger)
    {
        if (!UbisoftWindowDetector.IsForegroundWithinTarget(targetRootHwnd))
        {
            logger.Warn("Click aborted because foreground was not the target Ubisoft window.");
            return false;
        }

        if (!NativeMethods.SetCursorPos(x, y))
        {
            logger.Warn($"SetCursorPos failed with Win32 error {Marshal.GetLastWin32Error()}.");
            return false;
        }

        var inputs = new[]
        {
            MouseInput(NativeMethods.MOUSEEVENTF_LEFTDOWN),
            MouseInput(NativeMethods.MOUSEEVENTF_LEFTUP)
        };

        if (!SendAll(inputs, logger, "mouse click"))
        {
            return false;
        }

        Thread.Sleep(100);
        if (!UbisoftWindowDetector.IsForegroundWithinTarget(targetRootHwnd))
        {
            logger.Warn("Click completed, but foreground moved away from Ubisoft.");
            return false;
        }

        return true;
    }

    public static bool SendTextIfForeground(IntPtr targetRootHwnd, string text, AppLogger logger)
    {
        foreach (var ch in text)
        {
            if (!UbisoftWindowDetector.IsForegroundWithinTarget(targetRootHwnd))
            {
                logger.Warn("Typing aborted because foreground was not the target Ubisoft window.");
                return false;
            }

            var inputs = new[]
            {
                UnicodeKey(ch, keyUp: false),
                UnicodeKey(ch, keyUp: true)
            };

            if (!SendAll(inputs, logger, "text input"))
            {
                return false;
            }
        }

        return true;
    }

    public static bool SendEnterIfForeground(IntPtr targetRootHwnd, AppLogger logger)
    {
        if (!UbisoftWindowDetector.IsForegroundWithinTarget(targetRootHwnd))
        {
            logger.Warn("Enter aborted because foreground was not the target Ubisoft window.");
            return false;
        }

        var inputs = new[]
        {
            VirtualKey(NativeMethods.VK_RETURN, keyUp: false),
            VirtualKey(NativeMethods.VK_RETURN, keyUp: true)
        };

        return SendAll(inputs, logger, "Enter key");
    }

    private static bool SendAll(NativeMethods.INPUT[] inputs, AppLogger logger, string description)
    {
        // SendInput is only reached after foreground verification by the caller.
        // This app never uses the clipboard, so the password is not exposed there.
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            logger.Warn($"SendInput failed for {description}; sent {sent}/{inputs.Length}, Win32 error {Marshal.GetLastWin32Error()}.");
            return false;
        }

        return true;
    }

    private static NativeMethods.INPUT MouseInput(uint flags)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dwFlags = flags
                }
            }
        };
    }

    private static NativeMethods.INPUT UnicodeKey(char character, bool keyUp)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wScan = character,
                    dwFlags = NativeMethods.KEYEVENTF_UNICODE | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0)
                }
            }
        };
    }

    private static NativeMethods.INPUT VirtualKey(ushort virtualKey, bool keyUp)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0
                }
            }
        };
    }
}
