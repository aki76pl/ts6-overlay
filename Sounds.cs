using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TS6Overlay;

public enum SoundKind { Join, Leave, Favorite, Message, Poke, MuteWarning }

/// <summary>
/// Krótkie sygnały dźwiękowe z regulacją głośności. Domyślne dźwięki są generowane w pamięci.
/// Własne: join.wav / leave.wav / message.wav / poke.wav w %APPDATA%\TS6Overlay
/// albo osobny plik dla każdego ulubionego.
/// </summary>
public static class Sounds
{
    const int Rate = 44100;
    static readonly Dictionary<SoundKind, float[]> Cache = new();
    static WaveOutEvent? _out;
    static IDisposable? _reader;
    static readonly object Lock = new();

    public static int Volume { get; set; } = 60;

    public static void Play(SoundKind kind, string? customPath = null)
    {
        try
        {
            string? file = customPath;
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                string name = kind switch
                {
                    SoundKind.Join => "join", SoundKind.Leave => "leave", SoundKind.Message => "message",
                    SoundKind.Poke => "poke", SoundKind.Favorite => "favorite", _ => "mute",
                };
                file = new[] { ".wav", ".mp3" }.Select(e => Path.Combine(Config.Dir, name + e)).FirstOrDefault(File.Exists);
            }

            ISampleProvider src;
            AudioFileReader? reader = null;
            if (file != null)
                src = reader = new AudioFileReader(file);
            else
            {
                var data = Generated(kind);
                src = new RawSamples(data);
            }
            var vol = new VolumeSampleProvider(src) { Volume = Math.Clamp(Volume, 0, 100) / 100f };

            lock (Lock)
            {
                _out?.Stop();
                _out?.Dispose();
                _reader?.Dispose();
                _reader = reader;
                _out = new WaveOutEvent();
                _out.Init(vol);
                _out.Play();
            }
        }
        catch { }
    }

    static float[] Generated(SoundKind kind)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(kind, out var d)) return d;
            d = kind switch
            {
                SoundKind.Join => Tones(0.11, 659.25, 880.0),                 // E5 → A5
                SoundKind.Leave => Tones(0.11, 880.0, 587.33),                // A5 → D5
                SoundKind.Favorite => Tones(0.09, 659.25, 830.61, 987.77),    // E5 → G#5 → B5
                SoundKind.Message => Tones(0.14, 1046.5),                     // C6
                SoundKind.Poke => Tones(0.07, 1318.5, 0, 1318.5),             // podwójne „ding”
                _ => Tones(0.16, 220.0, 0, 220.0),                            // niskie ostrzeżenie
            };
            Cache[kind] = d;
            return d;
        }
    }

    /// <summary>Sekwencja tonów (0 = pauza) z łagodnym wygaszaniem.</summary>
    static float[] Tones(double noteSec, params double[] freqs)
    {
        int noteLen = (int)(Rate * noteSec);
        int total = noteLen * freqs.Length + noteLen * 2;
        var s = new float[total];
        for (int n = 0; n < freqs.Length; n++)
        {
            double f = freqs[n];
            if (f <= 0) continue;
            int start = n * noteLen;
            for (int i = 0; i < noteLen * 3 && start + i < total; i++)
            {
                double t = (double)i / Rate;
                double attack = Math.Min(1, i / (Rate * 0.005));
                double env = attack * Math.Exp(-t * 18);
                double v = (Math.Sin(2 * Math.PI * f * t) + 0.25 * Math.Sin(4 * Math.PI * f * t)) / 1.25;
                s[start + i] = (float)Math.Clamp(s[start + i] + v * env * 0.6, -1, 1);
            }
        }
        return s;
    }

    sealed class RawSamples(float[] data) : ISampleProvider
    {
        int _pos;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, data.Length - _pos);
            Array.Copy(data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }
}
