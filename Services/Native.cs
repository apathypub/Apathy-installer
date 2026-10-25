using System.Runtime.InteropServices;

namespace Apathy.Services;

internal static class Native
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Keeps Windows 11 from rounding the corners of the borderless window.</summary>
    public static void DisableRoundedCorners(IntPtr hwnd)
    {
        int preference = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    private static readonly Guid FolderDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags,
        IntPtr token, out string path);

    /// <summary>The user's Downloads folder, also when it was moved to another drive.</summary>
    public static string DownloadsFolder() =>
        SHGetKnownFolderPath(FolderDownloads, 0, IntPtr.Zero, out string path) == 0
            ? path
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}
