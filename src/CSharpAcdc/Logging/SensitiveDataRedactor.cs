using System.Text.Json;
using System.Web;
using CSharpAcdc.Configuration;

namespace CSharpAcdc.Logging;

/// <summary>
/// Redacts sensitive data from headers, URLs, and JSON bodies based on configured field names.
/// </summary>
public class SensitiveDataRedactor
{
    private const string Redacted = "[REDACTED]";
    private readonly HashSet<string> _sensitiveFields;

    /// <summary>
    /// Initializes a new instance of <see cref="SensitiveDataRedactor"/>.
    /// </summary>
    /// <param name="options">The logging options containing sensitive field names.</param>
    public SensitiveDataRedactor(AcdcLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _sensitiveFields = new HashSet<string>(options.SensitiveFields, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Redacts sensitive header values, replacing them with <c>[REDACTED]</c>.
    /// </summary>
    /// <param name="headers">The headers to redact.</param>
    /// <returns>A dictionary with sensitive values replaced.</returns>
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

    /// <summary>
    /// Redacts sensitive query string parameters in a URL.
    /// </summary>
    /// <param name="uri">The URI to redact.</param>
    /// <returns>The URI with sensitive query parameters replaced.</returns>
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

            var escapedName = Uri.EscapeDataString(name);
            var values = query.GetValues(name);

            if (IsSensitive(name))
            {
                var count = values?.Length ?? 1;
                for (var i = 0; i < count; i++)
                    redactedParts.Add($"{escapedName}={Redacted}");
            }
            else if (values is not null)
            {
                foreach (var value in values)
                    redactedParts.Add($"{escapedName}={Uri.EscapeDataString(value ?? string.Empty)}");
            }
            else
            {
                redactedParts.Add($"{escapedName}=");
            }
        }

        var builder = new UriBuilder(uri) { Query = string.Join("&", redactedParts) };
        return builder.Uri.ToString();
    }

    /// <summary>
    /// Redacts sensitive fields in a JSON body string.
    /// </summary>
    /// <param name="body">The JSON body to redact.</param>
    /// <returns>The redacted JSON string, or a placeholder if the body is not valid JSON.</returns>
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
            return "[non-JSON body, redaction skipped]";
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
