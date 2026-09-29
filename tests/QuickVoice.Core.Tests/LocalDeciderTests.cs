using QuickVoice.Core;

namespace QuickVoice.Core.Tests;

public class LocalDeciderTests
{
    private static readonly string[] Apps =
    [
        "Bloco de notas", "Calculadora", "Explorador de Arquivos", "Google Chrome", "Microsoft Edge", "Microsoft Teams",
        "Spotify", "Terminal", "Visual Studio Code", "Outlook (new)",
    ];

    /// <summary>Speaks the sentence word by word through the engine like the app does, then pauses.</summary>
    private static List<Command> Say(string sentence)
    {
        var decider = new LocalDecider();
        var engine = new Engine { Browsers = new HashSet<string> { "Google Chrome", "Microsoft Edge" } };
        var fired = new List<Command>();
        var words = Vocabulary.Words(sentence);

        void Run(Engine.Step step)
        {
            if (step.Command is { } c) fired.Add(c);
            if (step.Request is { } r) Run(engine.Receive(decider.Decide(r.Text, Apps), r));
        }

        for (var n = 1; n <= words.Length; n++)
        {
            if (engine.Hear(string.Join(' ', words.Take(n))) is { } request)
                Run(engine.Receive(decider.Decide(request.Text, Apps), request));
        }
        Run(engine.Pause());
        return fired;
    }

    [Theory]
    [InlineData("abre o bloco de notas", "Bloco de notas")]
    [InlineData("abre o notepad", "Bloco de notas")]
    [InlineData("abre o chrome", "Google Chrome")]
    [InlineData("abre a calculadora", "Calculadora")]
    [InlineData("abre o spotify por favor", "Spotify")]
    [InlineData("open visual studio code", "Visual Studio Code")]
    [InlineData("abre o teams", "Microsoft Teams")]
    public void OpensTheNamedApp(string sentence, string app) =>
        Assert.Equal([new Command.OpenApp(app)], Say(sentence));

    [Fact]
    public void AnAppOpensBeforeTheSentenceEnds()
    {
        var decider = new LocalDecider();
        var engine = new Engine();
        engine.Receive(decider.Decide("abre o bloco", Apps), engine.Hear("abre o bloco")!);
        var step = engine.Receive(decider.Decide("abre o bloco de", Apps), engine.Hear("abre o bloco de")!);
        Assert.Equal(new Command.OpenApp("Bloco de notas"), step.Command);
    }

    [Fact]
    public void ChainsOpenThenType() =>
        Assert.Equal([new Command.OpenApp("Bloco de notas"), new Command.TypeText("bom dia")], Say("abre o bloco de notas e digita bom dia"));

    [Fact]
    public void TypesWhatWasSaid() => Assert.Equal([new Command.TypeText("olá mundo")], Say("digita olá mundo"));

    [Theory]
    [InlineData("pesquisa receita de pão de queijo", "receita de pão de queijo")]
    [InlineData("pesquisa no google receita de bolo", "receita de bolo")]
    [InlineData("procura o horário do jogo", "horário do jogo")]
    [InlineData("search norbert wiener", "norbert wiener")]
    [InlineData("Pesquise no google sobre o dolar", "dolar")]
    [InlineData("Pesquise no Google sobre o dólar.", "dólar")]
    [InlineData("procure sobre a cotação do dólar hoje", "cotação do dólar hoje")]
    public void SearchesTheWeb(string sentence, string query) =>
        Assert.Equal([new Command.WebSearch(query)], Say(sentence));

    [Theory]
    [InlineData("abre o youtube", "https://youtube.com")]
    [InlineData("entra no linkedin", "https://linkedin.com")]
    [InlineData("vai no github", "https://github.com")]
    [InlineData("abre o linkedin no google", "https://linkedin.com")]
    [InlineData("open x dot com", "https://x.com")]
    public void OpensSites(string sentence, string url) =>
        Assert.Equal([new Command.OpenUrl(new Uri(url))], Say(sentence));

    [Fact]
    public void ChainsOpenThenSearch() =>
        Assert.Equal([new Command.OpenApp("Google Chrome"), new Command.WebSearch("receita de bolo")], Say("abre o chrome e pesquisa receita de bolo"));

    /// <summary>Write mode: the whole sentence arrives at once, then the pause.</summary>
    private static List<Command> Type(string sentence)
    {
        var decider = new LocalDecider();
        var aliases = LocalDecider.WithSpokenNames(Apps.ToDictionary(a => a, a => new List<string> { a }));
        var engine = new Engine { AppAliases = aliases, Browsers = new HashSet<string> { "Google Chrome", "Microsoft Edge" } };
        var fired = new List<Command>();

        void Run(Engine.Step step)
        {
            if (step.Command is { } c) fired.Add(c);
            if (step.Request is { } r) Run(engine.Receive(decider.Decide(r.Text, Apps), r));
        }

        var request = engine.Hear(sentence)!;
        Run(engine.Receive(decider.Decide(request.Text, Apps), request));
        Run(engine.Pause());
        return fired;
    }

    [Fact]
    public void TypedChainsKeepEveryCommand()
    {
        Assert.Equal([new Command.OpenApp("Google Chrome"), new Command.WebSearch("cotação do euro")], Type("abre o chrome e pesquisa cotação do euro"));
        Assert.Equal([new Command.OpenApp("Bloco de notas"), new Command.TypeText("oi")], Type("abre o notepad e digita oi"));
        Assert.Equal([new Command.WebSearch("dolar")], Type("Pesquise no google sobre o dolar"));
    }

    [Theory]
    [InlineData("cria uma nota nova")]
    [InlineData("abre uma nova aba")]
    [InlineData("new tab")]
    public void CreatesANewItem(string sentence) => Assert.Equal([new Command.NewItem()], Say(sentence));

    [Theory]
    [InlineData("então tá bom")]
    [InlineData("obrigado")]
    [InlineData("abre")]
    public void SmallTalkDoesNothing(string sentence) => Assert.Empty(Say(sentence));
}
