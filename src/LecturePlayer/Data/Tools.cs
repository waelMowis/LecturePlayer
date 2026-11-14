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
    public const string WhisperUrl = "https://github.com/ggml-org/whisper.cpp/releases/latest/download/whisper-bin-x64.zip";
    public const string WhisperModelUrlFormat = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{0}.bin";

    /// <summary>موديلات whisper المتاحة: الاسم ← الحجم التقريبي.</summary>
    public static readonly (string Name, string Label)[] WhisperModels =
    {
        ("base", "base — سريع (حوالي 150 ميجا)"),
        ("small", "small — أدق، أبطأ (حوالي 480 ميجا)"),
        ("medium", "medium — الأدق للعربي، بطيء (حوالي 1.5 جيجا)")
    };

    public static string ToolsDir => Path.Combine(Storage.AppDataDir, "tools");
    public static string FfmpegDir => Path.Combine(ToolsDir, "ffmpeg");
    public static string WhisperDir => Path.Combine(ToolsDir, "whisper");
    public static string ModelsDir => Path.Combine(ToolsDir, "models");

    public static string? FindFfmpeg() => Find("ffmpeg.exe", FfmpegDir) ?? Find("ffmpeg", FfmpegDir);
    public static string? FindWhisper() => Find("whisper-cli.exe", WhisperDir) ?? Find("whisper-cli", WhisperDir);
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

    public static async Task DownloadFfmpegAsync(IProgress<string> progress, CancellationToken ct)
    {
        string zip = Path.Combine(ToolsDir, "ffmpeg.zip");
        await DownloadAsync(FfmpegUrl, zip, "ffmpeg", progress, ct);
        progress.Report("بفك ضغط ffmpeg…");
        ExtractFlat(zip, FfmpegDir, e => e.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
        File.Delete(zip);
        if (FindFfmpeg() == null) throw new InvalidOperationException("ملف ffmpeg المضغوط مفيهوش ffmpeg.exe");
    }

    public static async Task DownloadWhisperAsync(string model, IProgress<string> progress, CancellationToken ct)
    {
        if (FindWhisper() == null)
        {
            string zip = Path.Combine(ToolsDir, "whisper.zip");
            await DownloadAsync(WhisperUrl, zip, "whisper", progress, ct);
            progress.Report("بفك ضغط whisper…");
            ExtractFlat(zip, WhisperDir, e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                              e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            File.Delete(zip);
        }
        if (!File.Exists(ModelPath(model)))
        {
            Directory.CreateDirectory(ModelsDir);
            await DownloadAsync(string.Format(CultureInfo.InvariantCulture, WhisperModelUrlFormat, model), ModelPath(model),
                $"موديل whisper ({model})", progress, ct);
        }
        if (FindWhisper() == null) throw new InvalidOperationException("ملف whisper المضغوط مفيهوش whisper-cli.exe");
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
        string ffmpeg = FindFfmpeg() ?? throw new FileNotFoundException("ffmpeg مش موجود");
        var r = await ProcessRunner.RunAsync(ffmpeg, new[]
        {
            "-v", "error", "-y", "-i", video, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", outWav
        }, TimeSpan.FromHours(2), ct);
        if (r.ExitCode != 0 || !File.Exists(outWav))
            throw new InvalidOperationException(ProcessRunner.LastErrorLine(r.StdErr, "ffmpeg مقدرش يقرا صوت الفيديو"));
    }
}
