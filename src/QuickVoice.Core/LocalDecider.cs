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
        ["vai", "va", "ir", "entra", "entrar", "entre", "acessa", "acessar", "acesse", "visita", "visitar", "go", "visit"];
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
        var verbAt = Array.FindIndex(norm, IsVerb);
        if (verbAt < 0) return Nothing();
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
    private static int ClauseEnd(string[] norm, int verbAt)
    {
        for (var i = verbAt + 1; i < norm.Length - 1; i++)
        {
            if (Vocabulary.Connectives.Contains(norm[i]) && IsVerb(norm[i + 1])) return i;
        }
        return norm.Length;
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

    private static Decision Act(ActionKind action, double p, string? app = null, double appProbability = 0, string? argument = null, double opensApp = 0.05) => new()
    {
        Action = action, Confidence = p, App = app, AppProbability = appProbability, Argument = argument, OpensApp = opensApp,
        ActionProbabilities = new Dictionary<ActionKind, double> { [action] = p, [ActionKind.None] = 1 - p },
    };

    private static Decision Nothing(double p = 1) => new()
    {
        Action = ActionKind.None, Confidence = p, OpensApp = 0.05,
        ActionProbabilities = new Dictionary<ActionKind, double> { [ActionKind.None] = p },
    };
}
