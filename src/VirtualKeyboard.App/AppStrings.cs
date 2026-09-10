using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.App;

internal sealed class AppStrings
{
    private AppStrings(UiLanguage language) => Language = language;

    internal static AppStrings For(UiLanguage language) => new(language);
    internal UiLanguage Language { get; }
    private bool Chinese => Language == UiLanguage.SimplifiedChinese;

    internal string SettingsTitle => Chinese ? "Virtual Keyboard 设置" : "Virtual Keyboard Settings";
    internal string LanguageLabel => Chinese ? "界面语言" : "Language";
    internal string Enabled => Chinese ? "启用键盘" : "Enable keyboard";
    internal string AutoShow => Chinese ? "自动显示" : "Show automatically";
    internal string ShowLauncherButton => Chinese ? "先在光标附近显示悬浮按钮，点击后展开键盘" : "Show a floating button before opening the keyboard";
    internal string OpenKeyboard => Chinese ? "展开虚拟键盘" : "Open virtual keyboard";
    internal string AutoHide => Chinese ? "自动隐藏" : "Hide automatically";
    internal string Width => Chinese ? "宽度 (DIP)" : "Width (DIP)";
    internal string Height => Chinese ? "高度 (DIP)" : "Height (DIP)";
    internal string Margin => Chinese ? "边距 (DIP)" : "Margin (DIP)";
    internal string Transparency => Chinese ? "透明程度" : "Transparency";
    internal string LayoutId => Chinese ? "布局 ID" : "Layout ID";
    internal string CustomKeys => Chinese ? "自定义按键（最多 12 个）" : "Custom keys (up to 12)";
    internal string Add => Chinese ? "添加" : "Add";
    internal string Delete => Chinese ? "删除" : "Delete";
    internal string KeyName => Chinese ? "按键名称" : "Key label";
    internal string OnPress => Chinese ? "按下后" : "On press";
    internal string EnterText => Chinese ? "输入文字" : "Enter text";
    internal string RecordShortcut => Chinese ? "录制按键或组合键" : "Record key or shortcut";
    internal string TextContent => Chinese ? "文字内容" : "Text";
    internal string Recorded => Chinese ? "已录制" : "Recorded";
    internal string NotRecorded => Chinese ? "尚未录制" : "Not recorded";
    internal string StartRecording => Chinese ? "开始录制" : "Start recording";
    internal string PressShortcut => Chinese ? "请按下组合键…" : "Press the shortcut…";
    internal string WaitingRelease => Chinese ? "等待按键，全部松开后完成…" : "Waiting for keys; release all keys to finish…";
    internal string RecordingHelp => Chinese ? "录制时按下全部按键并全部松开，例如 Win+Tab 或 Ctrl+Shift+S。" : "Press every key in the shortcut, then release them all, for example Win+Tab or Ctrl+Shift+S.";
    internal string PositionRetention => Chinese ? "拖动位置保留" : "Keep dragged position";
    internal string Diagnostics => Chinese ? "启用详细诊断" : "Enable detailed diagnostics";
    internal string Save => Chinese ? "保存" : "Save";
    internal string Cancel => Chinese ? "取消" : "Cancel";
    internal string CurrentField => Chinese ? "当前输入框" : "Current input field";
    internal string CurrentFieldDescription => Chinese ? "拖动键盘后，仅为当前输入框保留位置；切换到其他输入框时恢复自动定位。" : "Keep the dragged position for the current input field only; automatic placement resumes for another field.";
    internal string Persistent => Chinese ? "持续保留" : "Keep until changed";
    internal string PersistentDescription => Chinese ? "拖动键盘后继续使用手动位置，不因切换输入框而恢复自动定位。" : "Continue using the manual position when switching between input fields.";
    internal string AutoStart => Chinese ? "开机启动" : "Start with Windows";
    internal string AutoStartDescription => Chinese ? "登录 Windows 时自动启动虚拟键盘" : "Automatically start the virtual keyboard when you sign in to Windows";
    internal string AutoStartApplyFailed => Chinese ? "未能应用开机启动设置" : "Could not apply the auto-start setting";
    internal string InvalidSettings => Chinese ? "设置无效，请检查自定义按键或数值范围。" : "Invalid settings. Check custom keys and numeric ranges.";
    internal string SaveFailed => Chinese ? "设置无法保存，已保留当前内存配置。" : "Settings could not be saved; the current in-memory configuration was kept.";
    internal string InvalidNumber => Chinese ? "设置无效，请输入数字。" : "Invalid settings. Enter numeric values.";
    internal string NewCustomKey => Chinese ? "自定义" : "Custom";
    internal string RecordingFailed => Chinese ? "无法启动键盘录制，请重试。" : "Could not start keyboard recording. Try again.";
    internal string TooManyChordKeys => Chinese ? "组合键最多支持 8 个不同按键，请重新录制。" : "A shortcut supports up to 8 distinct keys. Record it again.";
    internal string Settings => Chinese ? "设置" : "Settings";
    internal string PauseKeyboard => Chinese ? "暂停键盘" : "Pause keyboard";
    internal string EnableKeyboard => Chinese ? "启用键盘" : "Enable keyboard";
    internal string ShowKeyboard => Chinese ? "显示当前键盘" : "Show keyboard";
    internal string ReloadLayouts => Chinese ? "重新加载布局" : "Reload layouts";
    internal string RestartFocusDetection => Chinese ? "恢复自动检测" : "Resume automatic detection";
    internal string FocusDetectionStopped => Chinese ? "自动检测已停止" : "Automatic detection stopped";
    internal string FocusDetectionStoppedHint => Chinese ? "请在托盘菜单选择“恢复自动检测”；若该项不可用，请重新启动应用。" : "Choose Resume automatic detection in the tray menu. If unavailable, restart the application.";
    internal string Exit => Chinese ? "退出" : "Exit";
    internal string PasswordUnavailable => Chinese ? "密码输入中此按键不可用" : "This key is unavailable for password input";
    internal string SelectEditable => Chinese ? "请先点击可编辑输入框" : "Select an editable input field first";
    internal string QueueStopped => Chinese ? "输入队列已停止或目标已变化" : "Input stopped or the target changed";
    internal string LayoutMissing => Chinese ? "未找到内置键盘布局" : "Built-in keyboard layout not found";
    internal string LayoutFailed(string path, string code) => Chinese ? $"布局加载失败：{path} · {code}" : $"Layout failed to load: {path} · {code}";
    internal string InputFailure(InputFailureFeedback feedback) => Chinese ? feedback.Message : feedback.Kind switch
    {
        InputFailureKind.TargetChanged => "The target changed; input was cancelled",
        InputFailureKind.PermissionBoundary => "The target has higher permissions; input is unavailable",
        InputFailureKind.PossiblePermissionBoundary => "A target permission boundary may be preventing input",
        InputFailureKind.PartialDelivery => "Input was only partially sent; it was not retried to avoid duplication",
        InputFailureKind.NativeUnavailable => "System input services are unavailable",
        InputFailureKind.InvalidRequest => "The input action is invalid",
        InputFailureKind.Cancelled => "Input was cancelled",
        InputFailureKind.SafetyFaulted => "The input engine stopped safely; restart the application",
        _ => "Input could not be sent",
    };
}
