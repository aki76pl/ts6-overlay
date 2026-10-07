using System.IO;
using System.Net.Sockets;
using System.Text;
using static TS6Overlay.L;

namespace TS6Overlay;

/// <summary>
/// Błyski podświetlenia klawiatury/myszy przez serwer SDK OpenRGB (domyślnie 127.0.0.1:6742).
/// OpenRGB obsługuje sprzęt wielu marek naraz. Przed błyskiem zapisujemy bieżący stan jako profil OpenRGB,
/// a po błysku go wczytujemy — Twoje ustawienia podświetlenia wracają bez zmian.
/// </summary>
public sealed class Rgb
{
    const string RestoreProfile = "TS6Overlay-restore";
    const uint ClientProtocol = 2;   // 2 = profile (zapis/odczyt); układ danych urządzeń jak w wersji 1

    // identyfikatory pakietów protokołu OpenRGB
    const uint RequestControllerCount = 0, RequestControllerData = 1, RequestProtocolVersion = 40, SetClientName = 50,
        SaveProfile = 151, LoadProfile = 152, UpdateLeds = 1050, SetCustomMode = 1100;

    readonly Config _cfg;
    readonly SemaphoreSlim _busy = new(1, 1);

    public sealed record Device(int Index, string Name, int Leds);

    /// <summary>Ostatnio wykryte urządzenia (do wyświetlenia w ustawieniach).</summary>
    public List<Device> Devices { get; private set; } = new();
    public string Status { get; private set; } = "";

    public Rgb(Config cfg) => _cfg = cfg;

    /// <summary>Błyska kolorem (#AARRGGBB albo #RRGGBB). Nie blokuje; gdy trwa poprzedni błysk — pomija.</summary>
    public void Flash(string color)
    {
        if (!TryColor(color, out var rgb)) return;
        if (!_busy.Wait(0)) return;
        _ = Task.Run(async () =>
        {
            try { await FlashCore(rgb); }
            catch (Exception ex) { Status = T("Błąd OpenRGB: {0}", ex.Message); }
            finally { _busy.Release(); }
        });
    }

    /// <summary>Sprawdza połączenie i odświeża listę urządzeń.</summary>
    public async Task<bool> Probe()
    {
        try
        {
            using var c = await Connect();
            Devices = await ReadDevices(c);
            Status = Devices.Count == 0 ? T("OpenRGB działa, ale nie widzi żadnych urządzeń RGB.") : T("Połączono z OpenRGB.");
            return Devices.Count > 0;
        }
        catch (Exception ex)
        {
            Devices = new();
            Status = T("Brak połączenia z OpenRGB: {0}", ex.Message);
            return false;
        }
    }

    async Task FlashCore((byte r, byte g, byte b) color)
    {
        using var c = await Connect();
        var devices = await ReadDevices(c);
        Devices = devices;
        if (devices.Count == 0) { Status = T("OpenRGB działa, ale nie widzi żadnych urządzeń RGB."); return; }

        // 1) zapamiętaj bieżący stan, 2) tryb bezpośredni, 3) błyski, 4) przywróć
        await c.Send(0, SaveProfile, Z(RestoreProfile));
        await Task.Delay(120);
        foreach (var d in devices) await c.Send((uint)d.Index, SetCustomMode, Array.Empty<byte>());
        int flashes = Math.Clamp(_cfg.RgbFlashes, 1, 10);
        int ms = Math.Clamp(_cfg.RgbFlashMs, 60, 1000);
        for (int i = 0; i < flashes; i++)
        {
            foreach (var d in devices) await c.Send((uint)d.Index, UpdateLeds, Leds(d.Leds, color));
            await Task.Delay(ms);
            foreach (var d in devices) await c.Send((uint)d.Index, UpdateLeds, Leds(d.Leds, (0, 0, 0)));
            await Task.Delay(ms * 2 / 3);
        }
        await c.Send(0, LoadProfile, Z(RestoreProfile));
        await Task.Delay(100);
        Status = T("Połączono z OpenRGB.");
    }

    static byte[] Z(string s) => Encoding.UTF8.GetBytes(s + "\0");

    /// <summary>Dane UPDATELEDS: rozmiar, liczba kolorów, kolory RGBA.</summary>
    static byte[] Leds(int count, (byte r, byte g, byte b) c)
    {
        var data = new byte[4 + 2 + 4 * count];
        BitConverter.GetBytes(data.Length).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)count).CopyTo(data, 4);
        for (int i = 0; i < count; i++)
        {
            data[6 + 4 * i] = c.r;
            data[7 + 4 * i] = c.g;
            data[8 + 4 * i] = c.b;
        }
        return data;
    }

    static bool TryColor(string hex, out (byte r, byte g, byte b) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            var c = Theme.C(hex);
            rgb = (c.R, c.G, c.B);
            return true;
        }
        catch { return false; }
    }

    // ---------------- połączenie ----------------

    async Task<Conn> Connect()
    {
        var tcp = new TcpClient();
        var connect = tcp.ConnectAsync(_cfg.RgbHost, _cfg.RgbPort);
        if (await Task.WhenAny(connect, Task.Delay(1500)) != connect) { tcp.Dispose(); throw new TimeoutException(T("OpenRGB nie odpowiada.")); }
        await connect;
        var c = new Conn(tcp);
        await c.Send(0, SetClientName, Z("TS6 Overlay"));
        await c.Send(0, RequestProtocolVersion, BitConverter.GetBytes(ClientProtocol));
        var (_, ver) = await c.Receive(RequestProtocolVersion);
        c.Protocol = Math.Min(ClientProtocol, BitConverter.ToUInt32(ver));
        if (c.Protocol < 2) throw new InvalidDataException(T("Zbyt stara wersja OpenRGB — zainstaluj 0.8 lub nowszą."));
        return c;
    }

    static async Task<List<Device>> ReadDevices(Conn c)
    {
        await c.Send(0, RequestControllerCount, Array.Empty<byte>());
        var (_, cnt) = await c.Receive(RequestControllerCount);
        int count = (int)BitConverter.ToUInt32(cnt);
        var list = new List<Device>();
        for (int i = 0; i < count; i++)
        {
            await c.Send((uint)i, RequestControllerData, BitConverter.GetBytes(c.Protocol));
            var (_, data) = await c.Receive(RequestControllerData);
            var (name, leds) = ParseController(data, c.Protocol);
            if (leds > 0) list.Add(new Device(i, name, leds));
        }
        return list;
    }

    /// <summary>Nazwa i liczba diod z bloku danych urządzenia (układ protokołu 1–2).</summary>
    static (string name, int leds) ParseController(byte[] d, uint proto)
    {
        int p = 0;
        ushort U16() { var v = BitConverter.ToUInt16(d, p); p += 2; return v; }
        uint U32() { var v = BitConverter.ToUInt32(d, p); p += 4; return v; }
        string Str() { int n = U16(); var s = Encoding.UTF8.GetString(d, p, n).TrimEnd('\0'); p += n; return s; }

        U32();                       // rozmiar danych
        U32();                       // typ urządzenia
        string name = Str();
        string vendor = proto >= 1 ? Str() : "";
        Str(); Str(); Str(); Str();  // opis, wersja, numer seryjny, lokalizacja
        int modes = U16();
        U32();                       // aktywny tryb
        for (int m = 0; m < modes; m++)
        {
            Str();                   // nazwa trybu
            p += 4 * 9;              // value, flags, speed min/max, colors min/max, speed, direction, color_mode
            int nc = U16();
            p += 4 * nc;
        }
        int zones = U16();
        for (int z = 0; z < zones; z++)
        {
            Str();
            p += 4 * 4;              // typ, leds min/max/count
            int matrix = U16();
            p += matrix;
        }
        int leds = U16();
        return ((vendor != "" ? vendor + " " : "") + name, leds);
    }

    sealed class Conn : IDisposable
    {
        readonly TcpClient _tcp;
        readonly NetworkStream _s;
        public uint Protocol;

        public Conn(TcpClient tcp) { _tcp = tcp; _s = tcp.GetStream(); }

        public async Task Send(uint dev, uint id, byte[] data)
        {
            var head = new byte[16];
            Encoding.ASCII.GetBytes("ORGB").CopyTo(head, 0);
            BitConverter.GetBytes(dev).CopyTo(head, 4);
            BitConverter.GetBytes(id).CopyTo(head, 8);
            BitConverter.GetBytes((uint)data.Length).CopyTo(head, 12);
            await _s.WriteAsync(head);
            if (data.Length > 0) await _s.WriteAsync(data);
        }

        /// <summary>Czeka na odpowiedź o danym id (inne pakiety, np. powiadomienia, pomija).</summary>
        public async Task<(uint dev, byte[] data)> Receive(uint expected)
        {
            using var cts = new CancellationTokenSource(3000);
            while (true)
            {
                var head = await Read(16, cts.Token);
                if (Encoding.ASCII.GetString(head, 0, 4) != "ORGB") throw new InvalidDataException("bad header");
                uint dev = BitConverter.ToUInt32(head, 4), id = BitConverter.ToUInt32(head, 8), size = BitConverter.ToUInt32(head, 12);
                var data = size > 0 ? await Read((int)size, cts.Token) : Array.Empty<byte>();
                if (id == expected) return (dev, data);
            }
        }

        async Task<byte[]> Read(int n, CancellationToken ct)
        {
            var buf = new byte[n];
            int read = 0;
            while (read < n)
            {
                int k = await _s.ReadAsync(buf.AsMemory(read, n - read), ct);
                if (k == 0) throw new EndOfStreamException();
                read += k;
            }
            return buf;
        }

        public void Dispose() => _tcp.Dispose();
    }
}
