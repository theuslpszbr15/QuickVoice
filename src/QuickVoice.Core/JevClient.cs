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

/// <summary>
/// POST /v1/systemone. Docs: https://docs.typesafe.ai/api. Also speaks to Laya
/// (https://github.com/NandhaKishorM/laya), the open model that serves the same endpoint on this PC.
/// </summary>
public sealed class JevClient : IDecider
{
    public static readonly Uri Endpoint = new("https://api.typesafe.ai/v1/systemone");

    /// <summary>Laya on CPU reads well up to ~20 options per question and refuses more than 100.</summary>
    public const int LayaMaxApps = 16;
    public const int LayaMaxSpans = 12;

    public static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string apiKey;
    private readonly string model;
    private readonly Uri endpoint;
    private readonly bool laya;
    private readonly HttpClient http;

    public JevClient(string apiKey, string model = "jev-latest", HttpMessageHandler? handler = null)
        : this(apiKey, model, handler, Endpoint, laya: false)
    {
    }

    /// <summary>A local Laya server: no key, the multilingual checkpoint, fewer options per question.</summary>
    public static JevClient Laya(Uri endpoint, HttpMessageHandler? handler = null) =>
        new("", "multilingual", handler, endpoint, laya: true);

    private JevClient(string apiKey, string model, HttpMessageHandler? handler, Uri endpoint, bool laya)
    {
        this.apiKey = apiKey;
        this.model = model;
        this.endpoint = endpoint;
        this.laya = laya;
        // HTTP/2 over one warm connection: a fresh TLS handshake costs more than Jev's answer.
        http = new HttpClient(handler ?? new SocketsHttpHandler { PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10) })
        {
            Timeout = TimeSpan.FromSeconds(laya ? 20 : 10),  // Laya on CPU takes 1–3 s
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
    }

    public static string Serialize(object body) => JsonSerializer.Serialize(body, Json);

    /// <summary>One fan-out request for one tail. Returns the decision and the input tokens billed.</summary>
    public async Task<(Decision Decision, int Tokens)> DecideAsync(string tail, string frontmost, IReadOnlyList<string> apps,
                                                                   IReadOnlyList<string> spans, CancellationToken cancel = default)
    {
        if (laya)
        {
            apps = Shortlist(tail, apps, LayaMaxApps);
            spans = Endings(tail, spans, LayaMaxSpans);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(Serialize(Questions.Body(tail, frontmost, apps, spans, model, laya)),
                                        Encoding.UTF8, "application/json"),
        };
        if (apiKey.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new JevException.Http((int)response.StatusCode, text.Length > 300 ? text[..300] : text);
        var decoded = JsonSerializer.Deserialize<JevResponse>(text, Json) ?? throw new JevException.MissingAnswer("action");
        return (Decision.FromAnswers(decoded.Answers), decoded.Usage?.InputTokens ?? 0);
    }

    /// <summary>
    /// Only the spans that end the sentence ("…quero ver | as notícias de hoje"), longest first: Laya is asked once the
    /// speaker settles, the argument is nearly always last, and among every span it often picks a lone word.
    /// </summary>
    public static IReadOnlyList<string> Endings(string tail, IReadOnlyList<string> spans, int max)
    {
        var words = Vocabulary.Words(tail).Select(Vocabulary.TrimPunctuation).Where(w => w.Length > 0).ToArray();
        if (words.Length == 0) return [];
        var last = words[^1];
        return spans.Where(s => s == last || s.EndsWith(' ' + last, StringComparison.Ordinal))
            .OrderByDescending(s => s.Length)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// The apps a word of the tail could be naming (a shared start of 3+ letters: "spoti" → Spotify),
    /// best match first. A small model picks better among a few than among every installed app.
    /// </summary>
    public static IReadOnlyList<string> Shortlist(string tail, IReadOnlyList<string> apps, int max)
    {
        var said = Vocabulary.Words(tail).Select(Vocabulary.Normalized).Where(w => w.Length >= 3).Distinct().ToArray();
        if (said.Length == 0) return [];
        return apps
            .Select((app, index) => (App: app, Index: index, Score: Vocabulary.Words(app).Select(Vocabulary.Normalized)
                .Where(word => word.Length >= 3)
                .Select(word => said.Max(w => Shared(word, w)) is var n && n >= Math.Min(4, word.Length) ? n : 0)
                .DefaultIfEmpty(0).Max()))
            .Where(a => a.Score > 0)
            .OrderByDescending(a => a.Score)
            .ThenBy(a => a.Index)
            .Take(max)
            .Select(a => a.App)
            .ToList();
    }

    private static int Shared(string a, string b)
    {
        var n = 0;
        while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
        return n;
    }
}
