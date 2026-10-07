using System.Speech.Synthesis;

namespace TS6Overlay;

/// <summary>
/// Lektor Windows (SAPI): czyta powiadomienia na głos. Słychać go także w grach z wyłącznym
/// pełnym ekranem, gdzie nakładki nie widać.
/// </summary>
public sealed class Speech : IDisposable
{
    readonly SpeechSynthesizer _synth = new();
    readonly Config _cfg;
    int _queued;

    public Speech(Config cfg)
    {
        _cfg = cfg;
        _synth.SetOutputToDefaultAudioDevice();
        _synth.SpeakCompleted += (_, _) => Interlocked.Decrement(ref _queued);
    }

    public static List<string> Voices()
    {
        try
        {
            using var s = new SpeechSynthesizer();
            return s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();
        }
        catch { return new(); }
    }

    /// <summary>Czy dane powiadomienie ma być przeczytane.</summary>
    public bool Wants(Notice n) => _cfg.TtsEnabled && n.Kind switch
    {
        NoticeKind.Join => _cfg.TtsJoin,
        NoticeKind.Leave => _cfg.TtsLeave,
        NoticeKind.Message => _cfg.TtsMessages,
        NoticeKind.Poke => _cfg.TtsPokes,
        NoticeKind.Friend or NoticeKind.Watched => _cfg.TtsFriends,
        _ => false,
    };

    public void Say(string text)
    {
        try
        {
            // Nie buduj kolejki: przy zalewie zdarzeń czytaj tylko najnowsze.
            if (Volatile.Read(ref _queued) >= 3) { _synth.SpeakAsyncCancelAll(); _queued = 0; }
            Apply();
            Interlocked.Increment(ref _queued);
            _synth.SpeakAsync(text);
        }
        catch { }
    }

    public void Stop()
    {
        try { _synth.SpeakAsyncCancelAll(); _queued = 0; } catch { }
    }

    void Apply()
    {
        _synth.Rate = Math.Clamp(_cfg.TtsRate, -10, 10);
        _synth.Volume = Math.Clamp(_cfg.TtsVolume, 0, 100);
        var voice = _cfg.TtsVoice;
        if (voice == "")
        {
            // Bez wybranego głosu: głos w języku interfejsu, jeśli jest.
            var culture = L.En ? "en" : "pl";
            voice = _synth.GetInstalledVoices().Where(v => v.Enabled)
                .FirstOrDefault(v => v.VoiceInfo.Culture.TwoLetterISOLanguageName == culture)?.VoiceInfo.Name ?? "";
        }
        if (voice != "" && _synth.Voice.Name != voice)
            try { _synth.SelectVoice(voice); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _synth.Dispose();
    }
}
