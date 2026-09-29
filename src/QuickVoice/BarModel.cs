using QuickVoice.Core;

namespace QuickVoice;

/// <summary>What the floating bar shows. Session writes it, BarWindow draws it.</summary>
internal sealed class BarModel
{
    public sealed record Fired(string Command, string? Lead = null);

    public event Action? Changed;

    public bool Listening { get => listening; set => Set(ref listening, value); }
    /// <summary>Expanded while you talk (Jev's read below the pill); collapses when the utterance ends.</summary>
    public bool Expanded { get; private set; }
    public IReadOnlyList<SpokenWord> Words { get; private set; } = [];
    public Decision? Decision { get; private set; }
    public List<Fired> FiredCommands { get; } = [];
    /// <summary>A command just fired: the chip turns yellow until the next answer.</summary>
    public bool Flash { get; private set; }
    public bool Paused { get => paused; set => Set(ref paused, value); }
    /// <summary>Something the person must fix (permissions, key). Shown while the bar is idle.</summary>
    public string? Notice { get => notice; set => Set(ref notice, value); }
    public string Hotkey { get => hotkey; set => Set(ref hotkey, value); }
    public string WriteHotkey { get => writeHotkey; set => Set(ref writeHotkey, value); }

    /// <summary>Set by the app: the bar's play/pause button.</summary>
    public Action Toggle { get; set; } = () => { };
    /// <summary>Set by the app: a command typed in write mode.</summary>
    public Action<string> Submit { get; set; } = _ => { };

    private bool listening, paused;
    private string? notice;
    private string hotkey = "Alt+Espaço";
    private string writeHotkey = "Alt+Shift+Espaço";

    public void Heard(IReadOnlyList<SpokenWord> words)
    {
        if (!Expanded) FiredCommands.Clear();  // a new utterance starts clean
        Words = words;
        Expanded = true;
        paused = false;
        Changed?.Invoke();
    }

    public void Answered(Decision decision)
    {
        Decision = decision;
        Flash = false;
        Changed?.Invoke();
    }

    public void Fire(Command command, IReadOnlyList<SpokenWord> words)
    {
        FiredCommands.Add(new Fired(command.ToString()));
        Words = words;
        Flash = true;
        Changed?.Invoke();
    }

    public void Finish(IReadOnlyList<string> leads)
    {
        for (var i = 0; i < leads.Count && i < FiredCommands.Count; i++) FiredCommands[i] = FiredCommands[i] with { Lead = leads[i] };
        Expanded = false;
        Decision = null;
        paused = false;
        Flash = false;
        Changed?.Invoke();
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke();
    }
}
