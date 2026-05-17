using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lzq.Core.Json;

/// <summary>
/// DateTime 格式化转换器 (yyyy-MM-dd HH:mm:ss)
/// </summary>
public class DateTimeFormatConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd HH:mm:ss";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (DateTime.TryParseExact(stringValue, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
                return result;

            // 兼容其他常见格式
            if (DateTime.TryParse(stringValue, out result))
                return result;
        }

        return reader.GetDateTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Format));
    }
}

/// <summary>
/// DateTime? 格式化转换器 (yyyy-MM-dd HH:mm:ss)
/// </summary>
public class DateTimeNullableFormatConverter : JsonConverter<DateTime?>
{
    private const string Format = "yyyy-MM-dd HH:mm:ss";

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (string.IsNullOrEmpty(stringValue))
                return null;

            if (DateTime.TryParseExact(stringValue, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
                return result;

            // 兼容其他常见格式
            if (DateTime.TryParse(stringValue, out result))
                return result;
        }

        return reader.GetDateTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value.ToString(Format));
    }
}
