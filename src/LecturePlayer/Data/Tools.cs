using System.Globalization;
using System.IO.Compression;

namespace LecturePlayer.Data;

/// <summary>
/// الأدوات الخارجية المجانية اللي البرنامج بيستخدمها، وبتتنزل وقت الحاجة في Data\tools:
/// • ffmpeg  — صور المعاينة، قراءة الصوت للتفريغ، اكتشاف السكوت، دمج فيديوهات يوتيوب المتنزلة.
/// • whisper.cpp — تفريغ كلام المحاضرات محليًا (من غير نت).
/// (yt-dlp و Deno في YtDlp.cs)
/// </summary>
public static class Tools
{
    public const string FfmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip";
    // آخر إصدارات whisper.cpp الرسمية (v1.9.x) بقت من غير ملفات ويندوز جاهزة، فرابط «latest» بيرجّع 404.
    // الملفات الجاهزة بقت في إصدارات تانية (bNNNN): بندوّر على أحدث إصدار فيه الملف، والرابط ده احتياطي لو مفيش نت لـ API.
    public const string WhisperFallbackUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/b5454/whisper-bin-x64.zip";
    private const string WhisperAsset = "whisper-bin-x64.zip";
    public const string WhisperModelUrlFormat = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{0}.bin";

    /// <summary>موديلات whisper المتاحة: الاسم ← الحجم التقريبي.</summary>
    public static readonly (string Name, string Label)[] WhisperModels =
    {
        ("base", "base — سريع (نحو 150 ميجابايت)"),
        ("small", "small — أدق، أبطأ (نحو 480 ميجابايت)"),
        ("medium", "medium — الأدق للعربية، بطيء (نحو 1.5 جيجابايت)")
    };

    public static string ToolsDir => Path.Combine(Storage.AppDataDir, "tools");
    public static string FfmpegDir => Path.Combine(ToolsDir, "ffmpeg");
    public static string WhisperDir => Path.Combine(ToolsDir, "whisper");
    public static string ModelsDir => Path.Combine(ToolsDir, "models");

    public static string? FindFfmpeg() => Find("ffmpeg.exe", FfmpegDir) ?? Find("ffmpeg", FfmpegDir);
    /// <summary>الأداة بتيجي جاهزة مع البرنامج (فولدر whisper جنب البرنامج)، أو متنزلة في فولدر الأدوات.</summary>
    public static string? FindWhisper() =>
        Find("whisper-cli.exe", WhisperDir) ?? Find("whisper-cli.exe", Path.Combine(AppContext.BaseDirectory, "whisper")) ?? Find("whisper-cli", WhisperDir);
    public static string ModelPath(string model) => Path.Combine(ModelsDir, $"ggml-{model}.bin");

    private static string? Find(string exe, string preferredDir)
    {
        var dirs = new List<string> { preferredDir, ToolsDir, AppContext.BaseDirectory };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (string dir in dirs)
        {
            try
            {
                string candidate = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    // =====================================================================
    //  التنزيل
    // =====================================================================

    // تنزيل واحد في نفس الوقت لكل أداة: طلبان معًا (معاينة + تسجيل مثلًا) كانا يكتبان في نفس الملف
    // فيظهر «The process cannot access the file … ffmpeg.zip.download because it is being used by another process».
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Task> Running = new();

    private static Task Once(string key, Func<Task> start)
    {
        lock (Gate)
        {
            if (Running.TryGetValue(key, out var t) && !t.IsCompleted) return t;
            t = start();
            Running[key] = t;
            return t;
        }
    }

    /// <summary>أداة الفيديو تُنزَّل الآن (طلب آخر بدأ تنزيلها).</summary>
    public static bool FfmpegDownloading { get { lock (Gate) return Running.TryGetValue("ffmpeg", out var t) && !t.IsCompleted; } }

    public static Task DownloadFfmpegAsync(IProgress<string> progress, CancellationToken ct) =>
        Once("ffmpeg", () => DownloadFfmpegCoreAsync(progress, ct));

    private static async Task DownloadFfmpegCoreAsync(IProgress<string> progress, CancellationToken ct)
    {
        if (FindFfmpeg() != null) return;
        CleanStaleDownloads();
        string zip = Path.Combine(ToolsDir, $"ffmpeg-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadAsync(FfmpegUrl, zip, "أداة الفيديو", progress, ct);
            progress.Report("جارٍ تجهيز أداة الفيديو…");
            ExtractFlat(zip, FfmpegDir, e => e.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
        }
        finally { TryDelete(zip); }
        if (FindFfmpeg() == null) throw new InvalidOperationException("ملف ffmpeg المضغوط لا يحتوي على ffmpeg.exe");
    }

    /// <summary>بقايا تنزيل انقطع (البرنامج أُغلق في أثنائه) تُحذف قبل التنزيل التالي.</summary>
    private static void CleanStaleDownloads()
    {
        try
        {
            if (!Directory.Exists(ToolsDir)) return;
            foreach (var f in Directory.EnumerateFiles(ToolsDir, "*.download").Concat(Directory.EnumerateFiles(ToolsDir, "ffmpeg*.zip")))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddMinutes(-30)) TryDelete(f);
        }
        catch { }
    }

    private static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

    public static Task DownloadWhisperAsync(string model, IProgress<string> progress, CancellationToken ct) =>
        Once("whisper:" + model, () => DownloadWhisperCoreAsync(model, progress, ct));

    private static async Task DownloadWhisperCoreAsync(string model, IProgress<string> progress, CancellationToken ct)
    {
        if (FindWhisper() == null)
        {
            string zip = Path.Combine(ToolsDir, $"whisper-{Guid.NewGuid():N}.zip");
            await DownloadAsync(await ResolveWhisperUrlAsync(ct), zip, "أداة تحويل الكلام إلى نص", progress, ct);
            progress.Report("جارٍ تجهيز أداة تحويل الكلام إلى نص…");
            ExtractFlat(zip, WhisperDir, e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                              e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            TryDelete(zip);
        }
        if (!File.Exists(ModelPath(model)))
        {
            Directory.CreateDirectory(ModelsDir);
            await DownloadAsync(string.Format(CultureInfo.InvariantCulture, WhisperModelUrlFormat, model), ModelPath(model),
                $"ملف اللغة ({model})", progress, ct);
        }
        if (FindWhisper() == null) throw new InvalidOperationException("ملف whisper المضغوط لا يحتوي على whisper-cli.exe");
    }

    /// <summary>أحدث إصدار من whisper.cpp فيه ملف ويندوز 64 جاهز.</summary>
    public static async Task<string> ResolveWhisperUrlAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Masar");
            string json = await http.GetStringAsync("https://api.github.com/repos/ggml-org/whisper.cpp/releases?per_page=30", ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
                if (!release.TryGetProperty("assets", out var assets)) continue;
                foreach (var a in assets.EnumerateArray())
                    if (a.GetProperty("name").GetString() == WhisperAsset)
                        return a.GetProperty("browser_download_url").GetString()!;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Info("whisper url lookup failed, using fallback: " + ex.Message); }
        return WhisperFallbackUrl;
    }

    /// <summary>بيفك الملفات المطلوبة من الـ zip في فولدر واحد (من غير الفولدرات اللي جواه).</summary>
    private static void ExtractFlat(string zip, string targetDir, Func<ZipArchiveEntry, bool> include)
    {
        Directory.CreateDirectory(targetDir);
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) || !include(entry)) continue;
            entry.ExtractToFile(Path.Combine(targetDir, entry.Name), overwrite: true);
        }
    }

    private static async Task DownloadAsync(string url, string target, string label, IProgress<string> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LecturePlayer/4.0");
        await YtDlp.DownloadFileAsync(http, url, target, label, progress, ct);
    }

    // =====================================================================
    //  ffmpeg
    // =====================================================================

    /// <summary>صورة صغيرة من الفيديو عند ثانية معينة (معاينة شريط التقديم).</summary>
    public static async Task<bool> ExtractFrameAsync(string video, long timeMs, string outJpg, int width, CancellationToken ct)
    {
        string? ffmpeg = FindFfmpeg();
        if (ffmpeg == null) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(outJpg)!);
        var r = await ProcessRunner.RunAsync(ffmpeg, new[]
        {
            "-v", "error", "-y",
            "-ss", (timeMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture),
            "-i", video, "-frames:v", "1",
            "-vf", $"scale={width}:-2", "-q:v", "5", outJpg
        }, TimeSpan.FromSeconds(20), ct);
        return r.ExitCode == 0 && File.Exists(outJpg);
    }

    /// <summary>صوت الفيديو كـ WAV أحادي 16 كيلوهرتز (الصيغة اللي whisper محتاجها).</summary>
    public static async Task ExtractAudioWavAsync(string video, string outWav, CancellationToken ct)
    {
        string ffmpeg = FindFfmpeg() ?? throw new FileNotFoundException("ffmpeg غير موجود");
        var r = await ProcessRunner.RunAsync(ffmpeg, new[]
        {
            "-v", "error", "-y", "-i", video, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", outWav
        }, TimeSpan.FromHours(2), ct);
        if (r.ExitCode != 0 || !File.Exists(outWav))
            throw new InvalidOperationException(ProcessRunner.LastErrorLine(r.StdErr, "لم يستطع ffmpeg قراءة صوت الفيديو"));
    }
}
