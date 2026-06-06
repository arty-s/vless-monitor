using System.Runtime.InteropServices;

namespace VlessMonitor.UI;

/// <summary>Win32/DWM interop for Fluent window styling (dark titlebar, rounded corners, Mica).</summary>
internal static class Native
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // DWM attributes (Windows 10 2004+ / Windows 11)
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // Corner preferences
    private const int DWMWCP_ROUND = 2;

    // Backdrop types
    private const int DWMSBT_MAINWINDOW = 2; // Mica

    public const int WM_SETTINGCHANGE = 0x001A;

    public static void UseDarkTitleBar(IntPtr hwnd, bool dark)
    {
        int v = dark ? 1 : 0;
        try { DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int)); }
        catch { /* older OS */ }
    }

    public static void UseRoundedCorners(IntPtr hwnd)
    {
        int v = DWMWCP_ROUND;
        try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, sizeof(int)); }
        catch { /* Win10 ignores */ }
    }

    public static void TryEnableMica(IntPtr hwnd)
    {
        int v = DWMSBT_MAINWINDOW;
        try { DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref v, sizeof(int)); }
        catch { /* unsupported < Win11 22H2 */ }
    }

    /// <summary>True if the WM_SETTINGCHANGE notification is about the immersive color set (theme switch).</summary>
    public static bool IsImmersiveColorSetChange(Message m)
    {
        if (m.Msg != WM_SETTINGCHANGE || m.LParam == IntPtr.Zero) return false;
        try
        {
            var s = Marshal.PtrToStringAuto(m.LParam);
            return s is "ImmersiveColorSet" or "WindowsThemeElement";
        }
        catch { return false; }
    }
}
