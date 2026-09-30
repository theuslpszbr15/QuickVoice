using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace QuickVoice;

/// <summary>
/// Offline speech recognition with Whisper (whisper.cpp) on this PC: nothing leaves the computer.
/// Whisper hears whole clips, not a stream, so while someone speaks the clip so far is transcribed again
/// every few hundred milliseconds; each result is the utterance so far, like the Windows partials.
/// </summary>
internal sealed partial class WhisperListener : IListener, IDisposable
{
    public event Action<string>? Partial;
    public event Action<string>? Ended;
    public string Language { get; }
    public bool Speaking
    {
        get
        {
            lock (gate) return inSpeech || transcribing || dirty;
        }
    }

    // Calibration knobs: a voice is louder than the room by this factor; this much quiet ends a burst of speech.
    private const int Rate = 16000;
    private const double VoiceOverNoise = 3.0;
    private const int QuietEndsSpeechMs = 700;
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(400);
    private const int PrerollSamples = Rate * 3 / 10;
    private const int MaxSamples = Rate * 28;  // Whisper hears up to 30 s at once

    private static readonly HashSet<string> Hallucinations = new(StringComparer.OrdinalIgnoreCase)
    {
        "obrigado.", "obrigada.", "obrigado por assistir.", "legendas pela comunidade amara.org", "thank you.", "thanks for watching!",
        "you", "tchau.", "e aí.", "...",
    };

    private readonly Dispatcher dispatcher;
    private readonly WhisperFactory factory;
    private readonly WhisperProcessor processor;
    private readonly WaveIn mic;
    private readonly Timer timer;
    private readonly object gate = new();
    private readonly List<float> utterance = [];
    private readonly List<float> preroll = [];
    private double noise = 0.005;
    private bool inSpeech, dirty, transcribing, running;
    private int quietMs, generation;
    private string lastText = "";

    private WhisperListener(WhisperFactory factory, WhisperProcessor processor, string language, Dispatcher dispatcher)
    {
        this.factory = factory;
        this.processor = processor;
        this.dispatcher = dispatcher;
        Language = language;
        mic = new WaveIn { WaveFormat = new WaveFormat(Rate, 16, 1), BufferMilliseconds = 50 };
        mic.DataAvailable += (_, e) => Hear(e.Buffer, e.BytesRecorded);
        mic.RecordingStopped += (_, e) =>
        {
            if (e.Exception is { } error) dispatcher.InvokeAsync(() => Ended?.Invoke(error.Message));
        };
        timer = new Timer(_ => Tick(), null, Every, Every);
    }

    public static string ModelPath(string model) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickVoice", "models", $"ggml-{model}.bin");

    public static string ModelSize(string model) => model switch { "tiny" => "75 MB", "small" => "470 MB", _ => "140 MB" };

    /// <summary>Downloads the model on first use (once), then loads it.</summary>
    public static async Task<WhisperListener> CreateAsync(string? locale, string model, Dispatcher dispatcher, Action<string> progress)
    {
        var path = ModelPath(model);
        if (!File.Exists(path))
        {
            progress($"Baixando o modelo Whisper “{model}” ({ModelSize(model)}), só na primeira vez…");
            var type = model switch { "tiny" => GgmlType.Tiny, "small" => GgmlType.Small, _ => GgmlType.Base };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var partial = path + ".download";
            await using (var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(type, QuantizationType.NoQuantization, CancellationToken.None))
            await using (var file = File.Create(partial))
            {
                await source.CopyToAsync(file);
            }
            File.Move(partial, path, overwrite: true);
        }
        progress("Carregando o Whisper…");
        var language = (locale ?? CultureInfo.CurrentUICulture.Name).Split('-')[0].ToLowerInvariant();
        return await Task.Run(() =>
        {
            var factory = WhisperFactory.FromPath(path);
            var processor = factory.CreateBuilder()
                .WithLanguage(language)
                .WithThreads(Math.Max(2, Environment.ProcessorCount / 2))
                .WithNoContext()
                .WithSingleSegment()
                .Build();
            return new WhisperListener(factory, processor, $"{language} (Whisper {model}, offline)", dispatcher);
        });
    }

    public Task StartAsync()
    {
        if (running) return Task.CompletedTask;
        Restart();
        try
        {
            mic.StartRecording();
        }
        catch (NAudio.MmException error)
        {
            throw new ListenerException($"O microfone não abriu: {error.Message}. Permita o microfone para apps da área de trabalho.",
                                        openSettings: "ms-settings:privacy-microphone");
        }
        running = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!running) return Task.CompletedTask;
        running = false;
        mic.StopRecording();
        Restart();
        return Task.CompletedTask;
    }

    /// <summary>A fresh utterance: forget the audio and any transcription still running for the old one.</summary>
    public void Restart()
    {
        lock (gate)
        {
            utterance.Clear();
            dirty = false;
            generation++;
            lastText = "";
        }
    }

    /// <summary>Voice detection by loudness against a running estimate of the room's noise.</summary>
    private void Hear(byte[] buffer, int bytes)
    {
        var chunk = new float[bytes / 2];
        double energy = 0;
        for (var i = 0; i < chunk.Length; i++)
        {
            chunk[i] = BitConverter.ToInt16(buffer, i * 2) / 32768f;
            energy += chunk[i] * chunk[i];
        }
        var rms = Math.Sqrt(energy / Math.Max(1, chunk.Length));
        var ms = chunk.Length * 1000 / Rate;
        lock (gate)
        {
            if (!running) return;
            if (!inSpeech)
            {
                noise = noise * 0.97 + rms * 0.03;
                preroll.AddRange(chunk);
                if (preroll.Count > PrerollSamples) preroll.RemoveRange(0, preroll.Count - PrerollSamples);
                if (rms <= Math.Max(0.01, noise * VoiceOverNoise)) return;
                inSpeech = true;
                quietMs = 0;
                Append(preroll);
                preroll.Clear();
                return;
            }
            Append(chunk);
            quietMs = rms < Math.Max(0.007, noise * 2) ? quietMs + ms : 0;
            if (quietMs >= QuietEndsSpeechMs) inSpeech = false;
        }
    }

    private void Append(IEnumerable<float> samples)
    {
        if (utterance.Count >= MaxSamples) return;
        utterance.AddRange(samples);
        dirty = true;
    }

    private void Tick()
    {
        float[] clip;
        int started;
        lock (gate)
        {
            if (!dirty || transcribing || utterance.Count < Rate / 4) return;
            clip = [.. utterance];
            started = generation;
            dirty = false;
            transcribing = true;
        }
        _ = TranscribeAsync(clip, started);
    }

    private async Task TranscribeAsync(float[] clip, int started)
    {
        try
        {
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(clip)) text.Append(segment.Text);
            var said = Clean(text.ToString());
            await dispatcher.InvokeAsync(() =>
            {
                lock (gate)
                {
                    if (started != generation || !running || said.Length == 0 || said == lastText) return;
                    lastText = said;
                }
                Partial?.Invoke(said);
            });
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await dispatcher.InvokeAsync(() => Ended?.Invoke($"Whisper: {error.Message}"));
        }
        finally
        {
            lock (gate) transcribing = false;
        }
    }

    /// <summary>Drops "[Música]"-style tags and the phrases Whisper invents over near-silence.</summary>
    private static string Clean(string text)
    {
        var cleaned = Tags().Replace(text, " ").Trim();
        cleaned = Spaces().Replace(cleaned, " ");
        return Hallucinations.Contains(cleaned) ? "" : cleaned;
    }

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public void Dispose()
    {
        timer.Dispose();
        mic.Dispose();
        processor.Dispose();
        factory.Dispose();
    }
}
