using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SubsRo.Converters;

/// <summary>
/// Custom JSON converter for DateTime values in the format "yyyy-MM-dd HH:mm:ss".
/// </summary>
public class CustomDateTimeConverter : JsonConverter<DateTime>
{
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// Reads a DateTime value from JSON.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The parsed DateTime.</returns>
    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var dateString = reader.GetString();
            if (string.IsNullOrEmpty(dateString))
            {
                return DateTime.MinValue;
            }

            if (
                DateTime.TryParseExact(
                    dateString,
                    DateTimeFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var result
                )
            )
            {
                return result;
            }

            // Fallback to default parsing
            if (
                DateTime.TryParse(
                    dateString,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out result
                )
            )
            {
                return result;
            }

            throw new JsonException($"Unable to parse DateTime from value: {dateString}");
        }

        throw new JsonException($"Unexpected token type: {reader.TokenType}");
    }

    /// <summary>
    /// Writes a DateTime value to JSON.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The DateTime value.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
    }
}
