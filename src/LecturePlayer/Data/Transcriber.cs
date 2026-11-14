using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>
/// جلب نص الفيديو:
/// • فيديو يوتيوب ← ترجمة يوتيوب (العادية أو التلقائية) عن طريق yt-dlp — سريع ومن غير تحميل الفيديو.
/// • ملف على الجهاز (أو يوتيوب من غير ترجمة) ← whisper.cpp على الجهاز (من غير نت ومجاني، بس بياخد وقت).
/// </summary>
public static class Transcriber
{
    // ---------------- يوتيوب ----------------

    /// <summary>ترتيب تفضيل ملفات الترجمة اللي yt-dlp نزّلها.</summary>
    public static string? PickSubtitleFile(IEnumerable<string> files, string preferredLang = "auto")
    {
        var list = files.Where(f => f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0) return null;
        string Lang(string f)
        {
            string name = Path.GetFileNameWithoutExtension(f); // video.ar  أو video.en-orig
            int dot = name.LastIndexOf('.');
            return dot >= 0 ? name[(dot + 1)..].ToLowerInvariant() : "";
        }
        int Rank(string f)
        {
            string l = Lang(f);
            if (preferredLang != "auto" && (l == preferredLang || l.StartsWith(preferredLang + "-"))) return 0;
            if (l.EndsWith("-orig")) return 1;   // لغة الفيديو الأصلية
            if (l == "ar" || l.StartsWith("ar-")) return 2;
            if (l == "en" || l.StartsWith("en-")) return 3;
            return 4;
        }
        return list.OrderBy(Rank).ThenBy(f => f.Length).First();
    }

    public static async Task<Transcript?> FromYoutubeAsync(string url, string preferredLang, CancellationToken ct)
    {
        string exe = YtDlp.FindYtDlp() ?? throw new FileNotFoundException("أداة yt-dlp مش موجودة");
        string dir = Path.Combine(Path.GetTempPath(), "LecturePlayer-subs", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string langs = preferredLang == "auto" ? ".*-orig,ar,en" : $"{preferredLang},{preferredLang}-orig,.*-orig";
            var r = await ProcessRunner.RunAsync(exe, new[]
            {
                "--skip-download", "--write-subs", "--write-auto-subs", "--sub-langs", langs, "--sub-format", "vtt",
                "--no-playlist", "--no-warnings", "--encoding", "utf-8", "-o", Path.Combine(dir, "sub.%(ext)s"), url
            }, TimeSpan.FromMinutes(3), ct);
            string? file = PickSubtitleFile(Directory.GetFiles(dir), preferredLang);
            if (file == null)
            {
                if (r.ExitCode != 0) Log.Info("yt subs: " + ProcessRunner.LastErrorLine(r.StdErr, "خطأ غير معروف"));
                return null;
            }
            var t = Transcript.Parse(await File.ReadAllTextAsync(file, ct)).WithoutRollingDuplicates();
            return t.IsEmpty ? null : t;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ---------------- whisper ----------------

    private static readonly Regex ProgressRegex = new(@"progress\s*=\s*(\d+)\s*%", RegexOptions.Compiled);

    public static int? ParseWhisperProgress(string line)
    {
        var m = ProgressRegex.Match(line);
        return m.Success ? Math.Clamp(int.Parse(m.Groups[1].Value), 0, 100) : null;
    }

    public static IReadOnlyList<string> WhisperArgs(string model, string wav, string outPrefix, string lang, int threads) => new[]
    {
        "-m", model, "-f", wav, "-l", string.IsNullOrWhiteSpace(lang) ? "auto" : lang,
        "-ovtt", "-of", outPrefix, "-pp", "-t", threads.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    /// <summary>تفريغ ملف على الجهاز بـ whisper.cpp. progress: 0..100.</summary>
    public static async Task<Transcript> WithWhisperAsync(string videoFile, string model, string lang, IProgress<int> progress, CancellationToken ct)
    {
        string whisper = Tools.FindWhisper() ?? throw new FileNotFoundException("whisper مش متنزّل");
        string modelPath = Tools.ModelPath(model);
        if (!File.Exists(modelPath)) throw new FileNotFoundException("موديل whisper مش متنزّل: " + model);
        if (Tools.FindFfmpeg() == null) throw new FileNotFoundException("ffmpeg مش متنزّل");

        string dir = Path.Combine(Path.GetTempPath(), "LecturePlayer-whisper", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string wav = Path.Combine(dir, "audio.wav");
            progress.Report(0);
            await Tools.ExtractAudioWavAsync(videoFile, wav, ct);

            string prefix = Path.Combine(dir, "out");
            int threads = Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
            var r = await ProcessRunner.RunAsync(whisper, WhisperArgs(modelPath, wav, prefix, lang, threads), TimeSpan.FromHours(4), ct, line =>
            {
                int? p = ParseWhisperProgress(line);
                if (p.HasValue) progress.Report(p.Value);
            }, workingDirectory: Path.GetDirectoryName(whisper));

            string vtt = prefix + ".vtt";
            if (r.ExitCode != 0 || !File.Exists(vtt))
                throw new InvalidOperationException("whisper فشل: " + ProcessRunner.LastErrorLine(r.StdErr, "خطأ غير معروف"));
            progress.Report(100);
            return Transcript.Parse(await File.ReadAllTextAsync(vtt, ct));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
