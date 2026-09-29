using QuickVoice.Core;

namespace QuickVoice.Core.Tests;

public class EngineTests
{
    private static Decision D(ActionKind action, double confidence = 1, string? app = null, double appP = 1, string? arg = null,
                              double opensApp = 0) =>
        new() { Action = action, Confidence = confidence, App = app, AppProbability = app is null ? 0 : appP, Argument = arg, OpensApp = opensApp };

    private static Command Open(string app) => new Command.OpenApp(app);
    private static readonly Command NewItem = new Command.NewItem();
    private static Command Url(string url) => new Command.OpenUrl(new Uri(url));

    [Fact]
    public void ClosedActionFiresAfterTwoAgreeingPartials()
    {
        var engine = new Engine();
        var r1 = engine.Hear("open the notes")!;
        Assert.Null(engine.Receive(D(ActionKind.OpenApp, app: "Notes"), r1).Command);
        var r2 = engine.Hear("open the notes app")!;
        Assert.Equal(Open("Notes"), engine.Receive(D(ActionKind.OpenApp, app: "Notes"), r2).Command);
    }

    [Fact]
    public void ShakyPartialResetsTheStreak()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.OpenApp, app: "Notes"), engine.Hear("open notes")!);
        engine.Receive(D(ActionKind.OpenApp, 0.5, app: "Notes"), engine.Hear("open notes for")!);
        Assert.Null(engine.Receive(D(ActionKind.OpenApp, app: "Notes"), engine.Hear("open notes for me")!).Command);
    }

    [Fact]
    public void ChangingTargetRestartsTheStreak()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.OpenApp, app: "Notes"), engine.Hear("open notes")!);
        Assert.Null(engine.Receive(D(ActionKind.OpenApp, app: "Safari"), engine.Hear("open notes no safari")!).Command);
    }

    [Fact]
    public void UnsureAppNeverFiresEarly()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.OpenApp, app: "Notes", appP: 0.6), engine.Hear("open no")!);
        Assert.Null(engine.Receive(D(ActionKind.OpenApp, app: "Notes", appP: 0.6), engine.Hear("open no tes")!).Command);
    }

    [Fact]
    public void SureAppOpensAtLowerConfidence()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.OpenApp, 0.81, app: "Notes"), engine.Hear("abre as notas")!);
        Assert.Equal(Open("Notes"), engine.Receive(D(ActionKind.OpenApp, 0.82, app: "Notes"), engine.Hear("abre as notas e")!).Command);
    }

    [Fact]
    public void AppBelowSureWaitsForThePause()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.OpenApp, app: "Google Chrome", appP: 0.9), engine.Hear("abre o google")!);
        Assert.Null(engine.Receive(D(ActionKind.OpenApp, app: "Google Chrome", appP: 0.9), engine.Hear("abre o google chrome")!).Command);
        Assert.Equal(Open("Google Chrome"), engine.Pause().Command);
    }

    [Fact]
    public void YesNoOpensAppFiresWhenASecondCommandSplitsTheChoice()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.TypeText, 0.5, app: "Notes", opensApp: 0.93), engine.Hear("abre as notas e digita")!);
        Assert.Equal(Open("Notes"),
            engine.Receive(D(ActionKind.TypeText, 0.46, app: "Notes", opensApp: 0.91), engine.Hear("abre as notas e digita eu")!).Command);
    }

    [Fact]
    public void YesNoOpensAppStillNeedsASureApp()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.None, app: "Notes", appP: 0.7, opensApp: 0.95), engine.Hear("abre as no")!);
        Assert.Null(engine.Receive(D(ActionKind.None, app: "Notes", appP: 0.7, opensApp: 0.95), engine.Hear("abre as not")!).Command);
    }

    [Fact]
    public void NewItemStillNeedsHighConfidence()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.NewItem, 0.81), engine.Hear("cria uma")!);
        Assert.Null(engine.Receive(D(ActionKind.NewItem, 0.82), engine.Hear("cria uma nota")!).Command);
    }

    [Fact]
    public void NewItemFiresEarlyWithoutAnApp()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.NewItem), engine.Hear("create a new")!);
        Assert.Equal(NewItem, engine.Receive(D(ActionKind.NewItem, app: "Notes"), engine.Hear("create a new note")!).Command);
    }

    [Fact]
    public void NewItemFiredBeforeItsLastWordsFiresOnce()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.NewItem, 0.86), engine.Hear("cria")!);
        Assert.Equal(NewItem, engine.Receive(D(ActionKind.NewItem, 0.95), engine.Hear("cria uma")!).Command);
        var rest = engine.Hear("cria uma nota nova")!;
        engine.Pause();
        Assert.Null(engine.Receive(D(ActionKind.NewItem), rest).Command);
    }

    [Fact]
    public void TheSameCommandAfterAConnectiveStillFires()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.NewItem), engine.Hear("cria uma")!);
        Assert.Equal(NewItem, engine.Receive(D(ActionKind.NewItem), engine.Hear("cria uma nota")!).Command);
        engine.Receive(D(ActionKind.NewItem), engine.Hear("cria uma nota e cria outra")!);
        Assert.Equal(NewItem, engine.Receive(D(ActionKind.NewItem), engine.Hear("cria uma nota e cria outra nota")!).Command);
    }

    [Fact]
    public void WordsAfterTheFiredCommandAreSkippedUpToTheNextOne()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.NewItem), engine.Hear("cria uma")!);
        Assert.Equal(NewItem, engine.Receive(D(ActionKind.NewItem), engine.Hear("cria uma nota")!).Command);
        engine.Receive(D(ActionKind.NewItem), engine.Hear("cria uma nota nova")!);
        var step = engine.Receive(D(ActionKind.NewItem), engine.Hear("cria uma nota nova e digita oi")!);
        Assert.Null(step.Command);
        var next = step.Request!;
        Assert.Equal(["e", "digita", "oi"], next.Tail);
        engine.Pause();
        Assert.Equal(new Command.TypeText("oi"), engine.Receive(D(ActionKind.TypeText, arg: "oi"), next).Command);
    }

    [Fact]
    public void OpenTextWaitsForThePause()
    {
        var engine = new Engine();
        Assert.Null(engine.Receive(D(ActionKind.WebSearch, arg: "norbert"), engine.Hear("google norbert")!).Command);
        Assert.Null(engine.Receive(D(ActionKind.WebSearch, arg: "norbert wiener"), engine.Hear("google norbert wiener")!).Command);
        Assert.Equal(new Command.WebSearch("norbert wiener"), engine.Pause().Command);
    }

    [Fact]
    public void PauseBeforeTheAnswerFiresWhenItArrives()
    {
        var engine = new Engine();
        var request = engine.Hear("type hello")!;
        Assert.Null(engine.Pause().Command);
        Assert.Equal(new Command.TypeText("hello"), engine.Receive(D(ActionKind.TypeText, arg: "hello"), request).Command);
    }

    [Fact]
    public void PauseStillNeedsConfidenceAndArgument()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.WebSearch, 0.6, arg: "lisbon"), engine.Hear("search lisbon")!);
        Assert.Null(engine.Pause().Command);
        engine.Receive(D(ActionKind.WebSearch), engine.Hear("search lisbon um")!);
        Assert.Null(engine.Pause().Command);
        engine.Receive(D(ActionKind.OpenApp, app: "Notes", appP: 0.5), engine.Hear("search lisbon um notes")!);
        Assert.Null(engine.Pause().Command);
    }

    [Fact]
    public void SameOutcomeSplitStillFiresAtThePause()
    {
        var engine = new Engine();
        var split = new Decision
        {
            Action = ActionKind.OpenUrl, Confidence = 0.66, Argument = "LinkedIn",
            ActionProbabilities = new Dictionary<ActionKind, double> { [ActionKind.OpenUrl] = 0.72, [ActionKind.OpenApp] = 0.15, [ActionKind.WebSearch] = 0.13 },
        };
        engine.Receive(split, engine.Hear("abre o LinkedIn no Google por favor")!);
        Assert.Equal(Url("https://linkedin.com"), engine.Pause().Command);
    }

    [Fact]
    public void ASiteThatIsNotAnAddressBecomesASearch()
    {
        var engine = new Engine();
        var split = new Decision
        {
            Action = ActionKind.OpenUrl, Confidence = 0.4, Argument = "receita de bolo",
            ActionProbabilities = new Dictionary<ActionKind, double> { [ActionKind.OpenUrl] = 0.45, [ActionKind.WebSearch] = 0.35, [ActionKind.None] = 0.2 },
        };
        engine.Receive(split, engine.Hear("abre receita de bolo")!);
        Assert.Equal(new Command.WebSearch("receita de bolo"), engine.Pause().Command);
    }

    [Fact]
    public void HowOrWhereBelongsToTheCommand()
    {
        Assert.Equal(7, Engine.WordsUsed(Url("https://linkedin.com"), "LinkedIn", [],
            ["abre", "o", "LinkedIn", "no", "Google", "por", "favor"]));
        Assert.Equal(6, Engine.WordsUsed(Open("Notes"), null, ["Notes"],
            ["open", "the", "notes", "app", "for", "me", "and", "type", "hi"]));
        Assert.Equal(5, Engine.WordsUsed(Open("Terminal"), null, ["Terminal"],
            ["abre", "o", "terminal", "no", "mac", "digita", "ls"]));
    }

    [Fact]
    public void ANamedBrowserJoinsTheWebOutcome()
    {
        var engine = new Engine { Browsers = new HashSet<string> { "Google Chrome" } };
        var split = new Decision
        {
            Action = ActionKind.OpenUrl, Confidence = 0.36, App = "Google Chrome", AppProbability = 0.7, Argument = "linkedin",
            ActionProbabilities = new Dictionary<ActionKind, double>
                { [ActionKind.OpenUrl] = 0.45, [ActionKind.OpenApp] = 0.3, [ActionKind.WebSearch] = 0.15, [ActionKind.None] = 0.1 },
        };
        engine.Receive(split, engine.Hear("abre o linkedin pelo google")!);
        Assert.Equal(Url("https://linkedin.com"), engine.Pause().Command);
    }

    [Fact]
    public void TheSiteBeatsJustOpeningTheBrowser()
    {
        var engine = new Engine { Browsers = new HashSet<string> { "Google Chrome" } };
        var split = new Decision
        {
            Action = ActionKind.OpenApp, Confidence = 0.4, App = "Google Chrome", AppProbability = 0.8, Argument = "LinkedIn",
            ActionProbabilities = new Dictionary<ActionKind, double>
                { [ActionKind.OpenApp] = 0.45, [ActionKind.OpenUrl] = 0.35, [ActionKind.WebSearch] = 0.1, [ActionKind.None] = 0.1 },
        };
        engine.Receive(split, engine.Hear("abre o linkedin no google por favor")!);
        Assert.Equal(Url("https://linkedin.com"), engine.Pause().Command);
    }

    [Fact]
    public void NamingOnlyTheBrowserOpensIt()
    {
        var engine = new Engine { Browsers = new HashSet<string> { "Google Chrome" } };
        var decision = new Decision
        {
            Action = ActionKind.OpenApp, Confidence = 0.6, App = "Google Chrome", AppProbability = 0.8, Argument = "google",
            ActionProbabilities = new Dictionary<ActionKind, double> { [ActionKind.OpenApp] = 0.6, [ActionKind.OpenUrl] = 0.3, [ActionKind.None] = 0.1 },
        };
        engine.Receive(decision, engine.Hear("abre o google")!);
        Assert.Equal(Open("Google Chrome"), engine.Pause().Command);
    }

    [Fact]
    public void DifferentOutcomesDoNotAddUp()
    {
        var engine = new Engine();
        var split = new Decision
        {
            Action = ActionKind.TypeText, Confidence = 0.4, Argument = "lista de compras",
            ActionProbabilities = new Dictionary<ActionKind, double> { [ActionKind.TypeText] = 0.45, [ActionKind.NewItem] = 0.45, [ActionKind.None] = 0.1 },
        };
        engine.Receive(split, engine.Hear("escreve lista de compras")!);
        Assert.Null(engine.Pause().Command);
    }

    [Fact]
    public void PauseFiresAShortClosedCommand()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.OpenApp, app: "Notes"), engine.Hear("notes")!);
        Assert.Equal(Open("Notes"), engine.Pause().Command);
    }

    [Fact]
    public void SpokenSiteBecomesUrl()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.OpenUrl, arg: "x dot com"), engine.Hear("open x dot com")!);
        Assert.Equal(Url("https://x.com"), engine.Pause().Command);
    }

    [Fact]
    public void NoneNeverFires()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.None), engine.Hear("can you")!);
        Assert.Null(engine.Receive(D(ActionKind.None), engine.Hear("can you um")!).Command);
        Assert.Null(engine.Pause().Command);
    }

    [Fact]
    public void FiredWordsAreConsumedSoTheNextCommandChains()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.OpenApp, app: "Notes"), engine.Hear("open notes")!);
        var step = engine.Receive(D(ActionKind.OpenApp, app: "Notes"), engine.Hear("open notes and")!);
        Assert.Equal(Open("Notes"), step.Command);
        Assert.Equal(["and"], step.Request!.Tail);  // the open ends at the app's name
        Assert.Equal(["and", "create", "a", "new", "note"], engine.Hear("open notes and create a new note")!.Tail);
    }

    [Fact]
    public void FireSendsTheRemainingTailRightAway()
    {
        var engine = new Engine();
        var r1 = engine.Hear("open notes")!;
        var r2 = engine.Hear("open notes app")!;
        var r3 = engine.Hear("open notes app new note")!;
        engine.Receive(D(ActionKind.OpenApp, app: "Notes"), r1);
        var step = engine.Receive(D(ActionKind.OpenApp, app: "Notes"), r2);
        Assert.Equal(Open("Notes"), step.Command);
        Assert.Equal(["new", "note"], step.Request!.Tail);
        Assert.Null(engine.Receive(D(ActionKind.OpenApp, app: "Notes"), r3).Command);
    }

    [Fact]
    public void FollowUpSentWhileSilentIsJudgedAsPaused()
    {
        var engine = new Engine();
        var r1 = engine.Hear("open notes")!;
        var r2 = engine.Hear("open notes app")!;
        engine.Hear("open notes app type hi");
        engine.Pause();
        engine.Receive(D(ActionKind.OpenApp, app: "Notes"), r1);
        var step = engine.Receive(D(ActionKind.OpenApp, app: "Notes"), r2);
        Assert.Equal(Open("Notes"), step.Command);
        var followUp = step.Request!;
        Assert.Equal(["type", "hi"], followUp.Tail);
        Assert.Equal(new Command.TypeText("hi"), engine.Receive(D(ActionKind.TypeText, arg: "hi"), followUp).Command);
    }

    [Fact]
    public void OpenTextKeepsTheCommandChainedAfterIt()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.WebSearch, arg: "cake recipes"), engine.Hear("search cake recipes and open notes")!);
        var step = engine.Pause();
        Assert.Equal(new Command.WebSearch("cake recipes"), step.Command);
        var followUp = step.Request!;
        Assert.Equal(["and", "open", "notes"], followUp.Tail);
        Assert.Equal(Open("Notes"), engine.Receive(D(ActionKind.OpenApp, app: "Notes"), followUp).Command);
    }

    [Fact]
    public void ArgumentAtTheEndUsesTheWholeTail()
    {
        var engine = new Engine();
        engine.Receive(D(ActionKind.WebSearch, arg: "norbert wiener"), engine.Hear("google norbert wiener")!);
        Assert.Null(engine.Pause().Request);
    }

    [Fact]
    public void AppCommandSaidAtOnceKeepsTheNextCommand()
    {
        var engine = new Engine { AppAliases = new Dictionary<string, List<string>> { ["Notes"] = ["Notes", "Notas"] } };
        var request = engine.Hear("abre as notas e digita oi")!;
        engine.Pause();
        var step = engine.Receive(D(ActionKind.OpenApp, app: "Notes"), request);
        Assert.Equal(Open("Notes"), step.Command);
        Assert.Equal(["e", "digita", "oi"], step.Request!.Tail);
    }

    [Fact]
    public void AppNamedByItsFirstWordStillBoundsTheCommand() =>
        Assert.Equal(3, Engine.WordsUsed(Open("Google Chrome"), null, ["Google Chrome"], ["abre", "o", "google", "e", "pesquisa", "bolo"]));

    [Fact]
    public void AccentsDoNotHideTheApp() =>
        Assert.Equal(4, Engine.WordsUsed(Open("Calendar"), null, ["Calendar", "Calendário"], ["abre", "o", "calendario", "agora"]));

    [Fact]
    public void UnknownAppMentionUsesTheWholeTail() =>
        Assert.Equal(5, Engine.WordsUsed(Open("Visual Studio Code"), null, ["Visual Studio Code"], ["abre", "o", "vscode", "e", "digita"]));

    [Fact]
    public void LateOlderAnswerIsIgnored()
    {
        var engine = new Engine();
        var r1 = engine.Hear("open notes")!;
        var r2 = engine.Hear("open notes app")!;
        engine.Receive(D(ActionKind.OpenApp, app: "Notes"), r2);
        Assert.Null(engine.Receive(D(ActionKind.OpenApp, app: "Notes"), r1).Command);
        Assert.Equal(Open("Notes"), engine.Receive(D(ActionKind.OpenApp, app: "Notes"), engine.Hear("open notes app please")!).Command);
    }

    [Fact]
    public void UnchangedTranscriptSendsNothing()
    {
        var engine = new Engine();
        Assert.Null(engine.Hear(""));
        Assert.NotNull(engine.Hear("open"));
        Assert.Null(engine.Hear("open"));
    }

    [Fact]
    public void ResetStartsAFreshUtterance()
    {
        var engine = new Engine();
        var old = engine.Hear("open notes")!;
        engine.Reset();
        Assert.Null(engine.Receive(D(ActionKind.OpenApp, app: "Notes"), old).Command);
        Assert.Equal(["open", "safari"], engine.Hear("open safari")!.Tail);
        Assert.Equal(0, engine.Consumed);
    }
}
