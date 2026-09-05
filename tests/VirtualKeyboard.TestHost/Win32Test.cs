using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VirtualKeyboard.TestHost;

/// <summary>
/// Win32 测试支持：向指定控件句柄发送真实 WM_KEYDOWN，
/// 使按键走 WinForms 真实消息管线（WndProc → OnKeyDown → KeyDown 事件）。
/// 仅测试进程使用（NFR-COMP-001），不属于产品架构约束范围（产品 P/Invoke 仍集中于 Windows 适配层）。
/// </summary>
internal static class Win32Test
{
    /// <summary>WM_KEYDOWN 消息编号。</summary>
    internal const int WmKeydown = 0x0100;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// 发送一次「按键按下」WM_KEYDOWN（lTransition=1、无扩展键、扫描码 0）。
    /// 同步调用：接收控件的 WndProc/KeyDown 事件在本调用返回前执行完毕。
    /// </summary>
    internal static void SendKeyDown(IntPtr controlHandle, Keys key)
    {
        long lParam = (1L << 30) | 1; // bit30=1 表示按下；低位=1 表示前次按键状态
        SendMessage(controlHandle, WmKeydown, (IntPtr)key, new IntPtr(lParam));
    }
}
