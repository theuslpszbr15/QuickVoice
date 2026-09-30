namespace QuickVoice.Core;

/// <summary>
/// A free, offline stand-in for Jev: fixed Portuguese and English phrasings instead of a model.
/// It answers in the same shape (actions with probabilities, an app, an argument span), so the
/// engine still acts mid-sentence on closed actions and waits for the pause on open ones.
/// </summary>
public sealed class LocalDecider : IDecider
{
    private static readonly HashSet<string> OpenVerbs =
        ["abre", "abrir", "abra", "abri", "open", "launch", "inicia", "iniciar", "inicie", "executa", "executar", "execute", "roda", "rodar", "liga", "start"];
    private static readonly HashSet<string> GoVerbs =
        ["vai", "va", "ir", "entra", "entrar", "entre", "acessa", "acessar", "acesse", "visita", "visitar", "go", "visit",
         "muda", "mude", "troca", "troque", "alterna", "alterne", "volta", "volte", "switch"];
    private static readonly HashSet<string> CloseVerbs =
        ["fecha", "fechar", "feche", "close", "encerra", "encerrar", "encerre", "quit", "sai", "sair", "saia", "exit", "kill"];
    private static readonly HashSet<string> PutVerbs =
        ["coloca", "coloque", "joga", "jogue", "manda", "mande", "poe", "ponha", "move", "mova", "leva", "leve", "put", "snap", "send"];
    private static readonly Dictionary<string, SystemAction> Sides = new()
    {
        ["esquerda"] = SystemAction.SnapLeft, ["left"] = SystemAction.SnapLeft, ["direita"] = SystemAction.SnapRight, ["right"] = SystemAction.SnapRight,
    };
    /// <summary>Folders everyone has, by what people call them.</summary>
    private static readonly Dictionary<string, string> KnownFolders = new()
    {
        ["downloads"] = "downloads", ["download"] = "downloads", ["documentos"] = "documents", ["documents"] = "documents",
        ["imagens"] = "pictures", ["fotos"] = "pictures", ["pictures"] = "pictures", ["musicas"] = "music", ["music"] = "music",
        ["videos"] = "videos", ["desktop"] = "desktop",
    };
    private static readonly HashSet<string> RecentWords = ["ultimo", "ultima", "last", "latest", "recente", "recent"];
    private static readonly HashSet<string> SearchVerbs =
        ["pesquisa", "pesquisar", "pesquise", "procura", "procurar", "procure", "busca", "buscar", "busque", "search", "google", "googla", "find", "look"];
    private static readonly HashSet<string> TypeVerbs =
        ["digita", "digitar", "digite", "escreve", "escrever", "escreva", "type", "write"];
    private static readonly HashSet<string> CreateVerbs = ["cria", "criar", "crie", "create", "nova", "novo", "new"];
    private static readonly HashSet<string> NewWords = ["nova", "novo", "new", "outra", "outro", "another"];
    private static readonly HashSet<string> ItemNouns =
        ["nota", "documento", "aba", "janela", "arquivo", "guia", "note", "document", "tab", "window", "file"];
    private static readonly HashSet<string> Articles = ["o", "a", "os", "as", "um", "uma", "the", "an", "meu", "minha", "my"];
    /// <summary>Said between a "go" verb and the place: "vai | no | youtube", "go | to | github".</summary>
    private static readonly HashSet<string> GoLinks = ["no", "na", "nos", "nas", "pro", "pra", "para", "em", "ao", "to", "on", "o", "a", "the"];
    /// <summary>Framing before a search query: "pesquisa | no google | receita", "search | for | cake".</summary>
    private static readonly HashSet<string> QueryLead = ["no", "na", "pelo", "pela", "o", "a", "sobre", "por", "for", "about", "on", "the", "google", "internet", "web", "up"];
    private static readonly HashSet<string> SearchPlaces = ["google", "internet", "web", "chrome", "edge", "navegador", "browser"];
    private static readonly HashSet<string> ClickVerbs = ["clica", "clicar", "clique", "click", "aperta", "apertar", "aperte", "press", "tap"];
    /// <summary>Framing before a button's name: "clica | no botão | salvar", "click | on the | save button".</summary>
    private static readonly HashSet<string> ClickLead =
        ["em", "no", "na", "o", "a", "on", "the", "botao", "button", "link", "opcao", "option", "item", "menu"];
    private static readonly HashSet<string> ClickNouns = ["botao", "button", "link", "opcao", "option"];
    /// <summary>Words in app names that do not tell apps apart ("Microsoft Edge", "Bloco de notas").</summary>
    private static readonly HashSet<string> GenericTokens =
        ["microsoft", "windows", "de", "do", "da", "dos", "das", "the", "for", "and", "e", "app", "aplicativo", "desktop", "for", "x64", "x86", "365"];

    /// <summary>What people call common Windows apps, mapped to words of the installed name.</summary>
    private static readonly Dictionary<string, string> SpokenAliases = new()
    {
        ["notepad"] = "bloco de notas", ["notas"] = "bloco de notas", ["bloco"] = "bloco de notas",
        ["calculator"] = "calculadora", ["explorer"] = "explorador de arquivos", ["arquivos"] = "explorador de arquivos",
        ["explorador"] = "explorador de arquivos", ["settings"] = "configuracoes", ["configuracao"] = "configuracoes",
        ["cmd"] = "prompt de comando", ["prompt"] = "prompt de comando", ["vscode"] = "visual studio code",
        ["code"] = "visual studio code", ["chrome"] = "google chrome", ["edge"] = "microsoft edge",
    };

    private IReadOnlyList<string>? indexedApps;
    private List<(string Name, string[] Tokens, string Normalized)> appIndex = [];
    private List<(string Name, string[] Words)> shortcutPhrases = [];

    /// <summary>The user's own commands: a name and the phrases that say it ("meu projeto", "abre meu projeto").</summary>
    public IReadOnlyList<(string Name, IReadOnlyList<string> Phrases)> Shortcuts
    {
        set => shortcutPhrases = value
            .SelectMany(s => s.Phrases.Select(p => (s.Name, Words: Vocabulary.Words(p).Select(Vocabulary.Normalized).Where(w => w.Length > 0).ToArray())))
            .Where(s => s.Words.Length > 0)
            .OrderByDescending(s => s.Words.Length)
            .ToList();
    }

    /// <summary>Adds the nicknames people say ("notepad", "vscode") to each installed app's names.</summary>
    public static Dictionary<string, List<string>> WithSpokenNames(IReadOnlyDictionary<string, List<string>> aliases)
    {
        var result = aliases.ToDictionary(a => a.Key, a => a.Value.ToList());
        foreach (var (spoken, target) in SpokenAliases)
        {
            var app = result.Keys.FirstOrDefault(name =>
            {
                var n = string.Join(' ', Vocabulary.Words(name).Select(Vocabulary.Normalized));
                return n == target || n.StartsWith(target + " ");
            });
            if (app is not null && !result[app].Contains(spoken)) result[app].Add(spoken);
        }
        return result;
    }

    public Task<(Decision Decision, int Tokens)> DecideAsync(string tail, string frontmost, IReadOnlyList<string> apps,
                                                             IReadOnlyList<string> spans, CancellationToken cancel = default) =>
        Task.FromResult((Decide(tail, apps), 0));

    public Decision Decide(string tail, IReadOnlyList<string> apps)
    {
        var words = Vocabulary.Words(tail).Select(Vocabulary.TrimPunctuation).Where(w => w.Length > 0).ToArray();
        var norm = words.Select(Vocabulary.Normalized).ToArray();
        var raw = Vocabulary.Words(tail).Where(w => Vocabulary.TrimPunctuation(w).Length > 0).ToArray();  // keeps "15%" for arithmetic
        Index(apps);
        // The first command said wins; a user's shortcut beats a built-in reading of the same words.
        for (var i = 0; i < norm.Length; i++)
        {
            if (ShortcutAt(norm, i) is { } shortcut)
                return Act(ActionKind.Shortcut, 0.95, argument: Join(words, Enumerable.Range(i, shortcut.Length)), detail: shortcut.Name);
            if (QuickAnswers.Match(raw, norm, i) is { } answer)
                return Act(ActionKind.Answer, 0.95, argument: answer.Question, detail: $"{answer.Kind}:{answer.Value}");
            var clauseEnd = ClauseEnd(norm, i);
            if (SnapAt(norm, i, clauseEnd) is { } snap)
                return Act(ActionKind.Control, 0.93, app: snap.App, appProbability: 0.95, argument: Join(words, Enumerable.Range(i, snap.End - i)),
                           detail: Controls.Encode(snap.Action, null));
            if (Controls.Match(norm, i, clauseEnd) is { } control)
                return Act(ActionKind.Control, 0.93, argument: Join(words, Enumerable.Range(i, control.End - i)),
                           detail: Controls.Encode(control.Action, control.Value));
            if (CloseVerbs.Contains(norm[i]))
            {
                var target = Enumerable.Range(i + 1, clauseEnd - i - 1)
                    .SkipWhile(j => Articles.Contains(norm[j]) || norm[j] is "do" or "da" or "de").ToArray();
                var (app, appP) = target.Length == 0 ? (null, 0.0) : MatchApp(target.Select(j => norm[j]).ToArray(), apps);
                if (app is not null && appP >= 0.7)
                    return Act(ActionKind.CloseApp, 0.93, app: app, appProbability: appP, argument: Join(words, Enumerable.Range(i, clauseEnd - i)));
            }
            if (ClickVerbs.Contains(norm[i]))
            {
                var target = Enumerable.Range(i + 1, clauseEnd - i - 1).SkipWhile(j => ClickLead.Contains(norm[j])).ToList();
                while (target.Count > 1 && ClickNouns.Contains(norm[target[^1]])) target.RemoveAt(target.Count - 1);  // "send | button"
                return target.Count == 0 ? Nothing(0.6) : Act(ActionKind.Click, 0.92, argument: Join(words, target));
            }
            if (IsVerb(norm[i])) return DecideVerb(words, norm, i, apps);
        }
        return Nothing();
    }

    private Decision DecideVerb(string[] words, string[] norm, int verbAt, IReadOnlyList<string> apps)
    {
        var verb = norm[verbAt];
        var end = ClauseEnd(norm, verbAt);
        var rest = Enumerable.Range(verbAt + 1, end - verbAt - 1).ToArray();

        if ((CreateVerbs.Contains(verb) || OpenVerbs.Contains(verb)) && rest.Any(i => ItemNouns.Contains(norm[i]))
            && (CreateVerbs.Contains(verb) || rest.Any(i => NewWords.Contains(norm[i]))))
            return Act(ActionKind.NewItem, 0.9);

        if (TypeVerbs.Contains(verb))
            return rest.Length == 0 ? Nothing(0.6) : Act(ActionKind.TypeText, 0.92, argument: Join(words, rest));

        if (SearchVerbs.Contains(verb))
        {
            var query = rest.SkipWhile(i => QueryLead.Contains(norm[i])).ToList();
            if (query.Count >= 2 && Vocabulary.ModifierOpeners.Contains(norm[query[^2]]) && SearchPlaces.Contains(norm[query[^1]]))
                query.RemoveRange(query.Count - 2, 2);
            return query.Count == 0 ? Nothing(0.6) : Act(ActionKind.WebSearch, 0.92, argument: Join(words, query));
        }

        if (OpenVerbs.Contains(verb) || GoVerbs.Contains(verb))
        {
            if (FolderOrRecent(words, norm, verbAt, rest) is { } place) return place;
            // "abre | meu projeto": a shortcut named right after the verb is the user's own place ("meu" may be part of its name).
            foreach (var at in rest)
            {
                if (ShortcutAt(norm, at) is { } shortcut)
                    return Act(ActionKind.Shortcut, 0.95, argument: Join(words, Enumerable.Range(verbAt, at + shortcut.Length - verbAt)), detail: shortcut.Name);
                if (!Articles.Contains(norm[at]) && !GoLinks.Contains(norm[at])) break;
            }
            var lead = GoVerbs.Contains(verb) ? GoLinks : Articles;
            var target = rest.SkipWhile(i => lead.Contains(norm[i]) || Articles.Contains(norm[i]))
                             .TakeWhile(i => !Vocabulary.ModifierOpeners.Contains(norm[i]) || norm[i - 1] is "dot" or "ponto")
                             .ToList();
            if (target.Count == 0) return Nothing(0.6);
            var (app, appP) = MatchApp(target.Select(i => norm[i]).ToArray(), apps);
            if (app is not null && appP >= 0.7)
                return Act(ActionKind.OpenApp, 0.9, app: app, appProbability: appP, opensApp: 0.95);
            return new Decision
            {
                Action = ActionKind.OpenUrl, Confidence = 0.85, Argument = Join(words, target), OpensApp = 0.05,
                ActionProbabilities = new Dictionary<ActionKind, double> { [ActionKind.OpenUrl] = 0.85, [ActionKind.WebSearch] = 0.1, [ActionKind.None] = 0.05 },
            };
        }
        return Nothing();
    }

    private static bool IsVerb(string w) =>
        OpenVerbs.Contains(w) || GoVerbs.Contains(w) || SearchVerbs.Contains(w) || TypeVerbs.Contains(w) || CreateVerbs.Contains(w);

    /// <summary>The clause ends at "e"/"and" followed by another command: "abre o chrome | e pesquisa bolo".</summary>
    private int ClauseEnd(string[] norm, int verbAt)
    {
        for (var i = verbAt + 1; i < norm.Length - 1; i++)
        {
            if (Vocabulary.Connectives.Contains(norm[i]) && StartsCommand(norm, i + 1)) return i;
        }
        return norm.Length;
    }

    private bool StartsCommand(string[] norm, int i) =>
        IsVerb(norm[i]) || ClickVerbs.Contains(norm[i]) || CloseVerbs.Contains(norm[i]) || ShortcutAt(norm, i) is not null
        || (Controls.Starters.Contains(norm[i]) && Controls.Match(norm, i, norm.Length) is not null)
        || SnapAt(norm, i, norm.Length) is not null;

    /// <summary>
    /// "chrome na esquerda", "coloca o bloco de notas na direita", "manda o teams pro outro monitor": an app named
    /// right before where its window goes. Without an app the generic controls handle it ("joga isso na esquerda").
    /// </summary>
    private (string App, SystemAction Action, int End)? SnapAt(string[] norm, int start, int clauseEnd)
    {
        var from = start;
        if (from < clauseEnd && IsVerb(norm[from]) && !PutVerbs.Contains(norm[from])) return null;  // "abre o chrome na esquerda" opens it
        if (from < clauseEnd && PutVerbs.Contains(norm[from])) from++;
        while (from < clauseEnd && Articles.Contains(norm[from])) from++;
        for (var k = from + 1; k < clauseEnd && k <= from + 5; k++)
        {
            var at = k;
            if (norm[at] is not ("na" or "pra" or "para" or "pro" or "no" or "to" or "on")) continue;
            var appWords = norm[from..k];
            at++;
            while (at < clauseEnd && norm[at] is "a" or "o" or "the") at++;
            SystemAction? action = null;
            var end = at + 1;
            if (at < clauseEnd && Sides.TryGetValue(norm[at], out var side)) action = side;
            else if (at + 1 < clauseEnd && norm[at] is "outro" or "outra" or "other" or "next" && norm[at + 1] is "monitor" or "tela" or "screen")
            {
                action = SystemAction.OtherMonitor;
                end = at + 2;
            }
            if (action is null) return null;
            var (app, p) = MatchApp(appWords, indexedApps ?? []);
            return app is not null && p >= 0.7 ? (app, action.Value, end) : null;
        }
        return null;
    }

    /// <summary>"abre a pasta projetos", "abre os downloads", "abre o último pdf".</summary>
    private static Decision? FolderOrRecent(string[] words, string[] norm, int verbAt, int[] rest)
    {
        var said = rest.SkipWhile(i => Articles.Contains(norm[i]) || GoLinks.Contains(norm[i])).ToArray();
        if (said.Length == 0) return null;
        string Span(int last) => Join(words, Enumerable.Range(verbAt, last - verbAt + 1));
        if (said.Any(i => RecentWords.Contains(norm[i])))
        {
            var kinds = said.Select(i => Recent.Nouns.GetValueOrDefault(norm[i])).OfType<string>().ToList();
            var kind = kinds.FirstOrDefault(k => k != "any") ?? kinds.FirstOrDefault();
            if (kind is not null) return Act(ActionKind.OpenRecent, 0.93, argument: Span(said[^1]), detail: kind);
        }
        if (norm[said[0]] is "pasta" or "folder")
        {
            var name = said.Skip(1).SkipWhile(i => norm[i] is "de" or "do" or "da" or "dos" or "das" or "meus" or "minhas").ToArray();
            if (name.Length == 0) return null;
            var folder = name.Length == 1 && KnownFolders.TryGetValue(norm[name[0]], out var known) ? known : Join(words, name);
            return Act(ActionKind.OpenFolder, 0.93, argument: Span(name[^1]), detail: folder);
        }
        if (said.Length == 1 && KnownFolders.TryGetValue(norm[said[0]], out var direct))
            return Act(ActionKind.OpenFolder, 0.93, argument: Span(said[0]), detail: direct);
        if (said.Length == 3 && norm[said[0]] == "area" && norm[said[2]] == "trabalho")
            return Act(ActionKind.OpenFolder, 0.93, argument: Span(said[2]), detail: "desktop");
        return null;
    }

    /// <summary>The longest of the user's phrases said from <paramref name="start"/>.</summary>
    private (string Name, int Length)? ShortcutAt(string[] norm, int start)
    {
        foreach (var (name, phrase) in shortcutPhrases)
        {
            if (start + phrase.Length <= norm.Length && norm.AsSpan(start, phrase.Length).SequenceEqual(phrase)) return (name, phrase.Length);
        }
        return null;
    }

    /// <summary>The installed app the words name, and how sure: a unique match is sure, a shared word is not.</summary>
    private (string? App, double Probability) MatchApp(string[] said, IReadOnlyList<string> apps)
    {
        Index(apps);
        var spoken = string.Join(' ', said);
        foreach (var word in said)
        {
            if (SpokenAliases.TryGetValue(word, out var alias)
                && appIndex.FirstOrDefault(a => a.Normalized == alias || a.Normalized.StartsWith(alias + " ")) is { Name: not null } hit)
                return (hit.Name, 0.98);
        }
        var exact = appIndex.Where(a => (" " + spoken + " ").Contains(" " + a.Normalized + " ")).OrderByDescending(a => a.Normalized.Length).FirstOrDefault();
        if (exact.Name is not null) return (exact.Name, 0.99);

        var scored = appIndex
            .Select(a => (a.Name, Matched: a.Tokens.Count(said.Contains), a.Tokens.Length))
            .Where(a => a.Matched > 0)
            .Select(a => (a.Name, a.Matched, Share: (double)a.Matched / a.Length))
            .OrderByDescending(a => a.Matched).ThenByDescending(a => a.Share)
            .ToList();
        if (scored.Count == 0) return (null, 0);
        var best = scored[0];
        var ties = scored.Count(a => a.Matched == best.Matched && Math.Abs(a.Share - best.Share) < 1e-9);
        return (best.Name, 0.97 / ties);
    }

    private void Index(IReadOnlyList<string> apps)
    {
        if (ReferenceEquals(apps, indexedApps)) return;
        indexedApps = apps;
        appIndex = apps.Select(name =>
        {
            var normalized = string.Join(' ', Vocabulary.Words(name).Select(Vocabulary.Normalized).Where(w => w.Length > 0));
            var tokens = normalized.Split(' ').Where(t => t.Length > 1 && !GenericTokens.Contains(t) && !t.All(char.IsDigit)).Distinct().ToArray();
            return (name, tokens, normalized);
        }).ToList();
    }

    private static string Join(string[] words, IEnumerable<int> indexes) => string.Join(' ', indexes.Select(i => words[i]));

    private static Decision Act(ActionKind action, double p, string? app = null, double appProbability = 0, string? argument = null,
                                double opensApp = 0.05, string? detail = null) => new()
    {
        Action = action, Confidence = p, App = app, AppProbability = appProbability, Argument = argument, OpensApp = opensApp, Detail = detail,
        ActionProbabilities = new Dictionary<ActionKind, double> { [action] = p, [ActionKind.None] = 1 - p },
    };

    private static Decision Nothing(double p = 1) => new()
    {
        Action = ActionKind.None, Confidence = p, OpensApp = 0.05,
        ActionProbabilities = new Dictionary<ActionKind, double> { [ActionKind.None] = p },
    };
}
