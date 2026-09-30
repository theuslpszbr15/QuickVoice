using Windows.Globalization;
using Windows.Media.SpeechRecognition;
using System.Windows.Threading;
using QuickVoice.Core;

namespace QuickVoice;

/// <summary>Microphone to text: partials of the whole utterance so far, and an event when recognition stops by itself.</summary>
internal interface IListener
{
    event Action<string>? Partial;
    event Action<string>? Ended;
    string Language { get; }
    /// <summary>Still hearing a voice though no new words came yet (Whisper transcribes in bursts): the pause must wait.</summary>
    bool Speaking { get; }
    Task StartAsync();
    Task StopAsync();
    void Restart();
}

/// <summary>
/// Microphone → Windows speech recognition (dictation), with partial results.
/// Windows reports one phrase at a time; this joins the phrases of the current utterance so the
/// session always sees the whole utterance so far, like the Apple recognizer the original used.
/// </summary>
internal sealed class Listener : IListener
{
    public event Action<string>? Partial;
    public event Action<string>? Ended;
    public string Language { get; }
    public bool Speaking => false;

    private readonly SpeechRecognizer recognizer;
    private readonly Dispatcher dispatcher;
    private string committed = "";
    private string hypothesis = "";
    private bool discardPhrase;
    private bool running;

    private Listener(SpeechRecognizer recognizer, Dispatcher dispatcher)
    {
        this.recognizer = recognizer;
        this.dispatcher = dispatcher;
        Language = recognizer.CurrentLanguage.LanguageTag;
        recognizer.HypothesisGenerated += (_, e) => Post(() => OnHypothesis(e.Hypothesis.Text));
        recognizer.ContinuousRecognitionSession.ResultGenerated += (_, e) => Post(() => OnResult(e.Result.Text));
        recognizer.ContinuousRecognitionSession.Completed += (_, e) => Post(() => OnCompleted(e.Status));
    }

    /// <summary>A null locale means the language Windows speech is set to.</summary>
    public static async Task<Listener> CreateAsync(string? locale, Dispatcher dispatcher)
    {
        var language = locale is null ? SpeechRecognizer.SystemSpeechLanguage : new Language(locale);
        if (!SpeechRecognizer.SupportedTopicLanguages.Any(l => l.LanguageTag.Equals(language.LanguageTag, StringComparison.OrdinalIgnoreCase)))
            throw new ListenerException($"Sem reconhecimento de fala para {language.LanguageTag}: instale o pacote de fala em Configurações → Hora e idioma → Fala.");
        var recognizer = new SpeechRecognizer(language);
        recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
        var compiled = await recognizer.CompileConstraintsAsync();
        if (compiled.Status != SpeechRecognitionResultStatus.Success)
        {
            recognizer.Dispose();
            throw new ListenerException($"O reconhecimento de fala não iniciou ({compiled.Status}).");
        }
        recognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromHours(1);
        return new Listener(recognizer, dispatcher);
    }

    public async Task StartAsync()
    {
        if (running) return;
        committed = hypothesis = "";
        discardPhrase = false;
        try
        {
            await recognizer.ContinuousRecognitionSession.StartAsync();
        }
        catch (Exception error) when (error.HResult == unchecked((int)0x80045509))
        {
            throw new ListenerException("Ative o reconhecimento de fala online: Configurações → Privacidade e segurança → Fala.", openSettings: "ms-settings:privacy-speech");
        }
        catch (UnauthorizedAccessException)
        {
            throw new ListenerException("Permita o microfone para apps da área de trabalho: Configurações → Privacidade e segurança → Microfone.", openSettings: "ms-settings:privacy-microphone");
        }
        running = true;
    }

    public async Task StopAsync()
    {
        if (!running) return;
        running = false;  // a phrase still reporting is ignored
        try
        {
            await recognizer.ContinuousRecognitionSession.CancelAsync();
        }
        catch (InvalidOperationException) { }  // already stopped by itself
    }

    /// <summary>A fresh utterance: forget the phrases heard so far.</summary>
    public void Restart()
    {
        committed = "";
        discardPhrase = hypothesis.Length > 0;  // its final result would replay words already acted on
        hypothesis = "";
    }

    private void OnHypothesis(string text)
    {
        if (!running) return;
        hypothesis = text;
        if (!discardPhrase) Partial?.Invoke(Join(committed, text));
    }

    /// <summary>
    /// The phrase is final. Windows rewrites it (capitals, punctuation); when the words are the same the partial
    /// already sent stands, so word offsets the engine consumed stay put.
    /// </summary>
    private void OnResult(string text)
    {
        if (!running) return;
        var last = hypothesis;
        hypothesis = "";
        if (discardPhrase)
        {
            discardPhrase = false;
            return;
        }
        var final = string.IsNullOrWhiteSpace(text) || SameWords(text, last) ? last : text;
        if (final.Length == 0) return;
        committed = Join(committed, final);
        if (final != last) Partial?.Invoke(committed);
    }

    private void OnCompleted(SpeechRecognitionResultStatus status)
    {
        if (!running) return;
        running = false;
        Ended?.Invoke(status.ToString());
    }

    private void Post(Action action) => dispatcher.InvokeAsync(action);

    private static string Join(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : a + " " + b;

    private static bool SameWords(string a, string b) =>
        Vocabulary.Words(a).Select(Vocabulary.Normalized).SequenceEqual(Vocabulary.Words(b).Select(Vocabulary.Normalized));
}

internal sealed class ListenerException(string message, string? openSettings = null) : Exception(message)
{
    /// <summary>The Settings page that fixes it, e.g. ms-settings:privacy-speech.</summary>
    public string? OpenSettings { get; } = openSettings;
}
