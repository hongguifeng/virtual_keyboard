using System.Runtime.InteropServices;

namespace VirtualKeyboard.TestHost;

/// <summary>TestHost 专用 Win32 支持：向控件句柄发送同步 WM_KEYDOWN。</summary>
internal static class Win32Test
{
    internal const int WmKeydown = 0x0100;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    internal static void SendKeyDown(IntPtr controlHandle, System.Windows.Forms.Keys key)
    {
        // 首次按下：repeat count=1；bit 30（previous key state）和 bit 31（transition）均为 0。
        _ = SendMessage(controlHandle, WmKeydown, (IntPtr)key, (IntPtr)1);
    }
}
