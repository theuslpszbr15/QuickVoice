using QuickVoice.Core;

namespace QuickVoice.Core.Tests;

public class ControlTests
{
    private static readonly string[] Apps = ["Bloco de notas", "Google Chrome", "Spotify"];

    private static List<Command> Say(string sentence, LocalDecider? decider = null, bool typed = false)
    {
        decider ??= new LocalDecider();
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
    [InlineData("fecha isso", SystemAction.CloseWindow, null)]
    [InlineData("fecha", SystemAction.CloseWindow, null)]
    [InlineData("fecha a aba", SystemAction.CloseTab, null)]
    [InlineData("minimiza a janela", SystemAction.Minimize, null)]
    [InlineData("mostra a área de trabalho", SystemAction.ShowDesktop, null)]
    [InlineData("aumenta o volume", SystemAction.VolumeUp, null)]
    [InlineData("abaixa o som", SystemAction.VolumeDown, null)]
    [InlineData("volume 30", SystemAction.VolumeSet, 30)]
    [InlineData("coloca o volume em cinquenta", SystemAction.VolumeSet, 50)]
    [InlineData("volume em 80%", SystemAction.VolumeSet, 80)]
    [InlineData("próxima música", SystemAction.NextTrack, null)]
    [InlineData("pausa a música", SystemAction.PlayPause, null)]
    [InlineData("tira um print", SystemAction.Screenshot, null)]
    [InlineData("bloqueia o computador", SystemAction.Lock, null)]
    [InlineData("troca de janela", SystemAction.SwitchWindow, null)]
    [InlineData("mute", SystemAction.Mute, null)]
    [InlineData("close the window", SystemAction.CloseWindow, null)]
    [InlineData("next track", SystemAction.NextTrack, null)]
    [InlineData("volume up", SystemAction.VolumeUp, null)]
    [InlineData("take a screenshot", SystemAction.Screenshot, null)]
    [InlineData("lock the computer", SystemAction.Lock, null)]
    [InlineData("pause the music", SystemAction.PlayPause, null)]
    [InlineData("show desktop", SystemAction.ShowDesktop, null)]
    [InlineData("start dictation", SystemAction.DictationStart, null)]
    [InlineData("modo ditado", SystemAction.DictationStart, null)]
    [InlineData("fim do ditado", SystemAction.DictationStop, null)]
    public void ControlsTheSystem(string sentence, SystemAction action, int? value) =>
        Assert.Equal([new Command.Control(action, value)], Say(sentence));

    [Fact]
    public void ChainsAnAppThenAControl() =>
        Assert.Equal([new Command.OpenApp("Spotify"), new Command.Control(SystemAction.VolumeUp)], Say("abre o spotify e aumenta o volume"));

    [Fact]
    public void ChainsInEnglish() =>
        Assert.Equal([new Command.OpenApp("Spotify"), new Command.Control(SystemAction.VolumeUp)], Say("open spotify and volume up"));

    [Fact]
    public void TypesEnglishPunctuation() =>
        Assert.Equal([new Command.TypeText("hi, how are you?")], Say("type hi comma how are you question mark"));

    [Fact]
    public void ChainsAControlThenAnApp() =>
        Assert.Equal([new Command.Control(SystemAction.CloseWindow), new Command.OpenApp("Google Chrome")], Say("fecha isso e abre o chrome", typed: true));

    [Fact]
    public void TypingWordsThatSoundLikeAControlTypesThem() =>
        Assert.Equal([new Command.TypeText("aumenta o volume")], Say("digita aumenta o volume"));

    [Theory]
    [InlineData("clica em salvar", "salvar")]
    [InlineData("clica no botão enviar", "enviar")]
    [InlineData("click on save as", "save as")]
    [InlineData("click on the send button", "send")]
    public void ClicksByName(string sentence, string target) =>
        Assert.Equal([new Command.Click(target)], Say(sentence));

    [Theory]
    [InlineData("meu projeto")]
    [InlineData("abre meu projeto")]
    [InlineData("abre o meu projeto")]
    public void RunsTheUsersShortcut(string sentence)
    {
        var decider = new LocalDecider { Shortcuts = [("projeto", ["meu projeto"])] };
        Assert.Equal([new Command.Shortcut("projeto")], Say(sentence, decider));
    }

    [Fact]
    public void AShortcutChainsWithOtherCommands()
    {
        var decider = new LocalDecider { Shortcuts = [("bom dia", ["bom dia equipe"])] };
        Assert.Equal([new Command.OpenApp("Bloco de notas"), new Command.Shortcut("bom dia")],
                     Say("abre o bloco de notas e bom dia equipe", decider, typed: true));
    }

    [Theory]
    [InlineData("oi vírgula tudo bem ponto de interrogação", false, "oi, tudo bem?")]
    [InlineData("olá ponto tudo certo exclamação", true, "Olá. Tudo certo!")]
    [InlineData("primeira linha nova linha segunda", true, "Primeira linha\nSegunda")]
    [InlineData("hello comma world period", true, "Hello, world.")]
    [InlineData("ele disse abre aspas oi fecha aspas", false, "ele disse \u201coi\u201d")]
    public void FormatsSpokenPunctuation(string spoken, bool capitalize, string written) =>
        Assert.Equal(written, Dictation.Format(spoken, capitalize));

    [Fact]
    public void TypedCommandsKeepALoneDotAsAWord() =>
        Assert.Equal([new Command.TypeText("meusite ponto com")], Say("digita meusite ponto com"));

    [Fact]
    public void TypedCommandsUnderstandPunctuation() =>
        Assert.Equal([new Command.TypeText("oi, tudo bem?")], Say("digita oi vírgula tudo bem ponto de interrogação"));

    [Fact]
    public void ControlDetailRoundTrips()
    {
        Assert.Equal(new Command.Control(SystemAction.VolumeSet, 40), Controls.Decode(Controls.Encode(SystemAction.VolumeSet, 40)));
        Assert.Null(Controls.Decode("nope"));
    }
}
