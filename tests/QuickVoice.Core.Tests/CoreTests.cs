using System.Net;
using System.Text.Json;
using QuickVoice.Core;

namespace QuickVoice.Core.Tests;

public class SiteTests
{
    [Theory]
    [InlineData("x dot com", "https://x.com")]
    [InlineData("Stripe dot com", "https://stripe.com")]
    [InlineData("youtube", "https://youtube.com")]
    [InlineData("wikipedia.org", "https://wikipedia.org")]
    [InlineData("g1 ponto com ponto br", "https://g1.com.br")]
    [InlineData("meu site do LinkedIn", "https://linkedin.com")]
    [InlineData("my website on github", "https://github.com")]
    [InlineData("linkedin no google", "https://linkedin.com")]
    [InlineData("meu linkedin pelo chrome", "https://linkedin.com")]
    public void SpokenSiteBecomesUrl(string spoken, string expected) => Assert.Equal(new Uri(expected), Site.Url(spoken));

    [Theory]
    [InlineData("")]
    [InlineData("what?")]
    [InlineData("x..com")]
    [InlineData("dot.")]
    [InlineData("meu site")]
    [InlineData("hacker news")]
    [InlineData("receita de bolo")]
    public void GarbageIsNotASite(string spoken) => Assert.Null(Site.Url(spoken));
}

public class CommandTests
{
    [Fact]
    public void BrowserCommandsHaveAWebAddress()
    {
        Assert.Equal("https://www.google.com/search?q=p%C3%A3o%20de%20queijo", new Command.WebSearch("pão de queijo").WebUrl?.AbsoluteUri);
        Assert.Equal(new Uri("https://x.com"), new Command.OpenUrl(new Uri("https://x.com")).WebUrl);
        Assert.Null(new Command.OpenApp("Notes").WebUrl);
    }

    [Fact]
    public void CommandsCompareByValue()
    {
        Assert.Equal<Command>(new Command.NewItem(), new Command.NewItem());
        Assert.NotEqual<Command>(new Command.OpenApp("Notes"), new Command.TypeText("Notes"));
    }
}

public class CandidatesTests
{
    [Fact]
    public void EveryContiguousSpanIsACandidate()
    {
        var spans = Candidates.Spans(["google", "search", "norbert", "wiener"]);
        Assert.Equal(7, spans.Count);  // 10 spans minus the three starting with "search"
        Assert.DoesNotContain("search norbert wiener", spans);
        Assert.Contains("norbert wiener", spans);
        Assert.Contains("google search norbert wiener", spans);
    }

    [Fact]
    public void DuplicatesAppearOnce() => Assert.Equal(["ha", "ha ha", "ha ha ha"], Candidates.Spans(["ha", "ha", "ha"]));

    [Fact]
    public void ArgumentsNeverStartWithTheCommandOrEndDangling()
    {
        Assert.Equal(["ls"], Candidates.Spans(["digita", "ls"]));
        Assert.Equal(["receita"], Candidates.Spans(["receita", "de"]));
        Assert.All(Candidates.Spans(["e", "digita", "bom", "dia"]), s => Assert.False(s.StartsWith("e ") || s.StartsWith("digita")));
        Assert.Contains("the meeting", Candidates.Spans(["type", "the", "meeting"]));
    }

    [Fact]
    public void CommandWordsAndFillerAreNeverArguments()
    {
        Assert.Empty(Candidates.Spans(["e", "pesquisar"]));
        var spans = Candidates.Spans(["e", "pesquisar", "receita"]);
        Assert.Contains("receita", spans);
        Assert.DoesNotContain("pesquisar", spans);
        Assert.DoesNotContain("e pesquisar", spans);
    }

    [Fact]
    public void EdgePunctuationIsTrimmed()
    {
        var spans = Candidates.Spans(["say", "hello.", "you're", "x.com"]);
        Assert.Contains("say hello", spans);
        Assert.DoesNotContain("hello.", spans);
        Assert.Contains("you're x.com", spans);
    }

    [Fact]
    public void SpansStopAtEightWords() =>
        Assert.Equal(8, Candidates.Spans(Enumerable.Range(1, 10).Select(i => $"w{i}").ToList()).Max(s => s.Split(' ').Length));

    [Fact]
    public void OnlyTheLastThirtyWordsCount()
    {
        var spans = Candidates.Spans(Enumerable.Range(1, 40).Select(i => $"w{i}").ToList());
        Assert.DoesNotContain("w10", spans);
        Assert.Contains("w11", spans);
    }
}

public class QuestionsTests
{
    private static JsonElement Json(object body) => JsonDocument.Parse(JevClient.Serialize(body)).RootElement;

    private static HashSet<string> Keys(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToHashSet();

    [Fact]
    public void BodyCarriesStateAndFiveQuestions()
    {
        var body = Json(Questions.Body("open notes", "Explorer", ["Notes"], ["notes"], "jev-latest"));
        Assert.Equal("jev-latest", body.GetProperty("model").GetString());
        Assert.Equal("open notes", body.GetProperty("state").GetProperty("transcript").GetString());
        Assert.Equal("Explorer", body.GetProperty("state").GetProperty("frontmost_app").GetString());
        var questions = body.GetProperty("questions");
        Assert.Equal(["action", "app", "argument", "complete", "opens_app"], Keys(questions).Order());
        Assert.Equal("noul", questions.GetProperty("complete").GetProperty("type").GetString());
        Assert.Equal("noul", questions.GetProperty("opens_app").GetProperty("type").GetString());
        var actions = questions.GetProperty("action").GetProperty("criteria");
        Assert.Equal(ActionKinds.Jev.Select(a => a.RawValue()).ToHashSet(), Keys(actions));
        Assert.Equal(JsonValueKind.String, actions.GetProperty("type_text").GetProperty("what").ValueKind);
        Assert.Equal(JsonValueKind.Array, actions.GetProperty("type_text").GetProperty("examples").ValueKind);
    }

    [Fact]
    public void CandidatesAreBareKeysPlusAnEscapeHatch()
    {
        var questions = Json(Questions.Body("open notes", "Explorer", ["Notes"], ["notes"], "m")).GetProperty("questions");
        var apps = questions.GetProperty("app").GetProperty("criteria");
        Assert.Equal(JsonValueKind.Null, apps.GetProperty("Notes").ValueKind);
        Assert.Equal(JsonValueKind.String, apps.GetProperty("none").ValueKind);
        var spans = questions.GetProperty("argument").GetProperty("criteria");
        Assert.Equal(JsonValueKind.Null, spans.GetProperty("notes").ValueKind);
        Assert.Equal(JsonValueKind.String, spans.GetProperty(Questions.NoMatch).ValueKind);
    }

    [Fact]
    public void OptionListsStayUnderTheChoiceCap()
    {
        var many = Enumerable.Range(1, 300).Select(i => $"App{i}").ToList();
        var questions = Json(Questions.Body("t", "Explorer", many, many, "m")).GetProperty("questions");
        Assert.Equal(255, Keys(questions.GetProperty("app").GetProperty("criteria")).Count);
        Assert.Equal(255, Keys(questions.GetProperty("argument").GetProperty("criteria")).Count);
    }

    [Fact]
    public void EmptyListsDropTheirQuestion() =>
        Assert.Equal(["action", "complete"], Keys(Json(Questions.Body("t", "Explorer", [], [], "m")).GetProperty("questions")).Order());
}

public class DecisionTests
{
    public const string Fixture = """
        {"model":"jev-1.13.0","answers":{
          "action":{"type":"choice","choice":"open_app","probabilities":{"open_app":0.97,"none":0.03},"confidence":0.9},
          "app":{"type":"choice","choice":"Notes","probabilities":{"Notes":0.96,"none":0.04},"confidence":0.9},
          "argument":{"type":"choice","choice":"no_match","probabilities":{"no_match":1.0},"confidence":1.0},
          "complete":{"type":"noul","noul":0.81},
          "opens_app":{"type":"noul","noul":0.9}},
         "usage":{"input_tokens":1581,"output_tokens":20}}
        """;

    [Fact]
    public void MapsTypedAnswers()
    {
        var response = JsonSerializer.Deserialize<JevResponse>(Fixture, JevClient.Json)!;
        var d = Decision.FromAnswers(response.Answers);
        Assert.Equal(ActionKind.OpenApp, d.Action);
        Assert.Equal(0.9, d.Confidence);
        Assert.Equal(0.97, d.ActionProbabilities[ActionKind.OpenApp]);
        Assert.Equal(0.03, d.ActionProbabilities[ActionKind.None]);
        Assert.Equal("Notes", d.App);
        Assert.Equal(0.96, d.AppProbability);
        Assert.Null(d.Argument);
        Assert.Equal(0.81, d.Complete);
        Assert.Equal(0.9, d.OpensApp);
        Assert.Equal(1581, response.Usage?.InputTokens);
    }

    [Fact]
    public void NoneAppMeansNoApp()
    {
        var d = Decision.FromAnswers(new Dictionary<string, JevAnswer>
        {
            ["action"] = new(Choice: "web_search", Confidence: 1),
            ["app"] = new(Choice: "none", Probabilities: new() { ["none"] = 1 }),
            ["argument"] = new(Choice: "lisbon"),
        });
        Assert.Null(d.App);
        Assert.Equal(0, d.AppProbability);
        Assert.Equal("lisbon", d.Argument);
    }

    [Fact]
    public void MissingOrUnknownActionThrows()
    {
        Assert.Throws<JevException.MissingAnswer>(() => Decision.FromAnswers(new Dictionary<string, JevAnswer>()));
        var e = Assert.Throws<JevException.UnexpectedAnswer>(() =>
            Decision.FromAnswers(new Dictionary<string, JevAnswer> { ["action"] = new(Choice: "fly") }));
        Assert.Equal("fly", e.Value);
    }

    [Fact]
    public void TopSortsByProbabilityThenName()
    {
        var d = new Decision
        {
            ActionProbabilities = new Dictionary<ActionKind, double>
                { [ActionKind.TypeText] = 0.3, [ActionKind.None] = 0.3, [ActionKind.OpenApp] = 0.4, [ActionKind.NewItem] = 0.0 },
        };
        Assert.Equal([ActionKind.OpenApp, ActionKind.None, ActionKind.TypeText], d.Top(3).Select(t => t.Action));
    }
}

public class JevClientTests
{
    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task PostsToSystemOneAndDecodes()
    {
        var stub = new Stub(HttpStatusCode.OK, DecisionTests.Fixture);
        var (decision, tokens) = await new JevClient("test-key", handler: stub).DecideAsync("open notes", "Explorer", ["Notes"], ["notes"]);
        Assert.Equal(ActionKind.OpenApp, decision.Action);
        Assert.Equal(1581, tokens);
        Assert.Equal(JevClient.Endpoint, stub.Seen!.RequestUri);
        Assert.Equal(HttpMethod.Post, stub.Seen.Method);
        Assert.Equal("Bearer test-key", stub.Seen.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task HttpErrorSurfacesStatusAndBody()
    {
        var client = new JevClient("k", handler: new Stub(HttpStatusCode.Unauthorized, "bad key"));
        var e = await Assert.ThrowsAsync<JevException.Http>(() => client.DecideAsync("t", "Explorer", [], []));
        Assert.Equal(401, e.Status);
        Assert.Equal("bad key", e.Body);
    }
}

public class DisplayTests
{
    [Fact]
    public void MarksConsumedWordsAndKeepsTheLast()
    {
        var words = Display.Words("abre as notas e digita bom dia", consumed: 3, limit: 5);
        Assert.Equal(["notas", "e", "digita", "bom", "dia"], words.Select(w => w.Text));
        Assert.Equal([true, false, false, false, false], words.Select(w => w.Used));
    }
}
