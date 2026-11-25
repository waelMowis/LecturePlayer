using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>
/// تسجيل الشاشة بـ ffmpeg: Desktop Duplication (ddagrab) على كارت الشاشة، ولو مانفعش gdigrab.
/// الإيقاف المؤقت = جزء جديد، والأجزاء بتتلزق في الآخر. الصوت (مايك/صوت الجهاز) بيتسجل لوحده (AudioRecorder) ويتدمج.
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    /// <summary>المساحة بإحداثيات الشاشة الافتراضية (كل الشاشات) + رقم الشاشة لـ ddagrab.</summary>
    public sealed record Area(int X, int Y, int W, int H, int Monitor, int MonitorX, int MonitorY);
    public sealed record Options(Area Area, int Fps, string WorkDir, bool DrawMouse = true, string? ForceInput = null);
    public sealed record Mark(long Ms, string Note);

    private readonly string _ffmpeg;
    private readonly Options _o;
    private Process? _proc;
    private readonly List<string> _segments = new();
    /// <summary>وقت الأجزاء اللي خلصت + ساعة الجزء الحالي.</summary>
    private TimeSpan _acc;
    private readonly Stopwatch _seg = new();
    private readonly List<Mark> _marks = new();
    private readonly StringBuilder _log = new();
    public bool Running => _proc != null;
    public bool Paused { get; private set; }
    public TimeSpan Elapsed => _acc + _seg.Elapsed;
    public IReadOnlyList<Mark> Marks => _marks;
    public IReadOnlyList<string> Segments => _segments;
    /// <summary>بداية كل جزء (Stopwatch.GetTimestamp) — عشان ملفات الكاميرا تتظبط عليها.</summary>
    public List<long> SegmentStarts { get; } = new();
    public string? Encoder { get; private set; }
    public string? Source { get; private set; }
    public string LastLog => _log.ToString();
    /// <summary>أول إطار اتكتب (عشان الصوت يبدأ معاه).</summary>
    public event Action? SegmentStarted;
    public event Action<string>? Failed;

    public ScreenRecorder(string ffmpeg, Options o) { _ffmpeg = ffmpeg; _o = o; Directory.CreateDirectory(o.WorkDir); }

    // ---------------- اختيار الإنكودر والمصدر (بتجربة فعلية، ومتخزنة) ----------------
    private static string? _encoder;
    private static bool? _ddagrab;

    /// <summary>أول إنكودر H.264 بيشتغل فعلًا على الجهاز ده (NVIDIA ← Intel ← AMD ← ويندوز) — وإلا mpeg4.</summary>
    public static async Task<string> PickEncoderAsync(string ffmpeg)
    {
        if (_encoder != null) return _encoder;
        foreach (var enc in new[] { "h264_nvenc", "h264_qsv", "h264_amf", "h264_mf" })
        {
            var r = await RunAsync(ffmpeg, $"-hide_banner -v error -f lavfi -i color=c=black:s=320x240:r=30:d=0.3 -c:v {enc} -pix_fmt nv12 -f null -", 20_000);
            if (r.Code == 0) return _encoder = enc;
        }
        return _encoder = "mpeg4";
    }

    public static async Task<bool> DdagrabWorksAsync(string ffmpeg)
    {
        if (_ddagrab is { } d) return d;
        var r = await RunAsync(ffmpeg, "-hide_banner -v error -f lavfi -i ddagrab=output_idx=0:framerate=5 -vf hwdownload,format=bgra -frames:v 2 -f null -", 20_000);
        return (bool)(_ddagrab = r.Code == 0);
    }

    internal static void ResetProbes() { _encoder = null; _ddagrab = null; }

    private static string EncoderArgs(string enc, int fps) => enc switch
    {
        "h264_nvenc" => "-c:v h264_nvenc -preset p4 -cq 23 -pix_fmt nv12",
        "h264_qsv" => "-c:v h264_qsv -global_quality 23 -pix_fmt nv12",
        "h264_amf" => "-c:v h264_amf -quality balanced -rc cqp -qp_i 22 -qp_p 24 -pix_fmt nv12",
        "h264_mf" => "-c:v h264_mf -rate_control quality -quality 75 -pix_fmt nv12",
        _ => "-c:v mpeg4 -q:v 3 -pix_fmt yuv420p"
    } + $" -g {fps * 2}";

    private string InputArgs()
    {
        var a = _o.Area;
        int w = a.W & ~1, h = a.H & ~1; // الإنكودرات عايزة أبعاد زوجية
        if (_o.ForceInput != null) return _o.ForceInput;
        if (Source == "ddagrab")
            return $"-f lavfi -i ddagrab=output_idx={a.Monitor}:framerate={_o.Fps}:draw_mouse={(_o.DrawMouse ? 1 : 0)}:offset_x={a.X - a.MonitorX}:offset_y={a.Y - a.MonitorY}:video_size={w}x{h} -vf hwdownload,format=bgra";
        return $"-f gdigrab -framerate {_o.Fps} -draw_mouse {(_o.DrawMouse ? 1 : 0)} -offset_x {a.X} -offset_y {a.Y} -video_size {w}x{h} -i desktop";
    }

    public async Task StartAsync()
    {
        Encoder = await PickEncoderAsync(_ffmpeg);
        Source = _o.ForceInput != null ? "test" : await DdagrabWorksAsync(_ffmpeg) ? "ddagrab" : "gdigrab";
        await StartSegmentAsync();
    }

    private async Task StartSegmentAsync()
    {
        string seg = Path.Combine(_o.WorkDir, $"seg{_segments.Count + 1:00}.mp4");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(_ffmpeg, $"-hide_banner -y {InputArgs()} {EncoderArgs(Encoder!, _o.Fps)} -movflags +faststart \"{seg}\"")
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (_log) { if (_log.Length < 20_000) _log.AppendLine(e.Data); }
            if (e.Data.StartsWith("frame=", StringComparison.Ordinal)) started.TrySetResult();
        };
        p.Exited += (_, _) => { started.TrySetResult(); if (_proc == p && !_stopping) Failed?.Invoke("التسجيل وقف لوحده: " + LastLine()); };
        if (!p.Start()) throw new InvalidOperationException("ffmpeg ماشتغلش");
        // الساعة والصوت بيبدأوا مع العملية نفسها (تقرير ffmpeg عن أول إطار بيتأخر نص ثانية)
        _seg.Restart();
        SegmentStarts.Add(Stopwatch.GetTimestamp());
        Paused = false;
        SegmentStarted?.Invoke();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        _proc = p;
        _segments.Add(seg);
        await Task.WhenAny(started.Task, Task.Delay(15_000));
        if (p.HasExited)
        {
            _seg.Reset();
            _proc = null;
            _segments.Remove(seg);
            SegmentStarts.RemoveAt(SegmentStarts.Count - 1);
            // ddagrab ممكن يقع (مثلًا شاشة مقفولة/RDP): نجرب gdigrab مرة
            if (Source == "ddagrab") { Source = "gdigrab"; _ddagrab = false; await StartSegmentAsync(); return; }
            throw new InvalidOperationException("ffmpeg مقدرش يسجل: " + LastLine());
        }
    }

    private string LastLine()
    {
        lock (_log)
        {
            var lines = _log.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.LastOrDefault(l => !l.StartsWith("frame=")) ?? "";
        }
    }

    private bool _stopping;
    private async Task StopSegmentAsync()
    {
        var p = _proc; if (p == null) return;
        _stopping = true;
        _acc += _seg.Elapsed;
        _seg.Reset();
        try
        {
            try { await p.StandardInput.WriteAsync("q"); await p.StandardInput.FlushAsync(); } catch { }
            var done = Task.Run(() => p.WaitForExit(15_000));
            if (!await done) { try { p.Kill(true); } catch { } }
        }
        finally { _proc = null; _stopping = false; p.Dispose(); }
    }

    public async Task PauseAsync() { if (Paused || _proc == null) return; await StopSegmentAsync(); Paused = true; }
    public async Task ResumeAsync() { if (!Paused) return; await StartSegmentAsync(); }

    public void AddMark(string note)
    {
        long ms = (long)Elapsed.TotalMilliseconds;
        if (_marks.Count > 0 && ms - _marks[^1].Ms < 1500) _marks[^1] = new Mark(_marks[^1].Ms, note); // كذا حدث ورا بعض: آخرهم
        else _marks.Add(new Mark(ms, note));
    }

    /// <summary>بيوقف ويرجّع ملف الصورة كلها (الأجزاء ملزوقة).</summary>
    public async Task<string> StopAsync()
    {
        if (_proc != null) await StopSegmentAsync();
        var segs = _segments.Where(s => File.Exists(s) && new FileInfo(s).Length > 0).ToList();
        if (segs.Count == 0) throw new InvalidOperationException("مفيش حاجة اتسجلت");
        if (segs.Count == 1) return segs[0];
        string list = Path.Combine(_o.WorkDir, "segments.txt");
        File.WriteAllLines(list, segs.Select(s => $"file '{s.Replace("'", "'\\''")}'"));
        string outFile = Path.Combine(_o.WorkDir, "screen.mp4");
        var r = await RunAsync(_ffmpeg, $"-hide_banner -v error -y -f concat -safe 0 -i \"{list}\" -c copy \"{outFile}\"", 120_000);
        if (r.Code != 0) throw new InvalidOperationException("مقدرتش ألزق أجزاء التسجيل: " + r.Err);
        return outFile;
    }

    public void Dispose()
    {
        try { if (_proc is { HasExited: false } p) p.Kill(true); } catch { }
        _proc = null;
    }

    // ---------------- أدوات ffmpeg ----------------
    public sealed record Result(int Code, string Err, string Out);

    public static async Task<Result> RunAsync(string ffmpeg, string args, int timeoutMs)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg, args) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true, StandardErrorEncoding = Encoding.UTF8 }
        };
        var err = new StringBuilder(); var outp = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null && err.Length < 40_000) err.AppendLine(e.Data); };
        p.OutputDataReceived += (_, e) => { if (e.Data != null && outp.Length < 40_000) outp.AppendLine(e.Data); };
        p.Start(); p.BeginErrorReadLine(); p.BeginOutputReadLine();
        bool done = await Task.Run(() => p.WaitForExit(timeoutMs));
        if (!done) { try { p.Kill(true); } catch { } return new Result(-1, "timeout", ""); }
        p.WaitForExit();
        return new Result(p.ExitCode, err.ToString(), outp.ToString());
    }

    /// <summary>مدة الملف بالثواني (من «Duration» في ffmpeg -i).</summary>
    public static async Task<double> DurationAsync(string ffmpeg, string file)
    {
        var r = await RunAsync(ffmpeg, $"-hide_banner -i \"{file}\"", 20_000);
        var m = Regex.Match(r.Err, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        return m.Success ? int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
    }

    /// <summary>أجهزة الكاميرا (DirectShow).</summary>
    public static async Task<List<string>> CamerasAsync(string ffmpeg)
    {
        var r = await RunAsync(ffmpeg, "-hide_banner -list_devices true -f dshow -i dummy", 15_000);
        var list = new List<string>();
        foreach (var line in r.Err.Split('\n'))
        {
            // "[dshow @ ...] "Integrated Camera" (video)"
            var m = Regex.Match(line, "\"([^\"]+)\"\\s*\\(video\\)");
            if (m.Success) list.Add(m.Groups[1].Value);
        }
        return list.Distinct().ToList();
    }
}

/// <summary>التشطيب: الصوت مع الصورة، المشاهد (الكاميرا كبيرة/صغيرة)، القص.</summary>
public static class RecordingEdit
{
    public enum Scene { ScreenWithCam, CamBigScreenSmall, CamOnly, ScreenOnly }
    public enum Corner { BottomRight, BottomLeft, TopRight, TopLeft }

    static string Q(string s) => "\"" + s + "\"";
    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// الملف النهائي: الصورة (والكاميرا لو المشهد محتاجها) + الصوت (مايك و/أو صوت الجهاز، متزامنين ببداية كل واحد).
    /// audio = (ملف، تأخير عن بداية الصورة بالثواني).
    /// </summary>
    public static async Task ComposeAsync(string ffmpeg, string screen, string? camera, Scene scene, Corner corner, double camSize,
        IReadOnlyList<(string File, double Offset)> audio, string encoderArgs, string outFile, bool camInScreen = true)
    {
        var args = new StringBuilder("-hide_banner -v error -y ");
        var inputs = new List<string> { screen };
        // camInScreen = فقاعة الكاميرا كانت ظاهرة في تسجيل الشاشة نفسه؛ لو لأ و«الشاشة + الكاميرا صغيرة» ← نركّبها دايرة من ملف الكاميرا
        bool useCam = camera != null && File.Exists(camera) && scene != Scene.ScreenOnly && (scene != Scene.ScreenWithCam || !camInScreen);
        if (useCam) inputs.Add(camera!);
        foreach (var i in inputs) args.Append($"-i {Q(i)} ");
        foreach (var (f, off) in audio) args.Append(off > 0.01 ? $"-itsoffset {F(off)} -i {Q(f)} " : $"-i {Q(f)} ");
        var fc = new List<string>();
        string vmap = "0:v";
        if (useCam)
        {
            if (scene == Scene.CamOnly) { fc.Add("[1:v]scale=trunc(iw/2)*2:trunc(ih/2)*2[v]"); vmap = "[v]"; }
            else if (scene == Scene.ScreenWithCam)
            {
                // دايرة الكاميرا في الركن، حجمها نسبة من طول الشاشة
                string pos = Pos(corner);
                fc.Add("[1:v]crop='min(iw,ih)':'min(iw,ih)'[sq];" +
                       $"[sq][0:v]scale2ref=w='trunc(main_h*{F(camSize)}/2)*2':h='trunc(main_h*{F(camSize)}/2)*2'[c0][scr];" +
                       "[c0]format=yuva444p,geq=lum='p(X,Y)':cb='p(X,Y)':cr='p(X,Y)':a='if(lte(hypot(X-W/2,Y-H/2),W/2-1),255,0)'[cam];" +
                       $"[scr][cam]overlay={pos}:format=auto,format=yuv420p[v]");
                vmap = "[v]";
            }
            else
            {
                // الكاميرا تملا الصورة والشاشة صغيرة في الركن
                string pos = Pos(corner);
                fc.Add($"[1:v]scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720[cam];[0:v]scale=iw*{F(camSize)}:-2[scr];[cam][scr]overlay={pos}[v]");
                vmap = "[v]";
            }
        }
        int a0 = inputs.Count;
        string? amap = null;
        if (audio.Count == 1) amap = $"{a0}:a";
        else if (audio.Count > 1)
        {
            fc.Add(string.Concat(Enumerable.Range(0, audio.Count).Select(i => $"[{a0 + i}:a]")) + $"amix=inputs={audio.Count}:duration=longest:normalize=0[a]");
            amap = "[a]";
        }
        if (fc.Count > 0) args.Append($"-filter_complex {Q(string.Join(";", fc))} ");
        args.Append($"-map {(vmap.StartsWith('[') ? Q(vmap) : vmap)} ");
        if (amap != null) args.Append($"-map {(amap.StartsWith('[') ? Q(amap) : amap)} -c:a aac -b:a 160k ");
        args.Append(vmap == "0:v" ? "-c:v copy " : encoderArgs + " ");
        args.Append($"-shortest -movflags +faststart {Q(outFile)}");
        var r = await ScreenRecorder.RunAsync(ffmpeg, args.ToString(), 3_600_000);
        if (r.Code != 0 || !File.Exists(outFile)) throw new InvalidOperationException("مقدرتش أجهّز الفيديو: " + r.Err.Split('\n').FirstOrDefault(l => l.Trim().Length > 0));
    }

    static string Pos(Corner c) => c switch
    {
        Corner.BottomLeft => "24:H-h-24",
        Corner.TopRight => "W-w-24:24",
        Corner.TopLeft => "24:24",
        _ => "W-w-24:H-h-24"
    };

    /// <summary>قص البداية والنهاية وأجزاء من النص (ثواني). الباقي بيتلزق.</summary>
    public static List<(double From, double To)> Keep(double duration, double start, double end, IEnumerable<(double From, double To)> cuts)
    {
        end = end <= 0 || end > duration ? duration : end;
        var keep = new List<(double, double)> { (Math.Max(0, start), end) };
        foreach (var (cf, ct) in cuts.Where(c => c.To > c.From).OrderBy(c => c.From))
        {
            var next = new List<(double, double)>();
            foreach (var (f, t) in keep)
            {
                if (ct <= f || cf >= t) { next.Add((f, t)); continue; }
                if (cf > f) next.Add((f, cf));
                if (ct < t) next.Add((ct, t));
            }
            keep = next;
        }
        return keep.Where(k => k.Item2 - k.Item1 > 0.05).ToList();
    }

    public static async Task TrimAsync(string ffmpeg, string input, IReadOnlyList<(double From, double To)> keep, bool hasAudio, string encoderArgs, string outFile)
    {
        if (keep.Count == 0) throw new InvalidOperationException("مفيش حاجة هتفضل بعد القص");
        var fc = new StringBuilder();
        for (int i = 0; i < keep.Count; i++)
        {
            fc.Append($"[0:v]trim={F(keep[i].From)}:{F(keep[i].To)},setpts=PTS-STARTPTS[v{i}];");
            if (hasAudio) fc.Append($"[0:a]atrim={F(keep[i].From)}:{F(keep[i].To)},asetpts=PTS-STARTPTS[a{i}];");
        }
        fc.Append(string.Concat(Enumerable.Range(0, keep.Count).Select(i => hasAudio ? $"[v{i}][a{i}]" : $"[v{i}]")));
        fc.Append($"concat=n={keep.Count}:v=1:a={(hasAudio ? 1 : 0)}[v]{(hasAudio ? "[a]" : "")}");
        string args = $"-hide_banner -v error -y -i {Q(input)} -filter_complex {Q(fc.ToString())} -map \"[v]\" {(hasAudio ? "-map \"[a]\" -c:a aac -b:a 160k" : "")} {encoderArgs} -movflags +faststart {Q(outFile)}";
        var r = await ScreenRecorder.RunAsync(ffmpeg, args, 3_600_000);
        if (r.Code != 0 || !File.Exists(outFile)) throw new InvalidOperationException("القص فشل: " + r.Err.Split('\n').FirstOrDefault(l => l.Trim().Length > 0));
    }

    /// <summary>العلامات بعد القص: اللي في جزء اتشال بتتشال، والباقي بيتزحزح.</summary>
    public static List<ScreenRecorder.Mark> ShiftMarks(IEnumerable<ScreenRecorder.Mark> marks, IReadOnlyList<(double From, double To)> keep)
    {
        var res = new List<ScreenRecorder.Mark>();
        foreach (var m in marks)
        {
            double t = m.Ms / 1000.0, acc = 0;
            foreach (var (f, to) in keep)
            {
                if (t >= f && t < to) { res.Add(new ScreenRecorder.Mark((long)((acc + t - f) * 1000), m.Note)); break; }
                acc += to - f;
            }
        }
        return res;
    }

    public static string EncoderArgs(string encoder) => encoder switch
    {
        "h264_nvenc" => "-c:v h264_nvenc -preset p4 -cq 23 -pix_fmt nv12",
        "h264_qsv" => "-c:v h264_qsv -global_quality 23 -pix_fmt nv12",
        "h264_amf" => "-c:v h264_amf -quality balanced -rc cqp -qp_i 22 -qp_p 24 -pix_fmt nv12",
        "h264_mf" => "-c:v h264_mf -rate_control quality -quality 75 -pix_fmt nv12",
        _ => "-c:v mpeg4 -q:v 3 -pix_fmt yuv420p"
    };
}
