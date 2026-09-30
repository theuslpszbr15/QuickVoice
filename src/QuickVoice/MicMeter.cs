using NAudio.Wave;

namespace QuickVoice;

/// <summary>
/// How loud the microphone is, for the glow on the bar: overall and in three bands (low, mid, high). Audio is only
/// measured here, never kept or sent anywhere, and only while QuickVoice is listening.
/// </summary>
internal sealed class MicMeter : IDisposable
{
    private const int Rate = 16000;
    // Gain chain from voice-glow: RMS × 5 × sensitivity 3.1.
    private const double Gain = 5 * 3.1;

    private WaveIn? mic;
    private double lowState, midState;
    private double level, low, mid, high;

    public (double Level, double Low, double Mid, double High) Read() =>
        (Volatile.Read(ref level), Volatile.Read(ref low), Volatile.Read(ref mid), Volatile.Read(ref high));

    public void Start()
    {
        if (mic is not null) return;
        try
        {
            mic = new WaveIn { WaveFormat = new WaveFormat(Rate, 16, 1), BufferMilliseconds = 40 };
            mic.DataAvailable += (_, e) => Measure(e.Buffer, e.BytesRecorded);
            mic.StartRecording();
        }
        catch (Exception error) when (error is NAudio.MmException or InvalidOperationException)
        {
            mic?.Dispose();
            mic = null;  // no glow is better than no QuickVoice
        }
    }

    public void Stop()
    {
        mic?.StopRecording();
        mic?.Dispose();
        mic = null;
        Volatile.Write(ref level, 0);
        Volatile.Write(ref low, 0);
        Volatile.Write(ref mid, 0);
        Volatile.Write(ref high, 0);
    }

    /// <summary>One-pole filters split the voice at 300 Hz and 2 kHz, cheaper than an FFT and enough for a glow.</summary>
    private void Measure(byte[] buffer, int bytes)
    {
        var count = bytes / 2;
        if (count == 0) return;
        var aLow = 1 - Math.Exp(-2 * Math.PI * 300 / Rate);
        var aMid = 1 - Math.Exp(-2 * Math.PI * 2000 / Rate);
        double sum = 0, sumLow = 0, sumMid = 0, sumHigh = 0;
        for (var i = 0; i < count; i++)
        {
            var x = BitConverter.ToInt16(buffer, i * 2) / 32768.0;
            lowState += aLow * (x - lowState);
            midState += aMid * (x - midState);
            sum += x * x;
            sumLow += lowState * lowState;
            sumMid += (midState - lowState) * (midState - lowState);
            sumHigh += (x - midState) * (x - midState);
        }
        Volatile.Write(ref level, Math.Sqrt(sum / count) * Gain);
        Volatile.Write(ref low, Math.Sqrt(sumLow / count) * Gain * 1.2);
        Volatile.Write(ref mid, Math.Sqrt(sumMid / count) * Gain * 1.4);
        Volatile.Write(ref high, Math.Sqrt(sumHigh / count) * Gain * 2.5);
    }

    public void Dispose() => Stop();
}
