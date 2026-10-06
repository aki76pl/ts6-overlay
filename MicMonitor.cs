using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace TS6Overlay;

/// <summary>
/// Nasłuch domyślnego mikrofonu, uruchamiany tylko gdy masz wyciszony mikrofon w TS.
/// Dźwięk jest analizowany na bieżąco (sam poziom głośności) i nigdzie nie jest zapisywany.
/// </summary>
public sealed class MicMonitor : IDisposable
{
    WasapiCapture? _cap;
    bool _voice;
    DateTime _lastLoud = DateTime.MinValue, _loudSince = DateTime.MinValue;

    /// <summary>Próg 0..1 szczytowej amplitudy.</summary>
    public float Threshold { get; set; } = 0.12f;
    /// <summary>true = wykryto mowę, false = cisza. Wywoływane z wątku audio.</summary>
    public event Action<bool>? VoiceChanged;
    /// <summary>Bieżący poziom 0..1 (do podglądu w ustawieniach).</summary>
    public float Level { get; private set; }
    public bool Running => _cap != null;

    public void Start()
    {
        if (_cap != null) return;
        try
        {
            var dev = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            _cap = new WasapiCapture(dev) { ShareMode = AudioClientShareMode.Shared };
            _cap.DataAvailable += OnData;
            _cap.RecordingStopped += (_, _) => SetVoice(false);
            _cap.StartRecording();
        }
        catch
        {
            _cap?.Dispose();
            _cap = null;
        }
    }

    public void Stop()
    {
        var c = _cap;
        _cap = null;
        if (c == null) return;
        try { c.StopRecording(); } catch { }
        c.Dispose();
        Level = 0;
        SetVoice(false);
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiCapture cap) return;
        var f = cap.WaveFormat;
        float peak = 0;
        if (f.Encoding == WaveFormatEncoding.IeeeFloat || (f.Encoding == WaveFormatEncoding.Extensible && f.BitsPerSample == 32))
        {
            for (int i = 0; i + 4 <= e.BytesRecorded; i += 4)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(e.Buffer, i)));
        }
        else if (f.BitsPerSample == 16)
        {
            for (int i = 0; i + 2 <= e.BytesRecorded; i += 2)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f));
        }
        Level = peak;

        var now = DateTime.UtcNow;
        if (peak >= Threshold)
        {
            if (_loudSince == DateTime.MinValue) _loudSince = now;
            _lastLoud = now;
            // co najmniej 150 ms głośno = mowa, nie stuknięcie w biurko
            if ((now - _loudSince).TotalMilliseconds >= 150) SetVoice(true);
        }
        else
        {
            if ((now - _lastLoud).TotalMilliseconds > 250) _loudSince = DateTime.MinValue;
            if ((now - _lastLoud).TotalMilliseconds > 700) SetVoice(false);
        }
    }

    void SetVoice(bool v)
    {
        if (v == _voice) return;
        _voice = v;
        VoiceChanged?.Invoke(v);
    }

    public void Dispose() => Stop();
}
