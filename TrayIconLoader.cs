using System.Reflection;
using System.Runtime.InteropServices;

namespace UbisoftAutoLogin;

internal static class TrayIconLoader
{
    private const string ResourceName = "UbisoftAutoLogin.Assets.TrayIcon.png";

    public static Icon Load(AppLogger logger)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                logger.Warn($"Tray icon resource '{ResourceName}' was not found.");
                return (Icon)SystemIcons.Application.Clone();
            }

            using var source = new Bitmap(stream);
            using var scaled = new Bitmap(source, SystemInformation.SmallIconSize);

            // Bitmap.GetHicon returns an unmanaged HICON. Clone the managed Icon,
            // then destroy the native handle so the tray app does not leak it.
            var handle = scaled.GetHicon();
            try
            {
                using var icon = Icon.FromHandle(handle);
                return (Icon)icon.Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(handle);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or IOException)
        {
            logger.Warn($"Tray icon load failed: {ex.Message}");
            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
