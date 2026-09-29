using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickVoice.Core;

public sealed record JevAnswer(
    [property: JsonPropertyName("choice")] string? Choice = null,
    [property: JsonPropertyName("probabilities")] Dictionary<string, double>? Probabilities = null,
    [property: JsonPropertyName("confidence")] double? Confidence = null,
    [property: JsonPropertyName("noul")] double? Noul = null);

public sealed record JevUsage([property: JsonPropertyName("input_tokens")] int InputTokens);

public sealed record JevResponse(
    [property: JsonPropertyName("answers")] Dictionary<string, JevAnswer> Answers,
    [property: JsonPropertyName("usage")] JevUsage? Usage);

public abstract class JevException(string message) : Exception(message)
{
    public sealed class Http(int status, string body) : JevException($"Jev HTTP {status}: {body}")
    {
        public int Status { get; } = status;
        public string Body { get; } = body;
    }

    public sealed class MissingAnswer(string question) : JevException($"Jev returned no answer for “{question}”")
    {
        public string Question { get; } = question;
    }

    public sealed class UnexpectedAnswer(string question, string value)
        : JevException($"Jev answered “{value}” to “{question}”, not one of the options")
    {
        public string Question { get; } = question;
        public string Value { get; } = value;
    }
}

/// <summary>POST /v1/systemone. Docs: https://docs.typesafe.ai/api</summary>
public sealed class JevClient : IDecider
{
    public static readonly Uri Endpoint = new("https://api.typesafe.ai/v1/systemone");

    public static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string apiKey;
    private readonly string model;
    private readonly HttpClient http;

    public JevClient(string apiKey, string model = "jev-latest", HttpMessageHandler? handler = null)
    {
        this.apiKey = apiKey;
        this.model = model;
        // HTTP/2 over one warm connection: a fresh TLS handshake costs more than Jev's answer.
        http = new HttpClient(handler ?? new SocketsHttpHandler { PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10) })
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
    }

    public static string Serialize(object body) => JsonSerializer.Serialize(body, Json);

    /// <summary>One fan-out request for one tail. Returns the decision and the input tokens billed.</summary>
    public async Task<(Decision Decision, int Tokens)> DecideAsync(string tail, string frontmost, IReadOnlyList<string> apps,
                                                                   IReadOnlyList<string> spans, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(Serialize(Questions.Body(tail, frontmost, apps, spans, model)), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new JevException.Http((int)response.StatusCode, text.Length > 300 ? text[..300] : text);
        var decoded = JsonSerializer.Deserialize<JevResponse>(text, Json) ?? throw new JevException.MissingAnswer("action");
        return (Decision.FromAnswers(decoded.Answers), decoded.Usage?.InputTokens ?? 0);
    }
}
