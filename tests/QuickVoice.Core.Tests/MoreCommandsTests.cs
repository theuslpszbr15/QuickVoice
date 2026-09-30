using QuickVoice.Core;

namespace QuickVoice.Core.Tests;

public class MoreCommandsTests
{
    private static readonly string[] Apps = ["Bloco de notas", "Google Chrome", "Microsoft Teams", "Spotify", "Outlook (new)"];

    private static List<Command> Say(string sentence, bool typed = false)
    {
        var decider = new LocalDecider();
        var aliases = LocalDecider.WithSpokenNames(Apps.ToDictionary(a => a, a => new List<string> { a }));
        var engine = new Engine { AppAliases = aliases, Browsers = new HashSet<string> { "Google Chrome" } };
        var fired = new List<Command>();

        void Run(Engine.Step step)
        {
            if (step.Command is { } c) fired.Add(c);
            if (step.Request is { } r) Run(engine.Receive(decider.Decide(r.Text, Apps), r));
        }

        var words = Vocabulary.Words(sentence);
        for (var n = typed ? words.Length : 1; n <= words.Length; n++)
        {
            if (engine.Hear(string.Join(' ', words.Take(n))) is { } request)
                Run(engine.Receive(decider.Decide(request.Text, Apps), request));
        }
        Run(engine.Pause());
        return fired;
    }

    [Theory]
    [InlineData("desfaz isso", SystemAction.Undo)]
    [InlineData("desfazer", SystemAction.Undo)]
    [InlineData("undo", SystemAction.Undo)]
    [InlineData("repete o último comando", SystemAction.Repeat)]
    [InlineData("faz de novo", SystemAction.Repeat)]
    [InlineData("joga isso na esquerda", SystemAction.SnapLeft)]
    [InlineData("coloca a janela na direita", SystemAction.SnapRight)]
    [InlineData("manda pro outro monitor", SystemAction.OtherMonitor)]
    public void NewControls(string sentence, SystemAction action) =>
        Assert.Equal([new Command.Control(action)], Say(sentence));

    [Theory]
    [InlineData("fecha o chrome", "Google Chrome")]
    [InlineData("fecha o spotify", "Spotify")]
    [InlineData("close teams", "Microsoft Teams")]
    public void ClosesAnAppByName(string sentence, string app) =>
        Assert.Equal([new Command.CloseApp(app)], Say(sentence));

    [Fact]
    public void ClosingTheWindowInFrontStillWorks() =>
        Assert.Equal([new Command.Control(SystemAction.CloseWindow)], Say("fecha isso"));

    [Theory]
    [InlineData("vai pro teams", "Microsoft Teams")]
    [InlineData("muda pro chrome", "Google Chrome")]
    [InlineData("switch to spotify", "Spotify")]
    public void SwitchesToAnApp(string sentence, string app) =>
        Assert.Equal([new Command.OpenApp(app)], Say(sentence));

    [Fact]
    public void SnapsNamedAppsSideBySide() =>
        Assert.Equal(
            [new Command.Control(SystemAction.SnapLeft, App: "Google Chrome"), new Command.Control(SystemAction.SnapRight, App: "Bloco de notas")],
            Say("chrome na esquerda e bloco de notas na direita", typed: true));

    [Fact]
    public void SendsANamedAppToTheOtherMonitor() =>
        Assert.Equal([new Command.Control(SystemAction.OtherMonitor, App: "Microsoft Teams")], Say("manda o teams pro outro monitor"));

    [Theory]
    [InlineData("chrome on the left", SystemAction.SnapLeft, "Google Chrome")]
    [InlineData("send teams to the other monitor", SystemAction.OtherMonitor, "Microsoft Teams")]
    [InlineData("coloca o bloco de notas na direita", SystemAction.SnapRight, "Bloco de notas")]
    public void SnapsInEitherLanguage(string sentence, SystemAction action, string app) =>
        Assert.Equal([new Command.Control(action, App: app)], Say(sentence, typed: true));

    [Fact]
    public void OpeningAnAppOnASideJustOpensIt() =>
        Assert.Equal([new Command.OpenApp("Google Chrome")], Say("abre o chrome"));

    [Fact]
    public void EnglishQuestions()
    {
        Assert.Equal("48", Assert.IsType<Command.Answer>(Assert.Single(Say("what is 15 percent of 320", typed: true))).Value);
        Assert.Equal([new Command.OpenRecent("pdf")], Say("open the latest pdf"));
    }

    [Theory]
    [InlineData("abre a pasta downloads", "downloads")]
    [InlineData("abre os downloads", "downloads")]
    [InlineData("abre a pasta de projetos", "projetos")]
    [InlineData("abre a área de trabalho", "desktop")]
    [InlineData("open the documents", "documents")]
    public void OpensFolders(string sentence, string folder) =>
        Assert.Equal([new Command.OpenFolder(folder)], Say(sentence));

    [Theory]
    [InlineData("abre o último pdf", "pdf")]
    [InlineData("abre a última planilha", "excel")]
    [InlineData("abre o último arquivo baixado", "download")]
    [InlineData("open the latest file", "any")]
    public void OpensRecentFiles(string sentence, string kind) =>
        Assert.Equal([new Command.OpenRecent(kind)], Say(sentence));

    [Theory]
    [InlineData("quanto é 15% de 320", "48")]
    [InlineData("quanto é 15 por cento de 320", "48")]
    [InlineData("quanto é 12 vezes 8", "96")]
    [InlineData("quanto é 100 dividido por 3", "33,3333")]
    [InlineData("calcula 2 elevado a 10", "1.024")]
    [InlineData("quanto é raiz de 81", "9")]
    [InlineData("what is 7 plus 5", "12")]
    [InlineData("quanto é 2,5 mais 1,25", "3,75")]
    public void AnswersArithmetic(string sentence, string value)
    {
        var fired = Assert.Single(Say(sentence, typed: true));
        var answer = Assert.IsType<Command.Answer>(fired);
        Assert.Equal(("math", value), (answer.Kind, answer.Value));
    }

    [Theory]
    [InlineData("que horas são em Nova York", "Eastern Standard Time|Nova York")]
    [InlineData("que horas são em Detroit", "Eastern Standard Time|Detroit")]
    [InlineData("que horas são", "local")]
    [InlineData("what time is it in Tokyo", "Tokyo Standard Time|Tóquio")]
    public void AnswersTheTime(string sentence, string value)
    {
        var answer = Assert.IsType<Command.Answer>(Assert.Single(Say(sentence, typed: true)));
        Assert.Equal(("time", value), (answer.Kind, answer.Value));
    }

    [Fact]
    public void AnswersTheDate() =>
        Assert.Equal("date", Assert.IsType<Command.Answer>(Assert.Single(Say("que dia é hoje", typed: true))).Kind);

    [Theory]
    [InlineData("quanto custa um carro")]
    [InlineData("quanto tempo falta")]
    public void QuestionsItCannotAnswerDoNothing(string sentence) => Assert.Empty(Say(sentence, typed: true));

    [Fact]
    public void SpeaksAnswers()
    {
        var noonUtc = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("15% de 320 = 48", QuickAnswers.Speak(new Command.Answer("15% de 320", "math", "48"), noonUtc));
        Assert.Equal("Em Nova York são 07:00", QuickAnswers.Speak(new Command.Answer("", "time", "Eastern Standard Time|Nova York"), noonUtc));
        Assert.Equal("Em Tóquio são 21:00", QuickAnswers.Speak(new Command.Answer("", "time", "Tokyo Standard Time|Tóquio"), noonUtc));
    }

    [Fact]
    public void ArithmeticFormatsTheBrazilianWay()
    {
        Assert.Equal("1.234,5", Arithmetic.Format(1234.5));
        Assert.Null(Arithmetic.Evaluate(["abc"]));
    }
}
