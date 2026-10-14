using System.Net;
using System.Text.Json;
using QuickVoice.Core;

namespace QuickVoice.Core.Tests;

public class LayaTests
{
    private sealed class Stub(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;
        public string Body = "";
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Seen = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return respond();
        }
    }

    private static readonly Uri Local = new("http://127.0.0.1:8765/v1/systemone");

    [Fact]
    public async Task LayaPostsWithoutKeyAndDescribesOptions()
    {
        var stub = new Stub(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DecisionTests.Fixture) });
        var apps = Enumerable.Range(0, 120).Select(i => $"App {i}").Append("Spotify").ToList();
        await JevClient.Laya(Local, stub).DecideAsync("abre o spotify", "Explorer", apps, ["spotify"]);
        Assert.Equal(Local, stub.Seen!.RequestUri);
        Assert.Null(stub.Seen.Headers.Authorization);
        using var json = JsonDocument.Parse(stub.Body);
        Assert.Equal("multilingual", json.RootElement.GetProperty("model").GetString());
        var app = json.RootElement.GetProperty("questions").GetProperty("app").GetProperty("criteria");
        Assert.Equal(["Spotify", "none"], app.EnumerateObject().Select(o => o.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Spotify", app.GetProperty("Spotify").GetString());
        Assert.False(json.RootElement.GetProperty("questions").TryGetProperty("complete", out _));
    }

    [Fact]
    public async Task JevKeepsNullOptionsAndTheKey()
    {
        var stub = new Stub(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DecisionTests.Fixture) });
        await new JevClient("k", handler: stub).DecideAsync("open notes", "Explorer", ["Notes"], ["notes"]);
        Assert.Equal("Bearer", stub.Seen!.Headers.Authorization?.Scheme);
        using var json = JsonDocument.Parse(stub.Body);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("questions").GetProperty("app").GetProperty("criteria").GetProperty("Notes").ValueKind);
    }

    [Theory]
    [InlineData("abre o spoti", "Spotify")]
    [InlineData("abre o visual studio", "Visual Studio Code")]
    [InlineData("vai no microsoft", "Microsoft Edge")]
    public void ShortlistKeepsAppsTheWordsStart(string tail, string expected) =>
        Assert.Contains(expected, JevClient.Shortlist(tail, ["Google Chrome", "Microsoft Edge", "Spotify", "Visual Studio Code", "Notepad"], 16));

    [Fact]
    public void ShortlistIsEmptyWhenNoAppIsNamed()
    {
        Assert.Empty(JevClient.Shortlist("pesquisa receita de bolo", ["Google Chrome", "Spotify"], 16));
        Assert.Empty(JevClient.Shortlist("bolo de cenoura", ["Software Center"], 16));  // "cen" is not enough
    }

    [Fact]
    public void LayaGetsOnlySpansThatEndTheSentence()
    {
        var tail = "quero ver as notícias de hoje";
        var endings = JevClient.Endings(tail, Candidates.Spans(Vocabulary.Words(tail)), 12);
        Assert.Contains("as notícias de hoje", endings);
        Assert.DoesNotContain("quero", endings);
        Assert.All(endings, s => Assert.EndsWith("hoje", s));
    }

    [Fact]
    public async Task HybridAsksTheModelOnlyWhenTheRulesDontKnow()
    {
        var stub = new Stub(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DecisionTests.Fixture) });
        var hybrid = new HybridDecider(new LocalDecider(), JevClient.Laya(Local, stub), TimeSpan.Zero);
        var (known, _) = await hybrid.DecideAsync("abre o spotify", "Explorer", ["Spotify"], ["spotify"]);
        Assert.Equal(ActionKind.OpenApp, known.Action);
        Assert.Equal(0, stub.Calls);
        var (asked, tokens) = await hybrid.DecideAsync("me deixa ver minhas anotações", "Explorer", ["Notes"], ["anotações"]);
        Assert.Equal(1, stub.Calls);
        Assert.Equal("Notes", asked.App);
        Assert.Equal(1581, tokens);
    }

    [Fact]
    public async Task HybridFallsBackToTheRulesWhileTheModelIsDown()
    {
        var stub = new Stub(() => throw new HttpRequestException("connection refused"));
        var hybrid = new HybridDecider(new LocalDecider(), JevClient.Laya(Local, stub), TimeSpan.Zero);
        var (first, _) = await hybrid.DecideAsync("me deixa ver minhas anotações", "Explorer", ["Notes"], []);
        var (second, _) = await hybrid.DecideAsync("me deixa ver minhas anotações agora", "Explorer", ["Notes"], []);
        Assert.Equal(ActionKind.None, first.Action);
        Assert.Equal(ActionKind.None, second.Action);
        Assert.Equal(1, stub.Calls);  // waits before trying again
    }

    [Fact]
    public async Task HybridSkipsPartialsSaidWhileWaiting()
    {
        var stub = new Stub(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DecisionTests.Fixture) });
        var hybrid = new HybridDecider(new LocalDecider(), JevClient.Laya(Local, stub), TimeSpan.FromMilliseconds(200));
        var early = hybrid.DecideAsync("me deixa", "Explorer", ["Notes"], []);
        var late = hybrid.DecideAsync("me deixa ver minhas anotações", "Explorer", ["Notes"], []);
        Assert.Equal(ActionKind.None, (await early).Decision.Action);
        Assert.Equal("Notes", (await late).Decision.App);
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task HybridLeavesShortLeftoversToTheRules()
    {
        var stub = new Stub(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DecisionTests.Fixture) });
        var hybrid = new HybridDecider(new LocalDecider(), JevClient.Laya(Local, stub), TimeSpan.Zero);
        var (leftover, _) = await hybrid.DecideAsync("notas", "Bloco de notas", ["Notes"], ["notas"]);
        Assert.Equal(ActionKind.None, leftover.Action);
        Assert.Equal(0, stub.Calls);
    }
}
