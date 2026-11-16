using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace LecturePlayer.Data;

/// <summary>فيديو واحد من رابط (من غير ما نجيب رابط التشغيل نفسه).</summary>
public sealed record OnlineEntry(string Url, string Title, long DurationMs);

/// <summary>نتيجة قراءة رابط: فيديو واحد أو قائمة تشغيل.</summary>
public sealed record OnlineListing(string? Title, bool IsPlaylist, List<OnlineEntry> Entries);

/// <summary>روابط التشغيل الفعلية (بتنتهي صلاحيتها بعد ساعات، فبنجيبها وقت التشغيل بس).</summary>
/// <param name="IsLive">بث مباشر (يوتيوب لايف…).</param>
/// <param name="ManifestUrl">قايمة البث الرئيسية (HLS master) بكل الجودات، عشان VLC يغيّر الجودة لوحده حسب النت.</param>
public sealed record OnlineStream(string VideoUrl, string? AudioUrl, string? UserAgent, string? Referer, long DurationMs,
    bool IsLive = false, string? ManifestUrl = null, IReadOnlyList<StreamPart>? Parts = null, DateTimeOffset? AvailableAt = null);

/// <summary>جزء من التشغيل (الصورة أو الصوت أو الاتنين) بالصيغة والهيدرز اللي yt-dlp استخدمها بالظبط.</summary>
public sealed record StreamPart(string FormatId, string Url, string Protocol, bool HasVideo, bool HasAudio,
    IReadOnlyDictionary<string, string> Headers, string Description);

/// <summary>
/// التعامل مع yt-dlp (نفس الأداة اللي PotPlayer بيستخدمها لليوتيوب).
/// يوتيوب محتاج كمان Deno (JavaScript runtime) عشان yt-dlp يشتغل كويس.
/// الأدوات بتتحط في %AppData%\LecturePlayer\tools
/// </summary>
public static class YtDlp
{
    public const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    public const string DenoDownloadUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";

    public static string ToolsDir => Path.Combine(Storage.AppDataDir, "tools");

    /// <summary>
    /// كوكيز المتصفح (firefox / chrome / edge / brave / opera) — بتحل «Sign in to confirm you're not a bot»
    /// والفيديوهات اللي محتاجة تسجيل دخول. فاضي = من غير.
    /// </summary>
    public static string? CookiesBrowser { get; set; }

    /// <summary>طريقة اختيار الصيغة: Best = صورة وصوت منفصلين بأعلى جودة، Muxed = ملف واحد فيه الاتنين (أضمن مع VLC).</summary>
    public enum StreamMode { Best, Muxed }

    /// <summary>إعدادات ثابتة لكل أوامر yt-dlp: إعادة المحاولة، مهلة الاتصال، والكوكيز.</summary>
    private static IEnumerable<string> CommonArgs()
    {
        yield return "--no-warnings";
        yield return "--encoding"; yield return "utf-8";
        yield return "--extractor-retries"; yield return "3";
        yield return "--socket-timeout"; yield return "20";
        if (!string.IsNullOrWhiteSpace(CookiesBrowser))
        {
            yield return "--cookies-from-browser";
            yield return CookiesBrowser.Trim();
        }
    }

    // =====================================================================
    //  تنضيف الروابط
    // =====================================================================

    /// <summary>
    /// بينضّف الرابط قبل ما يروح لـ yt-dlp: مسافات وعلامات تنصيص، ومن غير بروتوكول (www.youtube.com/...)،
    /// وقوائم «Mix» بتاعة يوتيوب (list=RD...) اللي مالهاش آخر ← فيديو لوحده.
    /// </summary>
    public static string NormalizeUrl(string url)
    {
        string u = (url ?? "").Trim().Trim('"', '\'', '<', '>', ' ');
        if (u.Length > 0 && !u.Contains("://", StringComparison.Ordinal) &&
            (u.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || u.Contains("youtu", StringComparison.OrdinalIgnoreCase)))
            u = "https://" + u;
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri)) return u;

        if (uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).ToList();
            bool hasVideo = query.Any(q => q.StartsWith("v=", StringComparison.Ordinal)) || uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
            string? list = query.FirstOrDefault(q => q.StartsWith("list=", StringComparison.Ordinal))?[5..];
            // Mix / Radio (RD...) قوايم أوتوماتيك مالهاش آخر: ناخد الفيديو بس
            bool mix = list != null && list.StartsWith("RD", StringComparison.Ordinal);
            query.RemoveAll(q => q.StartsWith("si=", StringComparison.Ordinal) || q.StartsWith("pp=", StringComparison.Ordinal) ||
                                 q.StartsWith("feature=", StringComparison.Ordinal) || q.StartsWith("start_radio=", StringComparison.Ordinal) ||
                                 (mix && hasVideo && (q.StartsWith("list=", StringComparison.Ordinal) || q.StartsWith("index=", StringComparison.Ordinal))));
            var b = new UriBuilder(uri) { Query = string.Join("&", query) };
            u = b.Uri.AbsoluteUri;
        }
        return u;
    }

    // =====================================================================
    //  رسائل الأخطاء بالعربي
    // =====================================================================

    /// <summary>رسالة خطأ yt-dlp ← شرح بالعربي + هل ينفع ينزّل أو لأ.</summary>
    public static (string Message, bool Recoverable) Explain(string raw)
    {
        string r = raw ?? "";
        bool Has(params string[] keys) => keys.Any(k => r.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (Has("confirm your age", "age-restricted", "Sign in to confirm your age"))
            return ("الفيديو ده محتاج تسجيل دخول (للكبار بس).\nالحل: الإعدادات ← يوتيوب والإنترنت ← «تسجيل الدخول من المتصفح».", false);
        if (Has("Sign in to confirm", "not a bot"))
            return ("يوتيوب طالب يتأكد إنك مش روبوت (بيحصل على بعض الشبكات زي شبكات الشغل والمدارس).\n" +
                    "البرنامج جرّب لوحده كذا طريقة بديلة ومنفعتش.\n" +
                    "الحل: افتح يوتيوب في Firefox وسجّل دخولك، وبعدين: الإعدادات ← يوتيوب والإنترنت ← «تسجيل الدخول من المتصفح» ← Firefox.\n" +
                    "(Chrome وEdge الجداد بيمنعوا البرامج تقرا تسجيل الدخول، فـ Firefox هو الأضمن.)", false);
        if (Has("Private video"))
            return ("الفيديو ده خاص (Private) ومش متاح غير لصاحبه.", false);
        if (Has("members-only", "Join this channel", "available to this channel's members"))
            return ("الفيديو ده للأعضاء المشتركين في القناة بس.", false);
        if (Has("Video unavailable", "has been removed", "no longer available", "This video has been", "account associated with this video has been terminated"))
            return ("الفيديو ده اتمسح أو مش متاح على يوتيوب.", false);
        if (Has("not available in your country", "geo restrict", "geo-restrict"))
            return ("الفيديو ده مش متاح في بلدك.", false);
        if (Has("not currently live", "is not live", "no live"))
            return ("القناة مش بتبث لايف دلوقتي. جرّب بعد شوية.", true);
        if (Has("live event will begin", "Premieres in", "This live event"))
            return ("البث ده لسه مابدأش.", false);
        if (Has("Unsupported URL"))
            return ("الرابط ده مش لصفحة فيها فيديو معروف للبرنامج.\nلو هو صفحة فيها فيديو، افتح الفيديو نفسه وانسخ رابطه.", false);
        if (Has("Unable to download webpage", "getaddrinfo", "timed out", "Connection reset", "Temporary failure in name resolution", "No route to host"))
            return ("مشكلة في الاتصال بالنت. اتأكد إن النت شغال وجرّب تاني.", true);
        if (Has("HTTP Error 403", "Forbidden"))
            return ("الموقع رفض الطلب (403). غالبًا أداة يوتيوب محتاجة تحديث: منيو إنترنت ← تحديث أداة يوتيوب.", true);
        if (Has("Requested format is not available"))
            return ("مفيش جودة مناسبة للفيديو ده. جرّب تغيير الجودة من منيو إنترنت ← جودة فيديوهات النت.", true);
        return (r.Length > 0 ? r : "خطأ مش معروف", true);
    }

    /// <summary>أخطاء غالبًا بتتحل بتحديث yt-dlp (يوتيوب غيّر حاجة في الموقع).</summary>
    public static bool MightBeFixedByUpdate(string raw) =>
        new[] { "HTTP Error 403", "HTTP Error 400", "Unable to extract", "nsig", "signature", "Requested format is not available",
                "Precondition check failed", "player", "Failed to extract", "Some formats may be missing", "JavaScript" }
            .Any(k => (raw ?? "").Contains(k, StringComparison.OrdinalIgnoreCase));

    public static bool IsUnsupportedUrl(string raw) => (raw ?? "").Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase);

    public static string? FindYtDlp() => FindTool("yt-dlp.exe");
    public static string? FindDeno() => FindTool("deno.exe");

    private static string? FindTool(string exe)
    {
        var dirs = new List<string> { ToolsDir, AppContext.BaseDirectory };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (string dir in dirs)
        {
            try
            {
                string candidate = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* مسار غلط في PATH */ }
        }
        return null;
    }

    // =====================================================================
    //  قراءة رابط (فيديو أو قائمة تشغيل)
    // =====================================================================

    public static async Task<OnlineListing> ListAsync(string url, bool wholePlaylist, CancellationToken ct)
    {
        var args = new List<string> { "--flat-playlist", "-J" };
        args.AddRange(CommonArgs());
        args.Add(wholePlaylist ? "--yes-playlist" : "--no-playlist");
        args.Add(NormalizeUrl(url));

        var (code, stdout, stderr) = await RunAsync(args, ct, TimeSpan.FromMinutes(3));
        if (code != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            // فيديو واحد ويوتيوب رافض الأداة: نجيب اسمه من يوتيوب مباشرة (oEmbed) ونضيفه — المدة بتتعرف وقت التشغيل
            if (IsBotCheck(stderr) && await OEmbedTitleAsync(url, ct) is { } title)
            {
                Log.Info("added via oEmbed (bot check): " + url);
                return new OnlineListing(null, false, new List<OnlineEntry> { new(NormalizeUrl(url), title, 0) });
            }
            throw new InvalidOperationException(ErrorFrom(stderr, "مقدرتش أقرا الرابط"));
        }

        return ParseListing(stdout, url);
    }

    /// <summary>اسم فيديو يوتيوب من غير أداة يوتيوب (خدمة oEmbed الرسمية). null لو مش فيديو واحد أو فشل.</summary>
    public static async Task<string?> OEmbedTitleAsync(string url, CancellationToken ct)
    {
        string u = NormalizeUrl(url);
        bool single = u.Contains("youtube.com/watch", StringComparison.OrdinalIgnoreCase) && !u.Contains("list=", StringComparison.OrdinalIgnoreCase)
                      || u.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase) || u.Contains("/shorts/", StringComparison.OrdinalIgnoreCase);
        if (!single) return null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            string json = await http.GetStringAsync("https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(u), ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } title ? title : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Info("oEmbed failed: " + ex.Message); return null; }
    }

    /// <summary>تحليل ناتج yt-dlp -J (public عشان يتجرّب لوحده).</summary>
    public static OnlineListing ParseListing(string json, string originalUrl)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string type = Str(root, "_type") ?? "video";

        if (type == "playlist" && root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            var list = new List<OnlineEntry>();
            foreach (var e in entries.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (Str(e, "_type") == "playlist") continue; // تابات القناة وغيرها
                string? title = Str(e, "title");
                if (title is "[Private video]" or "[Deleted video]") continue;

                string? entryUrl = Str(e, "url") ?? Str(e, "webpage_url");
                string? id = Str(e, "id");
                if (string.IsNullOrEmpty(entryUrl) || !MediaFiles.IsUrl(entryUrl))
                    entryUrl = id != null ? "https://www.youtube.com/watch?v=" + id : null;
                if (entryUrl == null) continue;

                list.Add(new OnlineEntry(entryUrl, title ?? id ?? entryUrl, Seconds(e, "duration")));
            }
            return new OnlineListing(Str(root, "title"), true, list);
        }

        string single = Str(root, "webpage_url") ?? Str(root, "original_url") ?? originalUrl;
        return new OnlineListing(null, false, new List<OnlineEntry>
        {
            new(single, Str(root, "title") ?? single, Seconds(root, "duration"))
        });
    }

    // =====================================================================
    //  روابط التشغيل
    // =====================================================================

    /// <param name="maxHeight">أعلى جودة (720 / 1080 ...)، 0 = أعلى جودة متاحة</param>
    public static async Task<OnlineStream> ResolveAsync(string url, int maxHeight, CancellationToken ct, StreamMode mode = StreamMode.Best)
    {
        // proto: يفضّل https العادي على m3u8 (VLC بيشغّله أسهل)، وh264/m4a لأنهم أضمن مع VLC
        // الصوت: من غير تفضيل m4a — yt-dlp بيختار أحسن صوت متاح (غالبًا opus زي PotPlayer)، وVLC بيشغّله عادي
        string sort = (maxHeight > 0 ? $"res:{maxHeight}," : "") + "proto,vcodec:h264" + (mode == StreamMode.Muxed ? ",acodec:m4a" : "");
        return await ResolveFormatAsync(url, mode == StreamMode.Muxed ? "b/bv*+ba" : "bv*+ba/b", sort, ct);
    }

    /// <summary>
    /// بث مباشر: ملف واحد (صورة وصوت مع بعض) بحد أقصى 30 صورة في الثانية.
    /// بث الحرم على يوتيوب بييجي 60 صورة في الثانية، وده بيتقل على فك الفيديو فالصورة تقف والصوت يكمّل.
    /// </summary>
    public static Task<OnlineStream> ResolveLiveAsync(string url, int maxHeight, CancellationToken ct)
    {
        // بث يوتيوب بييجي صورة لوحدها وصوت لوحده. الصيغة دي موجودة دايمًا (مفيش «جودة مش متاحة»)،
        // والتشغيل نفسه بيبقى من «القايمة الرئيسية» للبث (manifest) عشان المشغل يزامن الصورة والصوت.
        int h = maxHeight > 0 ? maxHeight : 720;
        return ResolveFormatAsync(url, "bv*+ba/b", $"res:{h},fps:30,vcodec:h264", ct);
    }

    /// <summary>رابط جديد لنفس الصيغة بالظبط (بيستخدمه الوسيط لما يوتيوب يرفض الرابط القديم في النص).</summary>
    public static async Task<OnlineStream> ResolveFormatAsync(string url, string format, string? sort, CancellationToken ct, bool forceIpv4 = false)
    {
        var args = new List<string> { "-j", "--no-playlist", "-f", format };
        if (forceIpv4) args.Add("--force-ipv4"); // الرابط يتربط بعنوان IPv4 (والوسيط بيتصل بـ IPv4)
        if (!string.IsNullOrEmpty(sort)) { args.Add("-S"); args.Add(sort); }
        args.AddRange(CommonArgs().Where(a => a != "--no-warnings")); // تحذيرات يوتيوب مهمة هنا (بتتسجل)
        args.Add(NormalizeUrl(url));

        var (code, stdout, stderr) = await RunAsync(args, ct, TimeSpan.FromSeconds(90));
        var warnings = stderr.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("WARNING", StringComparison.Ordinal)).Take(6).ToList();
        if (warnings.Count > 0) Log.Info("yt-dlp warnings:\n    " + string.Join("\n    ", warnings));
        if (code != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException(ErrorFrom(stderr, "مقدرتش أجيب الفيديو"));

        return ParseStream(stdout);
    }

    /// <summary>تحليل ناتج yt-dlp -j (public عشان يتجرّب لوحده).</summary>
    public static OnlineStream ParseStream(string json)
    {
        using var doc = ParseFirstJson(json);
        var root = doc.RootElement;

        string? videoUrl = null, audioUrl = null;
        var parts = new List<StreamPart>();
        if (root.TryGetProperty("requested_formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in formats.EnumerateArray())
            {
                string? u = Str(f, "url");
                if (u == null) continue;
                bool hasVideo = Str(f, "vcodec") is { } vc && vc != "none";
                if (hasVideo && videoUrl == null) videoUrl = u;
                else if (!hasVideo && audioUrl == null) audioUrl = u;
                parts.Add(PartFrom(f, root, u));
            }
        }
        if (videoUrl == null && Str(root, "url") is { } single)
        {
            videoUrl = single;
            parts.Add(PartFrom(root, root, single));
        }
        if (videoUrl == null) throw new InvalidOperationException("أداة يوتيوب مرجّعتش رابط تشغيل");

        string? userAgent = null, referer = null;
        if (root.TryGetProperty("http_headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
        {
            userAgent = Str(headers, "User-Agent");
            referer = Str(headers, "Referer");
        }
        bool isLive = root.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True;
        string? manifest = Str(root, "manifest_url");
        if (manifest == null && formats.ValueKind == JsonValueKind.Array)
            foreach (var f in formats.EnumerateArray())
                if (Str(f, "manifest_url") is { } m) { manifest = m; break; }
        // يوتيوب بقى بيفرض انتظار مدة الإعلان قبل ما الرابط يشتغل (available_at)؛ قبلها أي طلب بيترفض 403
        long availableAt = 0;
        foreach (var src in new[] { root }.Concat(formats.ValueKind == JsonValueKind.Array ? formats.EnumerateArray() : Enumerable.Empty<JsonElement>()))
            if (src.TryGetProperty("available_at", out var at) && at.ValueKind == JsonValueKind.Number && at.TryGetInt64(out var t))
                availableAt = Math.Max(availableAt, t);
        DateTimeOffset? available = availableAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(availableAt) : null;
        return new OnlineStream(videoUrl, audioUrl, userAgent, referer, Seconds(root, "duration"), isLive, manifest, parts, available);
    }

    private static StreamPart PartFrom(JsonElement f, JsonElement root, string url)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var src in new[] { root, f }) // هيدرز الصيغة نفسها بتكسب
        {
            if (src.TryGetProperty("http_headers", out var h) && h.ValueKind == JsonValueKind.Object)
                foreach (var p in h.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) headers[p.Name] = p.Value.GetString()!;
        }
        string vcodec = Str(f, "vcodec") ?? "", acodec = Str(f, "acodec") ?? "";
        bool hasVideo = vcodec != "none" && vcodec != "";
        bool hasAudio = acodec != "none" && acodec != "";
        if (!hasVideo && !hasAudio) { hasVideo = true; hasAudio = true; } // صيغة من غير بيانات: نعتبرها كاملة
        string desc = $"{Str(f, "format_id")} {Str(f, "protocol")} {vcodec}/{acodec} {(f.TryGetProperty("height", out var hh) && hh.ValueKind == JsonValueKind.Number ? hh.GetInt32() + "p" : "")}";
        return new StreamPart(Str(f, "format_id") ?? "", url, Str(f, "protocol") ?? "", hasVideo, hasAudio, headers, desc.Trim());
    }

    // =====================================================================
    //  تنزيل وتحديث الأدوات
    // =====================================================================

    public static async Task DownloadToolsAsync(bool includeDeno, IProgress<string> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ToolsDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LecturePlayer/1.2");

        await DownloadFileAsync(http, YtDlpDownloadUrl, Path.Combine(ToolsDir, "yt-dlp.exe"), "yt-dlp", progress, ct);

        if (includeDeno)
        {
            string zip = Path.Combine(ToolsDir, "deno.zip");
            await DownloadFileAsync(http, DenoDownloadUrl, zip, "الأداة المساعدة", progress, ct);
            progress.Report("بجهّز الأداة المساعدة…");
            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("deno.exe", StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidOperationException("ملف Deno المضغوط مفيهوش deno.exe");
                entry.ExtractToFile(Path.Combine(ToolsDir, "deno.exe"), overwrite: true);
            }
            File.Delete(zip);
        }
        progress.Report("الأدوات جاهزة ✓");
    }

    internal static async Task DownloadFileAsync(HttpClient http, string url, string target, string label,
        IProgress<string> progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;

        string tmp = target + ".download";
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(tmp))
        {
            var buffer = new byte[81920];
            long done = 0;
            int lastPercent = -1, read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                int percent = total > 0 ? (int)(done * 100 / total.Value) : -1;
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress.Report(percent >= 0 ? $"بنزّل {label}… {percent}%" : $"بنزّل {label}… {done / 1_048_576} ميجا");
                }
            }
        }
        File.Move(tmp, target, overwrite: true);
    }

    // =====================================================================
    //  تنزيل للمشاهدة من غير نت
    // =====================================================================

    /// <summary>
    /// بينزّل الفيديو في outDir ويرجّع مسار الملف النهائي.
    /// الصورة والصوت بيتنزلوا منفصلين وffmpeg بيدمجهم (عشان كده محتاجين ffmpeg).
    /// </summary>
    public static async Task<string> DownloadVideoAsync(string url, string outDir, int maxHeight,
        IProgress<int> percent, CancellationToken ct)
    {
        string exe = FindYtDlp() ?? throw new FileNotFoundException("أداة يوتيوب مش موجودة");
        Directory.CreateDirectory(outDir);
        string sort = maxHeight > 0 ? $"res:{maxHeight},vcodec:h264,acodec:m4a" : "vcodec:h264,acodec:m4a";
        var args = new List<string>
        {
            "--no-playlist", "--newline",
            "-f", "bv*+ba/b", "-S", sort, "--merge-output-format", "mp4",
            "-o", Path.Combine(outDir, "%(title).120B [%(id)s].%(ext)s"),
            "--print", "after_move:filepath"
        };
        args.AddRange(CommonArgs());
        if (Directory.Exists(Tools.FfmpegDir)) { args.Add("--ffmpeg-location"); args.Add(Tools.FfmpegDir); }
        args.Add(NormalizeUrl(url));

        var r = await ProcessRunner.RunAsync(exe, args, TimeSpan.FromHours(3), ct, line =>
        {
            int? p = ParseDownloadPercent(line);
            if (p.HasValue) percent.Report(p.Value);
        });
        string? file = r.StdOut.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && File.Exists(l));
        if (r.ExitCode != 0 || file == null)
            throw new InvalidOperationException(ErrorFrom(r.StdErr, "مقدرتش أنزّل الفيديو"));
        return file;
    }

    /// <summary>«[download]  45.3% of 120MiB» ← 45.</summary>
    public static int? ParseDownloadPercent(string line)
    {
        if (!line.StartsWith("[download]", StringComparison.Ordinal)) return null;
        int pct = line.IndexOf('%');
        if (pct < 0) return null;
        int start = pct - 1;
        while (start >= 0 && (char.IsDigit(line[start]) || line[start] == '.')) start--;
        string num = line.Substring(start + 1, pct - start - 1);
        return double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? (int)Math.Clamp(v, 0, 100) : null;
    }

    /// <summary>yt-dlp -U (يوتيوب بيغيّر حاجات كتير، فالتحديث بيحل أغلب المشاكل).</summary>
    public static async Task<string> UpdateAsync(CancellationToken ct)
    {
        var (_, stdout, stderr) = await RunAsync(new[] { "-U", "--encoding", "utf-8" }, ct, TimeSpan.FromMinutes(3));
        string text = (stdout + "\n" + stderr).Trim();
        return text.Length > 0 ? text : "تم";
    }

    // =====================================================================
    //  تشغيل yt-dlp
    // =====================================================================

    /// <summary>
    /// أداة يوتيوب نفسها بتسحب الفيديو وتكتبه على stdout (نفس طريقة التنزيل بالظبط: بتستنى وقت الإتاحة،
    /// وبتتعامل مع أي حماية جديدة من يوتيوب). الوسيط بيمرره للمشغل وهو بيتسحب، من غير ما يتحفظ على الجهاز.
    /// </summary>
    public static Process StartPipe(string url, string format)
    {
        string exe = FindYtDlp() ?? throw new FileNotFoundException("أداة يوتيوب مش موجودة");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string a in new[] { "-f", format, "-o", "-", "--no-part", "--no-playlist", "--quiet", "--no-progress" }) psi.ArgumentList.Add(a);
        foreach (string a in CommonArgs()) psi.ArgumentList.Add(a);
        if (_botWorkaround is { } w) foreach (string a in w) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(NormalizeUrl(url));
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        psi.Environment["PATH"] = ToolsDir + Path.PathSeparator + AppContext.BaseDirectory + Path.PathSeparator + path;
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        var process = new Process { StartInfo = psi };
        process.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                string err = await process.StandardError.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(err)) Log.Info("yt-dlp pipe: " + err.Trim()[..Math.Min(err.Trim().Length, 600)]);
            }
            catch { }
        });
        return process;
    }

    // =====================================================================
    //  «Sign in to confirm you're not a bot»
    // =====================================================================

    /// <summary>يوتيوب طالب يتأكد إن الطلب مش من روبوت (بيحصل على شبكات المدارس والشركات وبعض الـ IPs).</summary>
    public static bool IsBotCheck(string stderr) =>
        stderr.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase) && stderr.Contains("bot", StringComparison.OrdinalIgnoreCase);

    /// <summary>الطريقة اللي نجحت مع فحص الروبوت في الجلسة دي (بتتجرّب الأول المرة الجاية).</summary>
    private static string[]? _botWorkaround;

    /// <summary>لما البرنامج يلاقي المتصفح اللي تسجيل الدخول منه حل المشكلة (عشان يتحفظ في الإعدادات).</summary>
    public static event Action<string>? CookiesBrowserFound;

    /// <summary>
    /// الطرق البديلة بالترتيب: «عملاء» تانيين من يوتيوب (تطبيق النظارة، التليفزيون، الموبايل) غالبًا مش بيطلبوا الفحص ده،
    /// وبعدين تسجيل دخولك من المتصفحات اللي على الجهاز (Firefox الأول لأنه الأضمن على ويندوز).
    /// </summary>
    private static IEnumerable<(string Name, string[] Args)> BotWorkarounds()
    {
        yield return ("android_vr", new[] { "--extractor-args", "youtube:player_client=android_vr" });
        yield return ("tv_simply", new[] { "--extractor-args", "youtube:player_client=tv_simply" });
        yield return ("mweb", new[] { "--extractor-args", "youtube:player_client=mweb" });
        if (!string.IsNullOrWhiteSpace(CookiesBrowser)) yield break; // المستخدم مختار متصفح بالفعل
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var browsers = new (string Name, string Profile)[]
        {
            ("firefox", Path.Combine(appData, "Mozilla", "Firefox", "Profiles")),
            ("edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
        };
        foreach (var (name, profile) in browsers)
            if (Directory.Exists(profile))
                yield return ("cookies:" + name, new[] { "--cookies-from-browser", name });
    }

    /// <summary>
    /// بيشغّل الأداة، ولو يوتيوب طلب «أكّد إنك مش روبوت» بيجرّب الطرق البديلة لوحده (من غير ما يسأل المستخدم)
    /// ويفتكر اللي نجحت.
    /// </summary>
    private static async Task<(int Code, string StdOut, string StdErr)> RunAsync(
        IEnumerable<string> args, CancellationToken ct, TimeSpan timeout)
    {
        var list = args.ToList();
        if (list.Contains("-U")) return await RunOnceAsync(list, ct, timeout);

        if (_botWorkaround is { } known)
        {
            var r = await RunOnceAsync(WithExtra(list, known), ct, timeout);
            if (!IsBotCheck(r.StdErr)) return r;
            _botWorkaround = null;
        }
        var first = await RunOnceAsync(list, ct, timeout);
        if (!IsBotCheck(first.StdErr)) return first;

        Log.Info("youtube bot check — trying workarounds");
        foreach (var (name, extra) in BotWorkarounds())
        {
            ct.ThrowIfCancellationRequested();
            var r = await RunOnceAsync(WithExtra(list, extra), ct, timeout);
            if (r.Code == 0 && !IsBotCheck(r.StdErr))
            {
                Log.Info("youtube bot check passed with: " + name);
                _botWorkaround = extra;
                if (name.StartsWith("cookies:", StringComparison.Ordinal))
                {
                    CookiesBrowser = name[8..];
                    CookiesBrowserFound?.Invoke(CookiesBrowser);
                }
                return r;
            }
            Log.Info($"youtube bot workaround {name} failed: {FirstErrorLine(r.StdErr)}");
        }
        return first;
    }

    private static string FirstErrorLine(string stderr) =>
        stderr.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("ERROR", StringComparison.Ordinal)) is { } e
            ? e[..Math.Min(e.Length, 200)] : "";

    private static List<string> WithExtra(List<string> args, string[] extra)
    {
        // الإضافات قبل الرابط (آخر حاجة في الأوامر)
        var r = new List<string>(args);
        r.InsertRange(Math.Max(0, r.Count - 1), extra);
        return r;
    }

    private static async Task<(int Code, string StdOut, string StdErr)> RunOnceAsync(
        IEnumerable<string> args, CancellationToken ct, TimeSpan timeout)
    {
        string exe = FindYtDlp() ?? throw new FileNotFoundException("أداة يوتيوب مش موجودة");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        // عشان yt-dlp يلاقي deno.exe اللي في فولدر الأدوات
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        psi.Environment["PATH"] = ToolsDir + Path.PathSeparator + AppContext.BaseDirectory + Path.PathSeparator + path;
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException("أداة يوتيوب أخدت وقت طويل ومردّتش — جرّب تاني أو اعمل تحديث للأداة");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    /// <summary>yt-dlp -j بيطلع JSON في سطر لكل فيديو؛ لو جه أكتر من سطر ناخد أول واحد.</summary>
    private static JsonDocument ParseFirstJson(string json)
    {
        string text = json.Trim();
        try { return JsonDocument.Parse(text); }
        catch (JsonException)
        {
            string first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
            return JsonDocument.Parse(first);
        }
    }

    private static string ErrorFrom(string stderr, string fallback)
    {
        var line = stderr.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase));
        return line ?? (string.IsNullOrWhiteSpace(stderr) ? fallback : stderr.Trim().Split('\n')[^1].Trim());
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Seconds(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (long)(v.GetDouble() * 1000) : 0;
}
