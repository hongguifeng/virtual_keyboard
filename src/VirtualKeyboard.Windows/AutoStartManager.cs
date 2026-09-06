using Microsoft.Win32;
using VirtualKeyboard.Core.AutoStart;

namespace VirtualKeyboard.Windows;

/// <summary>
/// 当前用户级开机自启实现（FR-APP-004，设计文档 14.4）：
/// 写入/删除 <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> 下本应用命名的启动值。
/// 普通用户权限即可（HKCU 无需提权；不绕过 UIPI）。
/// 本类不写任何日志/诊断（NFR-PRI-001：注册表路径与进程路径属于本机信息，不得进入诊断）；
/// 失败只返回非零数字错误码。
/// </summary>
public sealed class AutoStartManager : IAutoStartManager
{
    /// <summary>启动值名称（只包含应用名，不含路径）。</summary>
    public const string RunValueName = "VirtualKeyboard";

    private static readonly string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly Func<IUserRunKeyStore> _storeFactory;
    private readonly Func<string?> _processPathProvider;

    /// <summary>生产构造：使用真实注册表存储。</summary>
    public AutoStartManager() : this(static () => new UserRunKeyStore(), () => Environment.ProcessPath)
    {
    }

    /// <summary>测试构造：允许注入自启项存储与进程路径提供器。</summary>
    internal AutoStartManager(Func<IUserRunKeyStore> storeFactory, Func<string?> processPathProvider)
    {
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        _processPathProvider = processPathProvider ?? throw new ArgumentNullException(nameof(processPathProvider));
    }

    /// <summary>
    /// 读取自启项真实状态：仅当 Run 值存在、非空且指向当前可执行文件路径时判定为已启用
    /// （值被外部改成其他路径时视为“未启用但外部占用”，读取本身仍然成功）。
    /// </summary>
    public bool TryGetEnabled(out bool enabled)
    {
        IUserRunKeyStore store = _storeFactory();
        if (!store.TryGetCommand(out string? command))
        {
            enabled = false;
            return false;
        }

        enabled = !string.IsNullOrWhiteSpace(command) &&
            string.Equals(command.Trim(), BuildCommand(_processPathProvider()), StringComparison.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>启用或禁用自启（幂等）。errorCode：1=进程路径不可用；2=注册表写入/删除失败。</summary>
    public bool TrySetEnabled(bool enabled, out int errorCode)
    {
        IUserRunKeyStore store = _storeFactory();

        if (enabled)
        {
            string? path = _processPathProvider();
            if (string.IsNullOrWhiteSpace(path))
            {
                errorCode = 1;
                return false;
            }

            if (!store.TrySetCommand(BuildCommand(path)))
            {
                errorCode = 2;
                return false;
            }
        }
        else
        {
            if (!store.TryDeleteCommand())
            {
                errorCode = 2;
                return false;
            }
        }

        errorCode = 0;
        return true;
    }

    private static string BuildCommand(string? processPath) => "\"" + processPath! + "\"";

    /// <summary>自启项（Run 值）存取接缝；单元测试以内存实现替换真实注册表。</summary>
    internal interface IUserRunKeyStore
    {
        bool TryGetCommand(out string? command);
        bool TrySetCommand(string command);
        bool TryDeleteCommand();
    }

    /// <summary>真实注册表存储（HKCU Run 键）；写失败（如策略禁用）返回 false 而不抛异常。</summary>
    private sealed class UserRunKeyStore : IUserRunKeyStore
    {
        public bool TryGetCommand(out string? command)
        {
            command = null;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                if (key is null)
                {
                    return false;
                }

                command = key.GetValue(RunValueName) as string;
                return true;
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }

        public bool TrySetCommand(string command)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key is null)
                {
                    return false;
                }

                key.SetValue(RunValueName, command);
                return true;
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }

        public bool TryDeleteCommand()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key is null)
                {
                    // 无 Run 键时视为已无自启项：幂等成功。
                    return true;
                }

                if (key.GetValue(RunValueName) is null)
                {
                    return true;
                }

                key.DeleteValue(RunValueName);
                return true;
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }
    }
}
