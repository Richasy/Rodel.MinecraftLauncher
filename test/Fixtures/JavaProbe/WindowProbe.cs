using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

/// <summary>A real hidden-then-visible Win32 window, not a JVM or evidence of Minecraft readiness.</summary>
internal static class WindowProbe
{
    internal static unsafe int Run(string home)
    {
        var window = PInvoke.CreateWindowEx(default, "STATIC", "Rodel synthetic game window", WINDOW_STYLE.WS_OVERLAPPEDWINDOW,
            32, 32, 320, 180, default, null, null, null);
        if (window.Value == null) throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            File.WriteAllText(Path.Combine(home, "window-created"), ((nint)window.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
            var deadline = DateTime.UtcNow.AddSeconds(60);
            var shown = false;
            var closeRequests = 0;
            while (PInvoke.IsWindow(window) && DateTime.UtcNow < deadline)
            {
                if (!shown && File.Exists(Path.Combine(home, "show-window")))
                {
                    _ = PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
                    shown = true;
                }

                if (File.Exists(Path.Combine(home, "exit-before-window"))) return 9;
                while (PInvoke.PeekMessage(out var message, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
                {
                    if (message.message == PInvoke.WM_SYSCOMMAND && (message.wParam.Value & 0xfff0u) == PInvoke.SC_CLOSE)
                    {
                        File.WriteAllText(Path.Combine(home, "close-observed"), (++closeRequests).ToString(System.Globalization.CultureInfo.InvariantCulture));
                        if (File.Exists(Path.Combine(home, "hold-close"))) continue;
                    }

                    _ = PInvoke.DispatchMessage(in message);
                }

                Thread.Sleep(10);
            }

            if (PInvoke.IsWindow(window)) return 10;
            File.WriteAllText(Path.Combine(home, "normal-window-exit"), "completed");
            return 0;
        }
        finally { if (PInvoke.IsWindow(window)) _ = PInvoke.DestroyWindow(window); }
    }
}