using System.Globalization;

namespace QuickVoice;

internal sealed class Options
{
    public const string Usage = """
        uso: QuickVoice [--text "frase"] [--dry-run] [--log] [--locale pt-BR] [--wpm 160]

          (sem --text)  barra flutuante: Alt+Espaço (ou Ctrl+Alt+Espaço) começa ou pausa
          --text        manda uma frase no ritmo da fala em vez do microfone (só terminal)
          --write       com --text: manda a frase de uma vez, como o modo de escrever
          --dry-run     mostra os comandos em vez de executá-los
          --log         guarda um rastro local em %LOCALAPPDATA%\QuickVoice\Logs (tudo o que o microfone ouvir)
          --locale      idioma da fala, ex. en-US (padrão: o idioma de fala do Windows)
          --wpm         ritmo para --text, em palavras por minuto
        """;

    public string? Text { get; private set; }
    public bool DryRun { get; private set; }
    public bool Write { get; private set; }
    public bool Log { get; private set; }
    public string? Locale { get; private set; }
    public double Wpm { get; private set; } = 160;
    public bool Help { get; private set; }
    /// <summary>Started by the app itself after a settings change: wait for the old instance to exit.</summary>
    public bool Restarted { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();
        var queue = new Queue<string>(args);
        while (queue.TryDequeue(out var flag))
        {
            switch (flag)
            {
                case "--dry-run": options.DryRun = true; break;
                case "--write": options.Write = true; break;
                case "--log": options.Log = true; break;
                case "--help" or "-h": options.Help = true; break;
                case "--restarted": options.Restarted = true; break;
                case "--text": options.Text = Value(flag, queue); break;
                case "--locale": options.Locale = Value(flag, queue); break;
                case "--wpm":
                    if (!double.TryParse(Value(flag, queue), NumberStyles.Float, CultureInfo.InvariantCulture, out var wpm) || wpm <= 0)
                        throw new ArgumentException("--wpm precisa de um número positivo");
                    options.Wpm = wpm;
                    break;
                default: throw new ArgumentException($"opção desconhecida {flag}");
            }
        }
        return options;
    }

    private static string Value(string flag, Queue<string> queue) =>
        queue.TryDequeue(out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"{flag} precisa de um valor");
}
