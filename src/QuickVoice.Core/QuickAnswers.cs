using System.Globalization;

namespace QuickVoice.Core;

/// <summary>
/// Quick questions answered on the bar: arithmetic ("quanto é 15% de 320"), the time ("que horas são em Nova York")
/// and today's date. Arithmetic is computed here; times are left to the executor, which has the clock.
/// </summary>
public static class QuickAnswers
{
    private static readonly HashSet<string> MathLeads = ["quanto", "quantos", "calcula", "calcular", "calcule", "what", "whats", "what's", "calculate", "compute"];
    private static readonly HashSet<string> MathFiller = ["e", "eh", "é", "da", "de", "is", "s", "the", "o", "a"];

    private static readonly Dictionary<string, (string Zone, string City)> Cities = new()
    {
        ["nova york"] = ("Eastern Standard Time", "Nova York"), ["new york"] = ("Eastern Standard Time", "Nova York"),
        ["detroit"] = ("Eastern Standard Time", "Detroit"), ["miami"] = ("Eastern Standard Time", "Miami"),
        ["chicago"] = ("Central Standard Time", "Chicago"), ["mexico"] = ("Central Standard Time (Mexico)", "Cidade do México"),
        ["los angeles"] = ("Pacific Standard Time", "Los Angeles"), ["california"] = ("Pacific Standard Time", "Califórnia"),
        ["sao paulo"] = ("E. South America Standard Time", "São Paulo"), ["brasilia"] = ("E. South America Standard Time", "Brasília"),
        ["buenos aires"] = ("Argentina Standard Time", "Buenos Aires"), ["londres"] = ("GMT Standard Time", "Londres"),
        ["london"] = ("GMT Standard Time", "Londres"), ["lisboa"] = ("GMT Standard Time", "Lisboa"), ["portugal"] = ("GMT Standard Time", "Portugal"),
        ["paris"] = ("Romance Standard Time", "Paris"), ["madri"] = ("Romance Standard Time", "Madri"),
        ["berlim"] = ("W. Europe Standard Time", "Berlim"), ["berlin"] = ("W. Europe Standard Time", "Berlim"),
        ["alemanha"] = ("W. Europe Standard Time", "Alemanha"), ["dubai"] = ("Arabian Standard Time", "Dubai"),
        ["india"] = ("India Standard Time", "Índia"), ["china"] = ("China Standard Time", "China"), ["xangai"] = ("China Standard Time", "Xangai"),
        ["toquio"] = ("Tokyo Standard Time", "Tóquio"), ["tokyo"] = ("Tokyo Standard Time", "Tóquio"), ["japao"] = ("Tokyo Standard Time", "Japão"),
        ["coreia"] = ("Korea Standard Time", "Coreia"), ["sydney"] = ("AUS Eastern Standard Time", "Sydney"),
    };

    /// <summary>An answer asked from <paramref name="start"/>: its kind, value, the question as said, and where it ends.</summary>
    public static (string Kind, string Value, string Question, int End)? Match(string[] raw, string[] norm, int start)
    {
        var rest = norm[start..];
        // "que horas são (em Nova York)", "what time is it (in Tokyo)"
        var timeWords = rest is ["que", "horas", ..] ? 2 : rest is ["what", "time", ..] ? 2 : 0;
        if (timeWords > 0)
        {
            var place = string.Join(' ', rest.Skip(timeWords).SkipWhile(w => w is "sao" or "e" or "is" or "it" or "agora" or "now" or "em" or "no" or "na" or "in" or "at"));
            var city = Cities.Keys.Where(c => place.StartsWith(c)).OrderByDescending(c => c.Length).FirstOrDefault();
            var question = string.Join(' ', raw[start..]);
            return city is null
                ? ("time", "local", question, norm.Length)
                : ("time", $"{Cities[city].Zone}|{Cities[city].City}", question, norm.Length);
        }
        if (rest is ["que", "dia", ..] or ["qual", "a", "data", ..] or ["what", "day", ..] or ["what", "is", "the", "date", ..] or ["whats", "the", "date", ..])
            return ("date", "", string.Join(' ', raw[start..]), norm.Length);
        if (!MathLeads.Contains(rest.FirstOrDefault() ?? "")) return null;
        var from = start + 1;
        while (from < norm.Length && MathFiller.Contains(norm[from])) from++;
        if (from >= norm.Length || Arithmetic.Evaluate(raw[from..]) is not { } value) return null;
        return ("math", Arithmetic.Format(value), string.Join(' ', raw[from..]).TrimEnd('?', '.', '!'), norm.Length);
    }

    /// <summary>The sentence shown on the bar for an answer, at <paramref name="utcNow"/>.</summary>
    public static string Speak(Command.Answer answer, DateTime utcNow)
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        switch (answer.Kind)
        {
            case "math":
                return $"{answer.Question} = {answer.Value}";
            case "date":
                var today = utcNow.ToLocalTime().ToString("dddd, d 'de' MMMM 'de' yyyy", pt);
                return char.ToUpper(today[0], pt) + today[1..];
            default:
                if (answer.Value.Split('|') is not [var zone, var city]) return $"Agora são {utcNow.ToLocalTime():HH:mm}";
                try
                {
                    var there = TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.FindSystemTimeZoneById(zone));
                    var days = (there.Date - utcNow.ToLocalTime().Date).Days;
                    var day = days switch { > 0 => " (amanhã)", < 0 => " (ontem)", _ => "" };
                    return $"Em {city} são {there:HH:mm}{day}";
                }
                catch (TimeZoneNotFoundException)
                {
                    return $"Não sei o fuso de {city}";
                }
        }
    }
}

/// <summary>Spoken arithmetic: "15% de 320", "12 vezes 8", "raiz de 81", "2 elevado a 10", "100 dividido por 3".</summary>
public static class Arithmetic
{
    private static readonly (string[] Said, string Op)[] Operators =
    [
        (["multiplicado", "por"], "*"), (["multiplied", "by"], "*"), (["dividido", "por"], "/"), (["divided", "by"], "/"),
        (["elevado", "ao"], "^"), (["elevado", "a"], "^"), (["to", "the", "power", "of"], "^"), (["por", "cento", "de"], "%of"),
        (["por", "cento"], "%"), (["percent", "of"], "%of"), (["percent"], "%"), (["raiz", "quadrada", "de"], "sqrt"),
        (["raiz", "de"], "sqrt"), (["square", "root", "of"], "sqrt"), (["mais"], "+"), (["plus"], "+"), (["menos"], "-"),
        (["minus"], "-"), (["vezes"], "*"), (["times"], "*"), (["x"], "*"), (["sobre"], "/"), (["de"], "of"), (["of"], "of"),
    ];

    private static readonly Dictionary<string, double> Spelled = new()
    {
        ["zero"] = 0, ["um"] = 1, ["uma"] = 1, ["dois"] = 2, ["duas"] = 2, ["tres"] = 3, ["quatro"] = 4, ["cinco"] = 5, ["seis"] = 6,
        ["sete"] = 7, ["oito"] = 8, ["nove"] = 9, ["dez"] = 10, ["vinte"] = 20, ["trinta"] = 30, ["cem"] = 100, ["mil"] = 1000,
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
    };

    public static double? Evaluate(string[] said)
    {
        var tokens = Tokens(said);
        if (tokens is null || tokens.Count == 0 || !tokens.Any(t => t is double)) return null;
        try
        {
            var position = 0;
            var value = Sum(tokens, ref position);
            return position == tokens.Count && double.IsFinite(value) ? value : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Brazilian style: "48", "33,3333", "1.024".</summary>
    public static string Format(double value) =>
        Math.Round(value, 4).ToString(Math.Abs(value % 1) < 1e-9 ? "#,0" : "#,0.####", CultureInfo.GetCultureInfo("pt-BR"));

    private static List<object>? Tokens(string[] said)
    {
        var words = new List<string>();
        foreach (var raw in said)
        {
            var w = raw.Trim().TrimEnd('?', '!', '.').ToLowerInvariant();
            if (w.Length == 0) continue;
            if (w.Length > 1 && w.EndsWith('%')) { words.Add(w[..^1]); words.Add("%"); }
            else words.Add(w);
        }
        var norm = words.Select(w => w is "%" or "+" or "-" or "*" or "/" or "^" or "×" ? w : Vocabulary.Normalized(w)).ToList();
        var tokens = new List<object>();
        for (var i = 0; i < norm.Count;)
        {
            var op = Operators.FirstOrDefault(o => i + o.Said.Length <= norm.Count && norm.Skip(i).Take(o.Said.Length).SequenceEqual(o.Said));
            if (op.Said is not null && !(op.Op == "*" && op.Said is ["x"] && (tokens.Count == 0 || tokens[^1] is string)))
            {
                tokens.Add(op.Op);
                i += op.Said.Length;
                continue;
            }
            var w = norm[i];
            if (w is "%" or "+" or "-" or "*" or "/" or "^") tokens.Add(w);
            else if (w == "×") tokens.Add("*");
            else if (Number(words[i]) is { } n) tokens.Add(n);
            else if (Spelled.TryGetValue(w, out var s)) tokens.Add(s);
            else return null;
            i++;
        }
        return tokens;
    }

    /// <summary>"15", "3,5", "1.000", "2.5": a comma is the decimal mark, dots group thousands unless the dot is the only separator with 1-2 decimals.</summary>
    private static double? Number(string word)
    {
        var w = word.Replace("r$", "").Replace("$", "");
        if (w.Length == 0 || !char.IsDigit(w[0]) && w[0] != '-') return null;
        if (w.Contains(','))
            w = w.Replace(".", "").Replace(',', '.');
        else if (w.Count(c => c == '.') == 1 && w.Split('.')[1].Length != 3) { }
        else w = w.Replace(".", "");
        return double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    // Recursive descent: sum := product (('+'|'-') product)*; product := power (('*'|'/'|'of'|'%of') power)*; power := unary ('^' unary)?
    private static double Sum(List<object> t, ref int p)
    {
        var value = Product(t, ref p);
        while (p < t.Count && t[p] is "+" or "-")
        {
            var op = (string)t[p++];
            var right = Product(t, ref p);
            value = op == "+" ? value + right : value - right;
        }
        return value;
    }

    private static double Product(List<object> t, ref int p)
    {
        var value = Power(t, ref p);
        while (p < t.Count && t[p] is "*" or "/" or "of" or "%of")
        {
            var op = (string)t[p++];
            var right = Power(t, ref p);
            value = op switch { "*" or "of" => value * right, "/" => value / right, _ => value / 100 * right };
        }
        return value;
    }

    private static double Power(List<object> t, ref int p)
    {
        var value = Unary(t, ref p);
        if (p < t.Count && t[p] is "^")
        {
            p++;
            value = Math.Pow(value, Unary(t, ref p));
        }
        return value;
    }

    private static double Unary(List<object> t, ref int p)
    {
        if (p >= t.Count) throw new FormatException();
        if (t[p] is "-") { p++; return -Unary(t, ref p); }
        if (t[p] is "sqrt") { p++; return Math.Sqrt(Unary(t, ref p)); }
        if (t[p] is not double n) throw new FormatException();
        p++;
        if (p < t.Count && t[p] is "%") { p++; n /= 100; }  // "15%" alone, or before "de": 0.15 of what follows
        return n;
    }
}

/// <summary>Kinds of recent files and the extensions that count as each.</summary>
public static class Recent
{
    public static readonly Dictionary<string, string[]> Extensions = new()
    {
        ["pdf"] = [".pdf"],
        ["excel"] = [".xlsx", ".xls", ".xlsm", ".csv"],
        ["word"] = [".docx", ".doc", ".rtf", ".odt"],
        ["powerpoint"] = [".pptx", ".ppt"],
        ["image"] = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".heic"],
        ["video"] = [".mp4", ".mov", ".mkv", ".avi", ".webm"],
        ["download"] = [],
        ["any"] = [],
    };

    /// <summary>What was said after "último" → a kind.</summary>
    public static readonly Dictionary<string, string> Nouns = new()
    {
        ["pdf"] = "pdf", ["planilha"] = "excel", ["excel"] = "excel", ["xlsx"] = "excel", ["spreadsheet"] = "excel",
        ["documento"] = "word", ["word"] = "word", ["doc"] = "word", ["document"] = "word",
        ["apresentacao"] = "powerpoint", ["powerpoint"] = "powerpoint", ["slides"] = "powerpoint", ["presentation"] = "powerpoint",
        ["imagem"] = "image", ["foto"] = "image", ["print"] = "image", ["screenshot"] = "image", ["image"] = "image", ["photo"] = "image",
        ["video"] = "video", ["baixado"] = "download", ["download"] = "download", ["downloaded"] = "download",
        ["arquivo"] = "any", ["file"] = "any",
    };

    public static string Describe(string kind) => kind switch
    {
        "pdf" => "PDF", "excel" => "Excel", "word" => "documento do Word", "powerpoint" => "PowerPoint", "image" => "imagem",
        "video" => "vídeo", "download" => "arquivo baixado", _ => "arquivo",
    };
}
