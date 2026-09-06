namespace VirtualKeyboard.Core.AutoStart;

/// <summary>
/// 当前用户开机自启（FR-APP-004；设计文档 14.4）。
/// Core 只依赖本接口；具体实现位于 Windows 适配层（HKCU Run 键，不要求管理员权限）。
/// 结果只携带数字错误码，不携带注册表路径等本机信息（NFR-PRI-001）。
/// </summary>
public interface IAutoStartManager
{
    /// <summary>
    /// 读取当前用户开机自启项的真实状态。
    /// 返回 true 表示读取成功（enabled 为自启项存在、非空且指向当前可执行文件）；
    /// 返回 false 表示读取失败（如注册表不可访问），enabled 为 false。
    /// </summary>
    bool TryGetEnabled(out bool enabled);

    /// <summary>
    /// 将当前用户开机自启项设置为指定状态（启用 = 写入带引号的启动命令；禁用 = 删除自启项）。
    /// 返回 true 表示操作完成（幂等）；返回 false 表示失败，errorCode 为非零数字码。
    /// </summary>
    bool TrySetEnabled(bool enabled, out int errorCode);
}
