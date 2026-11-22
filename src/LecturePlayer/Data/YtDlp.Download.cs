using System.Globalization;
using System.Text.Json;

namespace LecturePlayer.Data;

/// <summary>نوع الملف المتنزل.</summary>
public enum DownloadKind { Mp4, Mp3, M4a }

/// <summary>اختيارات التنزيل.</summary>
/// <param name="MaxHeight">أقصى جودة للفيديو (0 = أعلى متاح).</param>
/// <param name="Mp3Kbps">جودة الـ MP3 (128 / 192 / 320).</param>
/// <param name="EmbedCover">صورة الفيديو كغلاف + الاسم والقناة جوه بيانات الملف.</param>
/// <param name="EmbedChapters">فصول الفيديو جوه الملف.</param>
/// <param name="Subtitles">ترجمة عربي/إنجليزي جوه الفيديو (لو موجودة).</param>
public sealed record DownloadOptions(DownloadKind Kind, int MaxHeight = 720, int Mp3Kbps = 192,
    bool EmbedCover = true, bool EmbedChapters = true, bool Subtitles = false);

/// <summary>حالة التنزيل. Percent من 0 لـ 100 للملف كله (الصورة والصوت مع بعض).</summary>
public sealed record DownloadProgress(double Percent, long TotalBytes, double BytesPerSecond, int EtaSeconds, string Stage);

/// <summary>صيغة متاحة للفيديو (من yt-dlp -J).</summary>
public sealed record FormatInfo(string Id, string? Ext, string? VCodec, string? ACodec, int Height, long Bytes, double Tbr)
{
    public bool HasVideo => VCodec is { } v && v != "none";
    public bool HasAudio => ACodec is { } a && a != "none";
}

/// <summary>بيانات الفيديو قبل التنزيل (الاسم والمدة والجودات المتاحة بأحجامها).</summary>
public sealed record VideoInfo(string Url, string Title, string? Channel, long DurationMs, IReadOnlyList<FormatInfo> Formats)
{
    /// <summary>الجودات المتاحة (480، 720، 1080…) من الأصغر للأكبر.</summary>
    public IReadOnlyList<int> Heights => Formats.Where(f => f.HasVideo && f.Height > 0).Select(f => f.Height).Distinct().OrderBy(h => h).ToList();
}

public static partial class YtDlp
{
    /// <summary>بداية سطر التقدم اللي بنطلبه من الأداة (أرقام خام، فمش بيتأثر بلغة الجهاز ولا بشكل الأداة).</summary>
    internal const string ProgressTag = "[masar]";

    // =====================================================================
    //  أوامر التنزيل
    // =====================================================================

    /// <summary>
    /// أوامر yt-dlp لتنزيل فيديو واحد (public عشان الفحص التلقائي يتأكد منها).
    /// </summary>
    /// <param name="template">اسم الملف (قالب yt-dlp) جوه outDir.</param>
    public static List<string> BuildDownloadArgs(DownloadOptions o, string outDir, string template, string? ffmpegDir)
    {
        var a = new List<string>
        {
            "--no-playlist", "--newline", "--continue", "--no-mtime",
            "--progress-template",
            "download:" + ProgressTag + " %(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s",
            "-o", Path.Combine(outDir, template),
            "--print", "after_move:filepath",
            // --print لوحدها بتخلّي الأداة «ساكتة»: من غير تقدم ولا مراحل ولا أسماء الملفات المؤقتة
            "--no-quiet",
        };
        switch (o.Kind)
        {
            case DownloadKind.Mp4:
                // H.264 + AAC: بيشتغل في أي حتة (موبايل، تليفزيون، واتساب). الجودة الأعلى ≤ المطلوب، والكوديك ده لو موجود فيها
                string sort = (o.MaxHeight > 0 ? $"res:{o.MaxHeight}," : "") + "vcodec:h264,acodec:m4a";
                a.AddRange(new[] { "-f", "bv*+ba/b", "-S", sort, "--merge-output-format", "mp4", "--remux-video", "mp4" });
                if (o.Subtitles) a.AddRange(new[] { "--write-subs", "--sub-langs", "ar.*,ar,en.*,en", "--embed-subs" });
                break;
            case DownloadKind.Mp3:
                a.AddRange(new[] { "-f", "ba/b", "-x", "--audio-format", "mp3", "--audio-quality", $"{Math.Clamp(o.Mp3Kbps, 64, 320)}K" });
                break;
            case DownloadKind.M4a:
                // لو الصوت الأصلي m4a بيتنقل زي ما هو (من غير تحويل) — أسرع وأعلى جودة
                a.AddRange(new[] { "-f", "ba[ext=m4a]/ba/b", "-x", "--audio-format", "m4a" });
                break;
        }
        if (o.EmbedCover) a.AddRange(new[] { "--embed-thumbnail", "--convert-thumbnails", "jpg", "--embed-metadata" });
        if (o.EmbedChapters) a.Add("--embed-chapters");
        if (!string.IsNullOrEmpty(ffmpegDir)) { a.Add("--ffmpeg-location"); a.Add(ffmpegDir); }
        return a;
    }

    /// <summary>امتداد الملف النهائي.</summary>
    public static string ExtensionOf(DownloadKind kind) => kind switch { DownloadKind.Mp3 => ".mp3", DownloadKind.M4a => ".m4a", _ => ".mp4" };

    // =====================================================================
    //  قراءة التقدم
    // =====================================================================

    /// <summary>حالة التحليل وإنت بتقرا سطور الأداة (الفيديو والصوت بيتنزلوا ورا بعض).</summary>
    public sealed class ProgressReader
    {
        private int _parts = 1, _part = -1;
        private double _lastPct;
        private long _total;
        public string Stage { get; private set; } = "بيبدأ…";

        /// <summary>بيرجّع التقدم الجديد لو السطر فيه حاجة، أو null.</summary>
        public DownloadProgress? Read(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            // «[info] abc: Downloading 1 format(s): 137+140» ← جزئين
            if (line.StartsWith("[info]", StringComparison.Ordinal) && line.Contains("Downloading", StringComparison.Ordinal) && line.Contains("format(s):", StringComparison.Ordinal))
            {
                string ids = line[(line.LastIndexOf(':') + 1)..].Trim();
                _parts = Math.Max(1, ids.Split('+', StringSplitOptions.RemoveEmptyEntries).Length);
                return null;
            }
            // ملفات الترجمة والغلاف بتتنزل هي كمان بـ «Destination» — مش جزء من الفيديو
            if (line.StartsWith("[download] Destination:", StringComparison.Ordinal) && IsSideFile(line)) return null;
            if (line.StartsWith("[download] Destination:", StringComparison.Ordinal) ||
                line.StartsWith("[download]", StringComparison.Ordinal) && line.Contains("has already been downloaded", StringComparison.Ordinal))
            {
                _part = Math.Min(_part + 1, _parts - 1);
                _lastPct = 0;
                Stage = _parts > 1 ? (_part == 0 ? "بينزّل الصورة…" : "بينزّل الصوت…") : "بينزّل…";
                return Now(0, 0, -1);
            }
            if (line.StartsWith(ProgressTag, StringComparison.Ordinal))
            {
                var f = line[ProgressTag.Length..].Trim().Split('|');
                if (f.Length < 5) return null;
                long done = Long(f[0]), total = Long(f[1]);
                if (total <= 0) total = Long(f[2]);
                double speed = Dbl(f[3]);
                int eta = (int)Math.Round(Dbl(f[4], -1));
                if (_part < 0) { _part = 0; Stage = "بينزّل…"; }
                if (total > 0) _total = Math.Max(_total, total);
                double pct = total > 0 ? Math.Clamp(done * 100.0 / total, 0, 100) : _lastPct;
                _lastPct = pct;
                return Now(speed, eta, pct);
            }
            string? stage = line.StartsWith("[Merger]", StringComparison.Ordinal) ? "بيدمج الصورة والصوت…"
                : line.StartsWith("[ExtractAudio]", StringComparison.Ordinal) ? "بيجهّز ملف الصوت…"
                : line.StartsWith("[VideoRemuxer]", StringComparison.Ordinal) || line.StartsWith("[VideoConvertor]", StringComparison.Ordinal) ? "بيجهّز ملف الفيديو…"
                : line.StartsWith("[EmbedSubtitle]", StringComparison.Ordinal) ? "بيضيف الترجمة…"
                : line.StartsWith("[ThumbnailsConvertor]", StringComparison.Ordinal) || line.StartsWith("[EmbedThumbnail]", StringComparison.Ordinal) ? "بيضيف الغلاف…"
                : line.StartsWith("[Metadata]", StringComparison.Ordinal) ? "بيضيف البيانات…"
                : null;
            if (stage == null) return null;
            // الأداة بتطبع سطور الغلاف قبل التنزيل كمان: قبل ما يبدأ ماينفعش نقول «بيضيف الغلاف» ولا 100%
            if (_part < 0) return null;
            Stage = stage;
            return Now(0, -1, _lastPct);
        }

        private DownloadProgress Now(double speed, int eta, double partPct)
        {
            int part = Math.Max(0, _part);
            double overall = (part * 100.0 + Math.Max(0, partPct)) / _parts;
            return new DownloadProgress(Math.Clamp(overall, 0, 100), _total, speed, eta, Stage);
        }

        private static bool IsSideFile(string line)
        {
            string ext = Path.GetExtension(line.Trim()).ToLowerInvariant();
            return ext is ".vtt" or ".srt" or ".ttml" or ".srv1" or ".srv2" or ".srv3" or ".json3" or ".ass" or ".webp" or ".jpg" or ".jpeg" or ".png" or ".json";
        }

        private static long Long(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? (long)v : 0;
        private static double Dbl(string s, double fallback = 0) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
    }

    // =====================================================================
    //  البيانات والأحجام
    // =====================================================================

    /// <summary>بيانات فيديو واحد بكل صيغه (عشان نعرض الجودات المتاحة وحجم كل واحدة).</summary>
    public static async Task<VideoInfo> InfoAsync(string url, CancellationToken ct)
    {
        var args = new List<string> { "-J", "--no-playlist" };
        args.AddRange(CommonArgs());
        args.Add(NormalizeUrl(url));
        var (code, stdout, stderr) = await RunAsync(args, ct, TimeSpan.FromMinutes(2));
        if (code != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException(ErrorFrom(stderr, "مقدرتش أقرا بيانات الفيديو"));
        return ParseInfo(stdout, url);
    }

    public static VideoInfo ParseInfo(string json, string url)
    {
        using var doc = ParseFirstJson(json);
        var r = doc.RootElement;
        long durMs = Seconds(r, "duration");
        var formats = new List<FormatInfo>();
        if (r.TryGetProperty("formats", out var fs) && fs.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fs.EnumerateArray())
            {
                string? proto = Str(f, "protocol");
                if (proto is "mhtml") continue; // صور المعاينة
                double tbr = Num(f, "tbr");
                long bytes = (long)Num(f, "filesize");
                if (bytes <= 0) bytes = (long)Num(f, "filesize_approx");
                if (bytes <= 0 && tbr > 0 && durMs > 0) bytes = (long)(tbr * 1000 / 8 * durMs / 1000);
                formats.Add(new FormatInfo(Str(f, "format_id") ?? "", Str(f, "ext"), Str(f, "vcodec"), Str(f, "acodec"),
                    (int)Num(f, "height"), bytes, tbr));
            }
        }
        return new VideoInfo(Str(r, "webpage_url") ?? url, Str(r, "title") ?? url, Str(r, "channel") ?? Str(r, "uploader"), durMs, formats);
    }

    /// <summary>
    /// الحجم التقريبي للتنزيل بنفس منطق الاختيار اللي في الأوامر (أعلى جودة ≤ المطلوب، وH.264 + AAC لو موجودين).
    /// من غير بيانات (قايمة تشغيل): تقدير من المدة.
    /// </summary>
    public static long EstimateBytes(VideoInfo? info, long durationMs, DownloadOptions o)
    {
        double sec = Math.Max(0, (info?.DurationMs > 0 ? info.DurationMs : durationMs) / 1000.0);
        if (o.Kind == DownloadKind.Mp3) return (long)(Math.Clamp(o.Mp3Kbps, 64, 320) * 1000 / 8.0 * sec);

        FormatInfo? bestAudio = info?.Formats.Where(f => f.HasAudio && !f.HasVideo)
            .OrderByDescending(f => f.ACodec?.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase) == true)
            .ThenByDescending(f => f.Tbr).FirstOrDefault();
        long audio = bestAudio?.Bytes > 0 ? bestAudio.Bytes : (long)(130_000 / 8.0 * sec);
        if (o.Kind == DownloadKind.M4a) return audio;

        if (info != null)
        {
            var videos = info.Formats.Where(f => f.HasVideo && !f.HasAudio && (o.MaxHeight <= 0 || f.Height <= o.MaxHeight)).ToList();
            if (videos.Count > 0)
            {
                int h = videos.Max(f => f.Height);
                var pick = videos.Where(f => f.Height == h)
                    .OrderByDescending(f => f.VCodec?.StartsWith("avc1", StringComparison.OrdinalIgnoreCase) == true)
                    .ThenByDescending(f => f.Tbr).First();
                if (pick.Bytes > 0) return pick.Bytes + audio;
            }
            var muxed = info.Formats.Where(f => f.HasVideo && f.HasAudio && (o.MaxHeight <= 0 || f.Height <= o.MaxHeight))
                .OrderByDescending(f => f.Height).ThenByDescending(f => f.Tbr).FirstOrDefault();
            if (muxed?.Bytes > 0) return muxed.Bytes;
        }
        // تقدير متوسط لفيديو H.264 (محاضرات/شاشة بتبقى أقل من كده)
        double kbps = o.MaxHeight switch { <= 0 => 4500, <= 360 => 500, <= 480 => 900, <= 720 => 1800, <= 1080 => 3500, _ => 8000 };
        return (long)(kbps * 1000 / 8.0 * sec) + audio;
    }

    // =====================================================================
    //  التنزيل
    // =====================================================================

    /// <summary>
    /// بينزّل فيديو واحد ويرجّع مسار الملف النهائي.
    /// • لو اتقطع (نت/قفل البرنامج/إلغاء) المرة الجاية بيكمّل من مكان ماوقف (--continue + ملف .part).
    /// • لو يوتيوب طلب «أكّد إنك مش روبوت»: نفس الطرق البديلة اللي التشغيل بيستخدمها.
    /// • لو الفشل شكله من تغيير في يوتيوب: بيحدّث الأداة مرة ويعيد.
    /// </summary>
    public static async Task<string> DownloadAsync(string url, string outDir, string template, DownloadOptions o,
        IProgress<DownloadProgress> progress, CancellationToken ct, Action<string>? onUpdate = null, Action<string>? onLine = null)
    {
        string exe = FindYtDlp() ?? throw new FileNotFoundException("أداة يوتيوب مش موجودة");
        Directory.CreateDirectory(outDir);
        var baseArgs = BuildDownloadArgs(o, outDir, template, Directory.Exists(Tools.FfmpegDir) ? Tools.FfmpegDir : null);
        baseArgs.AddRange(CommonArgs());
        baseArgs.Add(NormalizeUrl(url));

        var attempts = new List<(string Name, string[] Extra)>();
        if (_botWorkaround is { } known) attempts.Add(("known", known));
        attempts.Add(("plain", Array.Empty<string>()));
        bool updated = false;
        string lastErr = "";

        for (int i = 0; i < attempts.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var reader = new ProgressReader();
            var args = WithExtra(baseArgs, attempts[i].Extra);
            var r = await ProcessRunner.RunAsync(exe, args, TimeSpan.FromHours(4), ct, line =>
            {
                onLine?.Invoke(line);
                if (reader.Read(line) is { } p) progress.Report(p);
            });
            string? file = r.StdOut.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !l.StartsWith('[') && File.Exists(l));
            if (r.ExitCode == 0 && file != null)
            {
                if (attempts[i].Extra.Length > 0 && attempts[i].Name != "known") _botWorkaround = attempts[i].Extra;
                return file;
            }
            lastErr = r.StdErr;
            Log.Info($"download attempt {attempts[i].Name} failed: {FirstErrorLine(r.StdErr)}");

            if (IsBotCheck(r.StdErr) && attempts.Count <= 2)
            {
                foreach (var w in BotWorkarounds()) attempts.Add(w);
                continue;
            }
            if (!updated && MightBeFixedByUpdate(r.StdErr))
            {
                updated = true;
                onUpdate?.Invoke("بيحدّث أداة يوتيوب ويحاول تاني…");
                try { Log.Info("download: updating yt-dlp → " + await UpdateAsync(ct)); }
                catch (Exception ex) when (ex is not OperationCanceledException) { Log.Info("yt-dlp update failed: " + ex.Message); }
                attempts.Add(("after-update", attempts[i].Extra));
            }
        }
        throw new InvalidOperationException(Explain(ErrorFrom(lastErr, "مقدرتش أنزّل الفيديو")).Message);
    }

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    /// <summary>«12.3 ميجا» / «1.4 جيجا».</summary>
    public static string FormatSize(long bytes) =>
        bytes <= 0 ? "؟" : bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} جيجا" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} ميجا" : $"{Math.Max(1, bytes / 1024)} كيلو";
}
