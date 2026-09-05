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
        Converters = { new JsonStringEnumConverter(), new AppVersionJsonConverter() },
    };

    public static string ToJsonLine(DiagnosticEvent e) => JsonSerializer.Serialize(e, Options);

    /// <summary>
    /// 将 <see cref="AppVersion"/> 序列化为 {"Major":..,"Minor":..,"Revision":..}，
    /// 保证版本号在 JSON 中以三个数字呈现（而非自由字符串）。
    /// </summary>
    private sealed class AppVersionJsonConverter : JsonConverter<AppVersion>
    {
        public override AppVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected an AppVersion object.");
            }

            var major = 0;
            var minor = 0;
            var revision = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                var name = reader.GetString();
                if (!reader.Read())
                {
                    break;
                }

                var value = reader.GetInt32();
                switch (name)
                {
                    case "Major":
                        major = (ushort)value;
                        break;
                    case "Minor":
                        minor = (ushort)value;
                        break;
                    case "Revision":
                        revision = (ushort)value;
                        break;
                }
            }

            return new AppVersion((ushort)major, (ushort)minor, (ushort)revision);
        }

        public override void Write(Utf8JsonWriter writer, AppVersion value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Major", value.Major);
            writer.WriteNumber("Minor", value.Minor);
            writer.WriteNumber("Revision", value.Revision);
            writer.WriteEndObject();
        }
    }
}
