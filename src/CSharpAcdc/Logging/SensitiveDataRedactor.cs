using System.Text.Json;
using System.Web;
using CSharpAcdc.Configuration;

namespace CSharpAcdc.Logging;

public class SensitiveDataRedactor
{
    private const string Redacted = "[REDACTED]";
    private readonly IReadOnlySet<string> _sensitiveFields;

    public SensitiveDataRedactor(AcdcLoggingOptions options)
    {
        _sensitiveFields = options.SensitiveFields;
    }

    public Dictionary<string, string> RedactHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            result[header.Key] = IsSensitive(header.Key)
                ? Redacted
                : string.Join(", ", header.Value);
        }

        return result;
    }

    public string RedactUrl(Uri? uri)
    {
        if (uri is null)
            return string.Empty;

        if (string.IsNullOrEmpty(uri.Query))
            return uri.ToString();

        var query = HttpUtility.ParseQueryString(uri.Query);
        var redactedParts = new List<string>();

        foreach (string? name in query)
        {
            if (name is null)
                continue;

            redactedParts.Add(IsSensitive(name)
                ? $"{Uri.EscapeDataString(name)}={Redacted}"
                : $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(query[name] ?? string.Empty)}");
        }

        var builder = new UriBuilder(uri) { Query = string.Join("&", redactedParts) };
        return builder.Uri.ToString();
    }

    public string? RedactJsonBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return body;

        try
        {
            using var doc = JsonDocument.Parse(body);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                RedactJsonElement(doc.RootElement, writer);
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return body; // Not valid JSON — return as-is
        }
    }

    private void RedactJsonElement(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitive(property.Name))
                        writer.WriteStringValue(Redacted);
                    else
                        RedactJsonElement(property.Value, writer);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    RedactJsonElement(item, writer);
                }
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private bool IsSensitive(string fieldName)
        => _sensitiveFields.Contains(fieldName);
}
