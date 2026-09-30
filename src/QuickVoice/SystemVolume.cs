using NAudio.CoreAudioApi;

namespace QuickVoice;

/// <summary>The default speaker's volume, through Windows Core Audio.</summary>
internal static class SystemVolume
{
    public static void Set(int percent)
    {
        using var devices = new MMDeviceEnumerator();
        using var speaker = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        speaker.AudioEndpointVolume.Mute = false;
        speaker.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent, 0, 100) / 100f;
    }
}
