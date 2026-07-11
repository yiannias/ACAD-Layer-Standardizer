using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AcLayerStandardizer.UI;

// Shared window-chrome theming so every dialog coordinates with the dark
// UI (chris, 2026-07-11) -- previously only NodeGraphWindow darkened its
// title bar, leaving WelcomeDialog/PreviewDialog with light chrome.
public static class WindowTheming
{
    // Call from the window's SourceInitialized (the HWND must exist):
    //   SourceInitialized += (_, _) => WindowTheming.EnableDarkTitleBar(this);
    public static void EnableDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
