namespace QuickVoice.Core;

/// <summary>What the PC itself can be told to do, apart from apps, sites and typing.</summary>
public enum SystemAction
{
    CloseWindow, CloseTab, Minimize, Maximize, ShowDesktop, SwitchWindow,
    VolumeUp, VolumeDown, VolumeSet, Mute, PlayPause, NextTrack, PreviousTrack,
    Screenshot, Lock, DictationStart, DictationStop,
    Undo, Repeat, SnapLeft, SnapRight, OtherMonitor,
}

/// <summary>
/// Spoken system controls in Portuguese and English. Patterns are normalized words: "a|b" is either word,
/// a trailing "?" makes a word optional, and "$" means the clause must end there ("fecha" alone closes the window).
/// </summary>
public static class Controls
{
    private static readonly (SystemAction Action, string Pattern)[] Rules =
    [
        (SystemAction.DictationStart, "modo|comeca|comecar|comece|inicia|iniciar|inicie|ativa|ativar|ative|liga|ligar|start|begin|enable o|a|the? modo? de? ditado|dictation"),
        (SystemAction.DictationStop, "fim|termina|terminar|termine|para|parar|pare|encerra|encerrar|encerre|desliga|desligar|sai|sair|stop|end|finish|exit do|o|a|the? modo? de? ditado|dictation"),
        (SystemAction.CloseTab, "fecha|fechar|feche|close a|essa|esta|this|the? aba|guia|tab"),
        (SystemAction.CloseWindow, "fecha|fechar|feche|close a|o|essa|esse|esta|este|this|the? isso|janela|window|app|aplicativo|programa|it|this"),
        (SystemAction.CloseWindow, "fecha|fechar|feche|close $"),
        (SystemAction.ShowDesktop, "mostra|mostrar|mostre|vai|ir|va a|pra|para|na|the? area de trabalho"),
        (SystemAction.ShowDesktop, "show|go the|to? desktop"),
        (SystemAction.ShowDesktop, "minimiza|minimizar|minimize|minimise tudo|everything|all"),
        (SystemAction.Minimize, "minimiza|minimizar|minimize|minimise a|o|essa|esse|esta|este|this|the? isso|janela|window|app|it|this?"),
        (SystemAction.Maximize, "maximiza|maximizar|maximize|maximise a|o|essa|esse|esta|este|this|the? isso|janela|window|app|it|this?"),
        (SystemAction.SwitchWindow, "troca|trocar|troque|muda|mudar|mude|alterna|alternar|alterne de janela"),
        (SystemAction.SwitchWindow, "proxima|next janela|window"),
        (SystemAction.SwitchWindow, "switch window|windows"),
        (SystemAction.Mute, "muta|mutar|silencia|silenciar|silencie|desmuta|desmutar|mute|unmute o|a|the? som|audio|volume|computador|pc|sound?"),
        (SystemAction.Mute, "tira|tirar|tire o som"),
        (SystemAction.Mute, "sem som"),
        (SystemAction.NextTrack, "proxima|next musica|faixa|song|track"),
        (SystemAction.NextTrack, "pula|pular|pule|skip a|essa|esta|this|the? musica|faixa|song|track"),
        (SystemAction.PreviousTrack, "musica|faixa anterior"),
        (SystemAction.PreviousTrack, "previous|last song|track"),
        (SystemAction.PreviousTrack, "volta|voltar|volte a|o? musica|faixa"),
        (SystemAction.PlayPause, "pausa|pausar|pause|continua|continuar|continue|retoma|retomar|toca|tocar|toque|play|resume a|o|the? musica|music|video|som|faixa|song"),
        (SystemAction.PlayPause, "play|pause $"),
        (SystemAction.Screenshot, "tira|tirar|tire|faz|fazer|faca|take um|uma|a? print|screenshot|captura de|da? tela?"),
        (SystemAction.Screenshot, "captura|capturar|capture a|the? tela|screen"),
        (SystemAction.Screenshot, "print da|de? tela"),
        (SystemAction.Screenshot, "screenshot"),
        (SystemAction.Lock, "bloqueia|bloquear|bloqueie|trava|travar|trave|lock o|a|the|my? computador|pc|tela|computer|screen"),
        (SystemAction.Undo, "desfaz|desfazer|desfaca|desfaça|undo isso|that|it|o|a? ultimo|ultima|last? comando|command?"),
        (SystemAction.Repeat, "repete|repetir|repita|repeat o|a|that|the|it? ultimo|ultima|last? comando|command|isso?"),
        (SystemAction.Repeat, "faz|faca|fazer de novo"),
        (SystemAction.Repeat, "de novo $"),
        (SystemAction.Repeat, "do it again"),
        (SystemAction.SnapLeft, "coloca|coloque|joga|jogue|manda|mande|poe|ponha|move|mova|leva|leve|snap|put isso|a|essa|esta|this|the|it? janela|window? na|pra|para|pro|to|on a|the? esquerda|left"),
        (SystemAction.SnapRight, "coloca|coloque|joga|jogue|manda|mande|poe|ponha|move|mova|leva|leve|snap|put isso|a|essa|esta|this|the|it? janela|window? na|pra|para|pro|to|on a|the? direita|right"),
        (SystemAction.OtherMonitor, "coloca|coloque|joga|jogue|manda|mande|poe|ponha|move|mova|leva|leve|put isso|a|essa|esta|this|the|it? janela|window? pro|pra|para|no|na|to|on o|a|the? outro|outra|other|next monitor|tela|screen|display"),
        (SystemAction.OtherMonitor, "troca|trocar|troque|muda|mudar|mude|switch de? monitor|tela|screen"),
    ];

    private static readonly List<(SystemAction Action, Token[] Tokens)> Parsed =
        Rules.Select(r => (r.Action, Parse(r.Pattern))).ToList();

    private static readonly HashSet<string> Up = ["aumenta", "aumentar", "aumente", "sobe", "subir", "suba", "raise", "increase", "up"];
    private static readonly HashSet<string> Down = ["diminui", "diminuir", "diminua", "abaixa", "abaixar", "abaixe", "baixa", "baixar", "baixe", "lower", "decrease", "reduce", "down"];
    private static readonly HashSet<string> SetVerbs = ["coloca", "colocar", "coloque", "poe", "por", "bota", "botar", "deixa", "deixar", "deixe", "muda", "mudar", "set", "put"];
    private static readonly HashSet<string> VolumeWords = ["volume", "som", "audio", "sound"];
    private static readonly HashSet<string> NumberLinks = ["em", "pra", "para", "no", "a", "to", "at", "o"];
    private static readonly Dictionary<string, int> SpelledNumbers = new()
    {
        ["zero"] = 0, ["dez"] = 10, ["vinte"] = 20, ["trinta"] = 30, ["quarenta"] = 40, ["cinquenta"] = 50, ["sessenta"] = 60,
        ["setenta"] = 70, ["oitenta"] = 80, ["noventa"] = 90, ["cem"] = 100, ["ten"] = 10, ["twenty"] = 20, ["thirty"] = 30,
        ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90, ["hundred"] = 100,
        ["maximo"] = 100, ["max"] = 100, ["metade"] = 50, ["half"] = 50,
    };

    /// <summary>The first word of any control, so a clause can end where one starts ("abre o spotify | e aumenta o volume").</summary>
    public static readonly HashSet<string> Starters =
        Parsed.SelectMany(p => p.Tokens[0].Words).Concat(Up).Concat(Down).Concat(SetVerbs.Where(v => v != "por")).Append("volume").ToHashSet();

    /// <summary>A control said from <paramref name="start"/>: which one, its value, and where its words end.</summary>
    public static (SystemAction Action, int? Value, int End)? Match(string[] words, int start, int clauseEnd)
    {
        if (Volume(words, start, clauseEnd) is { } volume) return volume;
        foreach (var (action, tokens) in Parsed)
        {
            if (Match(tokens, words, start, clauseEnd) is { } end) return (action, null, end);
        }
        return null;
    }

    /// <summary>"aumenta o volume", "volume 30", "coloca o volume em cinquenta", "volume up".</summary>
    private static (SystemAction, int?, int)? Volume(string[] w, int i, int clauseEnd)
    {
        int? Find(int from, int span)
        {
            for (var j = from; j < Math.Min(clauseEnd, from + span); j++)
                if (VolumeWords.Contains(w[j])) return j;
            return null;
        }
        (int Value, int End)? Number(int from)
        {
            var j = from;
            while (j < clauseEnd && NumberLinks.Contains(w[j])) j++;
            if (j >= clauseEnd) return null;
            int? value = int.TryParse(w[j], out var n) ? n : SpelledNumbers.TryGetValue(w[j], out var s) ? s : null;
            if (value is null) return null;
            j++;
            if (j < clauseEnd && w[j] is "porcento" or "percent") j++;
            else if (j + 1 < clauseEnd && w[j] == "por" && w[j + 1] == "cento") j += 2;
            return (Math.Clamp(value.Value, 0, 100), j);
        }

        if (i >= clauseEnd) return null;
        var first = w[i];
        if ((Up.Contains(first) || Down.Contains(first)) && first is not ("up" or "down") && Find(i + 1, 3) is { } v)
            return (Up.Contains(first) ? SystemAction.VolumeUp : SystemAction.VolumeDown, null, v + 1);
        if (SetVerbs.Contains(first) && first != "por" && Find(i + 1, 2) is { } sv && Number(sv + 1) is { } set)
            return (SystemAction.VolumeSet, set.Value, set.End);
        if (first != "volume") return null;
        if (Number(i + 1) is { } direct) return (SystemAction.VolumeSet, direct.Value, direct.End);
        if (i + 1 < clauseEnd && (Up.Contains(w[i + 1]) || w[i + 1] is "mais" or "alto")) return (SystemAction.VolumeUp, null, i + 2);
        if (i + 1 < clauseEnd && (Down.Contains(w[i + 1]) || w[i + 1] is "menos" or "baixo")) return (SystemAction.VolumeDown, null, i + 2);
        return null;
    }

    public static string Encode(SystemAction action, int? value) => value is { } v ? $"{action}:{v}" : action.ToString();

    public static Command.Control? Decode(string detail)
    {
        var parts = detail.Split(':');
        if (!Enum.TryParse<SystemAction>(parts[0], out var action)) return null;
        int? value = parts.Length > 1 && int.TryParse(parts[1], out var v) ? v : null;
        return new Command.Control(action, value);
    }

    public static string Describe(SystemAction action, int? value) => action switch
    {
        SystemAction.CloseWindow => "fechar a janela",
        SystemAction.CloseTab => "fechar a aba",
        SystemAction.Minimize => "minimizar",
        SystemAction.Maximize => "maximizar",
        SystemAction.ShowDesktop => "mostrar a área de trabalho",
        SystemAction.SwitchWindow => "trocar de janela",
        SystemAction.VolumeUp => "aumentar o volume",
        SystemAction.VolumeDown => "diminuir o volume",
        SystemAction.VolumeSet => $"volume em {value}%",
        SystemAction.Mute => "ligar/desligar o som",
        SystemAction.PlayPause => "tocar/pausar",
        SystemAction.NextTrack => "próxima faixa",
        SystemAction.PreviousTrack => "faixa anterior",
        SystemAction.Screenshot => "tirar um print",
        SystemAction.Lock => "bloquear o PC",
        SystemAction.DictationStart => "começar o ditado",
        SystemAction.DictationStop => "parar o ditado",
        SystemAction.Undo => "desfazer",
        SystemAction.Repeat => "repetir o último comando",
        SystemAction.SnapLeft => "janela na esquerda",
        SystemAction.SnapRight => "janela na direita",
        SystemAction.OtherMonitor => "janela no outro monitor",
        _ => action.ToString(),
    };

    private sealed record Token(HashSet<string> Words, bool Optional, bool End);

    private static Token[] Parse(string pattern) =>
        pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => part == "$"
            ? new Token([], false, true)
            : new Token(part.TrimEnd('?').Split('|').ToHashSet(), part.EndsWith('?'), false)).ToArray();

    /// <summary>Optional words are skipped when they do not fit, so "fecha isso" and "fecha a janela" both match.</summary>
    private static int? Match(Token[] tokens, string[] words, int start, int clauseEnd)
    {
        var j = start;
        foreach (var token in tokens)
        {
            if (token.End)
            {
                if (j != clauseEnd) return null;
                continue;
            }
            if (j < clauseEnd && token.Words.Contains(words[j])) j++;
            else if (!token.Optional) return null;
        }
        return j;
    }
}
