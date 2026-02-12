using CSharpAcdc.Configuration;
using CSharpAcdc.Logging;
using FluentAssertions;
using Xunit;

namespace CSharpAcdc.Tests.Logging;

public class SensitiveDataRedactorTests
{
    private static SensitiveDataRedactor CreateRedactor(AcdcLoggingOptions? options = null)
        => new(options ?? new AcdcLoggingOptions());

    [Fact]
    public void RedactHeaders_RedactsSensitiveHeaders()
    {
        var redactor = CreateRedactor();
        var headers = new Dictionary<string, IEnumerable<string>>
        {
            ["Authorization"] = ["Bearer secret-token"],
            ["Content-Type"] = ["application/json"],
            ["Cookie"] = ["session=abc123"],
        };

        var result = redactor.RedactHeaders(headers);

        result["Authorization"].Should().Be("[REDACTED]");
        result["Content-Type"].Should().Be("application/json");
        result["Cookie"].Should().Be("[REDACTED]");
    }

    [Theory]
    [InlineData("authorization")]
    [InlineData("AUTHORIZATION")]
    [InlineData("Authorization")]
    [InlineData("cookie")]
    [InlineData("COOKIE")]
    [InlineData("set-cookie")]
    [InlineData("SET-COOKIE")]
    [InlineData("x-api-key")]
    [InlineData("X-API-KEY")]
    [InlineData("password")]
    [InlineData("PASSWORD")]
    [InlineData("token")]
    [InlineData("TOKEN")]
    [InlineData("secret")]
    [InlineData("SECRET")]
    [InlineData("key")]
    [InlineData("KEY")]
    [InlineData("credential")]
    [InlineData("CREDENTIAL")]
    [InlineData("access_token")]
    [InlineData("ACCESS_TOKEN")]
    [InlineData("refresh_token")]
    [InlineData("REFRESH_TOKEN")]
    [InlineData("client_secret")]
    [InlineData("CLIENT_SECRET")]
    [InlineData("api_key")]
    [InlineData("API_KEY")]
    [InlineData("private_key")]
    [InlineData("PRIVATE_KEY")]
    [InlineData("session_id")]
    [InlineData("SESSION_ID")]
    [InlineData("x-csrf-token")]
    [InlineData("X-CSRF-TOKEN")]
    public void RedactHeaders_AllDefaultSensitiveFields_CaseInsensitive(string headerName)
    {
        var redactor = CreateRedactor();
        var headers = new Dictionary<string, IEnumerable<string>>
        {
            [headerName] = ["sensitive-value"],
        };

        var result = redactor.RedactHeaders(headers);

        result[headerName].Should().Be("[REDACTED]");
    }

    [Fact]
    public void RedactHeaders_NonSensitiveHeaders_NotRedacted()
    {
        var redactor = CreateRedactor();
        var headers = new Dictionary<string, IEnumerable<string>>
        {
            ["Accept"] = ["text/html"],
            ["Content-Length"] = ["42"],
        };

        var result = redactor.RedactHeaders(headers);

        result["Accept"].Should().Be("text/html");
        result["Content-Length"].Should().Be("42");
    }

    [Fact]
    public void RedactHeaders_MultipleValues_JoinedWithComma()
    {
        var redactor = CreateRedactor();
        var headers = new Dictionary<string, IEnumerable<string>>
        {
            ["Accept"] = ["text/html", "application/json"],
        };

        var result = redactor.RedactHeaders(headers);

        result["Accept"].Should().Be("text/html, application/json");
    }

    [Fact]
    public void RedactHeaders_CustomSensitiveField()
    {
        var options = new AcdcLoggingOptions
        {
            SensitiveFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "X-Custom-Secret" },
        };
        var redactor = CreateRedactor(options);
        var headers = new Dictionary<string, IEnumerable<string>>
        {
            ["X-Custom-Secret"] = ["my-secret"],
            ["Authorization"] = ["Bearer token"], // Not sensitive with custom set
        };

        var result = redactor.RedactHeaders(headers);

        result["X-Custom-Secret"].Should().Be("[REDACTED]");
        result["Authorization"].Should().Be("Bearer token"); // Not in custom set
    }

    [Fact]
    public void RedactUrl_NullUri_ReturnsEmpty()
    {
        var redactor = CreateRedactor();

        var result = redactor.RedactUrl(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void RedactUrl_NoQueryParams_ReturnsOriginal()
    {
        var redactor = CreateRedactor();
        var uri = new Uri("https://api.example.com/users");

        var result = redactor.RedactUrl(uri);

        result.Should().Be("https://api.example.com/users");
    }

    [Fact]
    public void RedactUrl_SensitiveQueryParam_Redacted()
    {
        var redactor = CreateRedactor();
        var uri = new Uri("https://api.example.com/users?token=secret123&page=1");

        var result = redactor.RedactUrl(uri);

        result.Should().Contain("token=[REDACTED]");
        result.Should().Contain("page=1");
        result.Should().NotContain("secret123");
    }

    [Fact]
    public void RedactUrl_MultipleSensitiveQueryParams_AllRedacted()
    {
        var redactor = CreateRedactor();
        var uri = new Uri("https://api.example.com/auth?api_key=mykey123&password=s3cret!&page=2");

        var result = redactor.RedactUrl(uri);

        result.Should().Contain("api_key=[REDACTED]");
        result.Should().Contain("password=[REDACTED]");
        result.Should().Contain("page=2");
        result.Should().NotContain("mykey123");
        result.Should().NotContain("s3cret!");
    }

    [Fact]
    public void RedactJsonBody_NullBody_ReturnsNull()
    {
        var redactor = CreateRedactor();

        var result = redactor.RedactJsonBody(null);

        result.Should().BeNull();
    }

    [Fact]
    public void RedactJsonBody_EmptyBody_ReturnsEmpty()
    {
        var redactor = CreateRedactor();

        var result = redactor.RedactJsonBody("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void RedactJsonBody_InvalidJson_ReturnsPlaceholder()
    {
        var redactor = CreateRedactor();

        var result = redactor.RedactJsonBody("not json at all");

        result.Should().Be("[non-JSON body, redaction skipped]");
    }

    [Fact]
    public void RedactJsonBody_SensitiveFields_Redacted()
    {
        var redactor = CreateRedactor();
        var body = """{"username":"john","password":"secret","email":"john@example.com"}""";

        var result = redactor.RedactJsonBody(body);

        result.Should().Contain("\"password\":\"[REDACTED]\"");
        result.Should().Contain("\"username\":\"john\"");
        result.Should().Contain("\"email\":\"john@example.com\"");
        result.Should().NotContain("secret");
    }

    [Fact]
    public void RedactJsonBody_NestedObjects_RedactsSensitiveFields()
    {
        var redactor = CreateRedactor();
        var body = """{"user":{"name":"john","credentials":{"password":"secret","token":"abc"}}}""";

        var result = redactor.RedactJsonBody(body);

        result.Should().Contain("\"password\":\"[REDACTED]\"");
        result.Should().Contain("\"token\":\"[REDACTED]\"");
        result.Should().Contain("\"name\":\"john\"");
        result.Should().NotContain("secret");
        result.Should().NotContain("abc");
    }

    [Fact]
    public void RedactJsonBody_ArraysPreserved()
    {
        var redactor = CreateRedactor();
        var body = """{"items":[{"id":1,"token":"abc"},{"id":2,"token":"def"}]}""";

        var result = redactor.RedactJsonBody(body);

        result.Should().Contain("\"token\":\"[REDACTED]\"");
        result.Should().Contain("\"id\":1");
        result.Should().Contain("\"id\":2");
    }

    [Fact]
    public void RedactJsonBody_NonObjectValues_Preserved()
    {
        var redactor = CreateRedactor();
        var body = """{"count":42,"active":true,"name":null,"tags":["a","b"]}""";

        var result = redactor.RedactJsonBody(body);

        result.Should().Contain("\"count\":42");
        result.Should().Contain("\"active\":true");
        result.Should().Contain("\"name\":null");
    }
}
