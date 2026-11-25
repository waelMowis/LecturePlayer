using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LecturePlayer.Data;

/// <summary>
/// تسجيل صوت (WASAPI): المايك، أو صوت الجهاز نفسه (loopback). كل واحد في ملف WAV لوحده ويتدمجوا مع الصورة في الآخر.
/// صوت الجهاز مابيبعتش حاجة وهو ساكت — فبنملا السكوت بأصفار عشان الملف يفضل ماشي مع الصورة.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    public sealed record Device(string Id, string Name);

    public static List<Device> Microphones()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            return en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Select(d => new Device(d.ID, d.FriendlyName)).ToList();
        }
        catch { return new(); }
    }

    public static string? DefaultMicrophone()
    {
        try { using var en = new MMDeviceEnumerator(); return en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID; } catch { return null; }
    }

    private readonly string _file;
    private readonly bool _loopback;
    private readonly string? _deviceId;
    private IWaveIn? _cap;
    private WaveFileWriter? _w;
    private readonly object _lock = new();
    private bool _active;
    private readonly Stopwatch _seg = new();
    private long _segBytes;
    public float Level { get; private set; }
    public string File => _file;
    public bool HasData { get; private set; }
    public string? Error { get; private set; }

    public AudioRecorder(string file, string? deviceId, bool loopback) { _file = file; _deviceId = deviceId; _loopback = loopback; }

    /// <summary>بيفتح الجهاز (لسه مابيكتبش — Begin بيبدأ الكتابة مع الصورة).</summary>
    public bool Open()
    {
        try
        {
            if (_loopback) _cap = new WasapiLoopbackCapture();
            else
            {
                using var en = new MMDeviceEnumerator();
                var dev = _deviceId != null ? en.GetDevice(_deviceId) : en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                _cap = new WasapiCapture(dev);
            }
            _w = new WaveFileWriter(_file, _cap.WaveFormat);
            _cap.DataAvailable += OnData;
            _cap.RecordingStopped += (_, e) => { if (e.Exception != null) Error = e.Exception.Message; };
            _cap.StartRecording();
            return true;
        }
        catch (Exception ex) { Error = ex.Message; Log.Info($"audio {(_loopback ? "system" : "mic")}: " + ex.Message); Dispose(); return false; }
    }

    public void Begin() { lock (_lock) { _active = true; _seg.Restart(); _segBytes = 0; } }
    public void Pause() { lock (_lock) { FillTo(_seg.Elapsed); _active = false; _seg.Reset(); } }

    private void OnData(object? s, WaveInEventArgs e)
    {
        float peak = 0;
        if (_w != null && _w.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            for (int i = 0; i + 4 <= e.BytesRecorded; i += 4 * Math.Max(1, _w.WaveFormat.Channels)) peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(e.Buffer, i)));
        Level = peak;
        lock (_lock)
        {
            if (!_active || _w == null || e.BytesRecorded == 0) return;
            if (_loopback)
            {
                // الجهاز كان ساكت (مفيش بيانات): نملا المدة دي سكوت قبل الصوت الجديد
                var incoming = TimeSpan.FromSeconds((double)e.BytesRecorded / _w.WaveFormat.AverageBytesPerSecond);
                FillTo(_seg.Elapsed - incoming - TimeSpan.FromMilliseconds(50));
            }
            _w.Write(e.Buffer, 0, e.BytesRecorded);
            _segBytes += e.BytesRecorded;
            HasData = true;
        }
    }

    /// <summary>سكوت لحد وقت معين من بداية الجزء (لصوت الجهاز بس).</summary>
    private void FillTo(TimeSpan t)
    {
        if (!_loopback || _w == null || t <= TimeSpan.Zero) return;
        var f = _w.WaveFormat;
        long want = (long)(t.TotalSeconds * f.AverageBytesPerSecond);
        want -= want % f.BlockAlign;
        long missing = want - _segBytes;
        if (missing < f.AverageBytesPerSecond / 10) return; // أقل من 0.1 ثانية: عادي
        var zeros = new byte[Math.Min(missing, 1 << 16)];
        while (missing > 0)
        {
            int n = (int)Math.Min(missing, zeros.Length);
            _w.Write(zeros, 0, n);
            missing -= n; _segBytes += n;
        }
        HasData = true;
    }

    public void Stop()
    {
        lock (_lock) { if (_active) FillTo(_seg.Elapsed); _active = false; }
        try { _cap?.StopRecording(); } catch { }
        Thread.Sleep(100);
        lock (_lock) { try { _w?.Dispose(); } catch { } _w = null; }
    }

    public void Dispose()
    {
        try { _cap?.StopRecording(); } catch { }
        try { _cap?.Dispose(); } catch { }
        lock (_lock) { try { _w?.Dispose(); } catch { } _w = null; }
        _cap = null;
    }
}

/// <summary>
/// الكاميرا بـ ffmpeg (DirectShow): صورة صغيرة لشباك الكاميرا (BGRA على stdout) + ملف لوحده لو عايز تركّب المشهد بعدين.
/// </summary>
public sealed class CameraFeed : IDisposable
{
    public const int PreviewW = 480, PreviewH = 270;
    private readonly string _ffmpeg;
    private Process? _proc;
    private Thread? _reader;
    public event Action<byte[]>? Frame;
    public event Action<string>? Failed;
    public string? Device { get; private set; }
    public string? TestInput { get; set; }
    public List<string> Files { get; } = new();
    public bool Running => _proc is { HasExited: false };

    public CameraFeed(string ffmpeg) { _ffmpeg = ffmpeg; }

    /// <summary>بيشغّل الكاميرا. file = ملف جزء جديد (أو null = معاينة بس).</summary>
    public void Start(string device, bool mirror, string? file, string encoderArgs)
    {
        Stop();
        Device = device;
        string input = TestInput ?? $"-f dshow -rtbufsize 200M -i video=\"{device.Replace("\"", "")}\"";
        string prev = $"scale={PreviewW}:{PreviewH}:force_original_aspect_ratio=increase,crop={PreviewW}:{PreviewH}{(mirror ? ",hflip" : "")},format=bgra";
        string args = file == null
            ? $"-hide_banner -v error {input} -vf {prev} -f rawvideo pipe:1"
            : $"-hide_banner -v error -y {input} -filter_complex \"[0:v]split=2[p][r];[p]{prev}[pv];[r]scale=trunc(iw/2)*2:trunc(ih/2)*2[rv]\" -map \"[pv]\" -f rawvideo pipe:1 -map \"[rv]\" {encoderArgs} -movflags +faststart \"{file}\"";
        if (file != null) Files.Add(file);
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(_ffmpeg, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true },
            EnableRaisingEvents = true
        };
        var err = new System.Text.StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null && err.Length < 4000) err.AppendLine(e.Data); };
        p.Exited += (_, _) => { if (_proc == p) Failed?.Invoke(err.ToString().Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "الكاميرا وقفت"); };
        p.Start();
        p.BeginErrorReadLine();
        _proc = p;
        var stream = p.StandardOutput.BaseStream;
        _reader = new Thread(() =>
        {
            int size = PreviewW * PreviewH * 4;
            var buf = new byte[size];
            try
            {
                while (true)
                {
                    int got = 0;
                    while (got < size) { int n = stream.Read(buf, got, size - got); if (n <= 0) return; got += n; }
                    Frame?.Invoke((byte[])buf.Clone());
                }
            }
            catch { }
        }) { IsBackground = true, Name = "camera" };
        _reader.Start();
    }

    public void Stop()
    {
        var p = _proc; _proc = null;
        if (p == null) return;
        try { p.StandardInput.Write("q"); p.StandardInput.Flush(); } catch { }
        if (!p.WaitForExit(8000)) try { p.Kill(true); } catch { }
        p.Dispose();
    }

    /// <summary>ملفات الأجزاء ملزوقة (أو null لو مفيش).</summary>
    public async Task<string?> JoinAsync(string workDir)
    {
        var segs = Files.Where(f => System.IO.File.Exists(f) && new FileInfo(f).Length > 0).ToList();
        if (segs.Count == 0) return null;
        if (segs.Count == 1) return segs[0];
        string list = Path.Combine(workDir, "camera.txt"), outFile = Path.Combine(workDir, "camera.mp4");
        System.IO.File.WriteAllLines(list, segs.Select(s => $"file '{s.Replace("'", "'\\''")}'"));
        var r = await ScreenRecorder.RunAsync(_ffmpeg, $"-hide_banner -v error -y -f concat -safe 0 -i \"{list}\" -c copy \"{outFile}\"", 120_000);
        return r.Code == 0 ? outFile : segs[0];
    }

    public void Dispose() => Stop();
}
