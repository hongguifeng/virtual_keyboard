namespace VirtualKeyboard.Core.Diagnostics;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// 诊断事件唯一序列化点（单一 JSON）。
/// <see cref="DiagnosticEvent"/> 自身类型受约束（封闭枚举 + 数字字段 + 版本号），
/// 因此序列化内容确定性地不含敏感数据；此处不开放任何自由文本字段。
/// </summary>
public static class DiagnosticSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJsonLine(DiagnosticEvent e) => JsonSerializer.Serialize(e, Options);
}
