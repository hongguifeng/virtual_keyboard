namespace VirtualKeyboard.Core.Diagnostics;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// 结构化应用版本（Major/Minor/Revision 均为 ushort）。
/// 用于 <see cref="DiagnosticEvent.AppVersion"/>：相比自由字符串，
/// 该类型在编译期不可能承载任意文本（NFR-PRI-001 / 设计文档 15.2），
/// 序列化结果由三个数字确定性决定。
/// </summary>
[ExcludeFromCodeCoverage]
public readonly struct AppVersion : IEquatable<AppVersion>
{
    public const int MaxComponent = ushort.MaxValue;

    /// <summary>主版本号（0–65535）。</summary>
    public ushort Major { get; }

    /// <summary>次版本号（0–65535）。</summary>
    public ushort Minor { get; }

    /// <summary>修订号（0–65535）。</summary>
    public ushort Revision { get; }

    public AppVersion(ushort major, ushort minor, ushort revision)
    {
        Major = major;
        Minor = minor;
        Revision = revision;
    }

    public static bool operator ==(AppVersion left, AppVersion right) => left.Equals(right);

    public static bool operator !=(AppVersion left, AppVersion right) => !left.Equals(right);

    /// <summary>
    /// 解析形如 "MAJOR.MINOR.REVISION" 的版本号；任一组件缺失或超出 0–65535 返回 false。
    /// 供宿主把配置/运行时版本注入 <see cref="DiagnosticLogger.Log"/>。
    /// </summary>
    public static bool TryParse(string? s, [NotNullWhen(true)] out AppVersion? result)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            result = null;
            return false;
        }

        var parts = s.Split('.');
        if (parts.Length != 3)
        {
            result = null;
            return false;
        }

        var components = new ushort[3];
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], out var n) || n < 0 || n > MaxComponent)
            {
                result = null;
                return false;
            }

            components[i] = (ushort)n;
        }

        result = new AppVersion(components[0], components[1], components[2]);
        return true;
    }

    public bool Equals(AppVersion other) =>
        Major == other.Major && Minor == other.Minor && Revision == other.Revision;

    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Revision);

    public override string ToString() => $"{Major}.{Minor}.{Revision}";
}
