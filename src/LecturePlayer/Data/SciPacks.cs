using System.Collections.Concurrent;

namespace LecturePlayer.Data;

/// <summary>
/// مكتبة الرسوم العلمية على السبورة: الرسوم مش جوه المثبّت — بتتنزل عند أول طلب من مصادرها المفتوحة
/// (نسخ مثبّتة برقم commit على GitHub، وPhyloPic) وتتحفظ في فولدر البرنامج، فبعد كده بتشتغل من غير نت.
/// الطلب لازم يكون لمصدر من القائمة المسموحة بس.
/// </summary>
internal static class SciPacks
{
    public static string CacheDir => Path.Combine(Storage.AppDataDir, "sci-packs");

    /// <summary>المستودعات المسموح التنزيل منها (owner/repo).</summary>
    private static readonly HashSet<string> Repos = new(StringComparer.OrdinalIgnoreCase)
    {
        "duerrsimon/bioicons", "resolvetosavelives/healthicons", "waelMowis/LecturePlayer"
    };

    private static readonly HttpClient Http = CreateHttp();
    private static HttpClient CreateHttp()
    {
        var h = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(10) }) { Timeout = TimeSpan.FromSeconds(60) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Masar-LecturePlayer/4 (+https://github.com/waelMowis/LecturePlayer)");
        return h;
    }
    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> Pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>عنوان المصدر الأصلي لطلب من القارئ (أو null لو مش مسموح).</summary>
    public static (string Url, string? Cache)? Resolve(string route, string query)
    {
        route = route.Replace('\\', '/');
        if (route.Contains("..") || route.Contains("//") || route.Contains(':')) return null;
        var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 5 && parts[0] == "gh" && Repos.Contains(parts[1] + "/" + parts[2]))
        {
            string rest = string.Join('/', parts.Skip(3).Select(Uri.EscapeDataString));
            return ($"https://raw.githubusercontent.com/{parts[1]}/{parts[2]}/{rest}", Path.Combine(new[] { CacheDir }.Concat(parts).ToArray()));
        }
        // PubChem (بحث المركبات وبناها ثلاثية الأبعاد) من غير حفظ
        if (parts.Length is >= 3 and <= 8 && parts[0] == "pubchem" && parts[1] == "compound" && parts.Skip(1).All(p => p.Length is > 0 and < 120 && p.All(c => c is not ('/' or '\\' or '?' or '#'))))
            return ("https://pubchem.ncbi.nlm.nih.gov/rest/pug/" + string.Join('/', parts.Skip(1).Select(Uri.EscapeDataString)) + SafeQuery(query), null);
        // Wikimedia Commons: البحث (من غير حفظ) والصور (بتتحفظ)
        if (parts.Length == 2 && parts[0] == "wiki" && parts[1] == "api")
            return ("https://commons.wikimedia.org/w/api.php" + SafeQuery(query), null);
        if (parts.Length >= 4 && parts[0] == "wiki" && parts[1] == "img" && parts.Skip(2).All(p => p.Length is > 0 and < 260))
            return ("https://upload.wikimedia.org/wikipedia/commons/" + string.Join('/', parts.Skip(2).Select(Uri.EscapeDataString)), Path.Combine(new[] { CacheDir, "wiki" }.Concat(parts.Skip(2).Select(SafeName)).ToArray()));
        // PhyloPic: البحث (من غير حفظ) والصور (بتتحفظ)
        if (parts.Length >= 2 && parts[0] == "phylo" && parts[1] == "api" && parts.Length <= 4 && parts.Skip(2).All(p => p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
            return ("https://api.phylopic.org/" + string.Join('/', parts.Skip(2)) + SafeQuery(query), null);
        if (parts.Length is 4 or 5 && parts[0] == "phylo" && parts[1] == "img" && Guid.TryParse(parts[2], out _)
            && parts.Skip(3).All(p => p.Length > 0 && p.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
            return ($"https://images.phylopic.org/images/{string.Join('/', parts.Skip(2))}", Path.Combine(new[] { CacheDir, "phylo" }.Concat(parts.Skip(2)).ToArray()));
        return null;
    }

    /// <summary>اسم ملف آمن على ويندوز (أسماء ملفات ويكيميديا فيها رموز).</summary>
    private static string SafeName(string p) => string.Concat(p.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c));

    private static string SafeQuery(string q) =>
        string.IsNullOrEmpty(q) ? "" : q.All(c => c is not ('\r' or '\n' or '#')) && q.StartsWith('?') ? q : "";

    /// <summary>بيرجّع مسار الملف المحفوظ (وينزّله لو أول مرة).</summary>
    public static Task<string?> FileAsync(string url, string cache)
    {
        if (File.Exists(cache)) return Task.FromResult<string?>(cache);
        var lazy = Pending.GetOrAdd(cache, _ => new Lazy<Task<string?>>(() => DownloadAsync(url, cache)));
        return lazy.Value.ContinueWith(t => { Pending.TryRemove(cache, out _); return t.IsCompletedSuccessfully ? t.Result : null; });
    }

    private static async Task<string?> DownloadAsync(string url, string cache)
    {
        try
        {
            using var r = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!r.IsSuccessStatusCode) { Log.Info($"sci-packs: {(int)r.StatusCode} {url}"); return null; }
            if (r.Content.Headers.ContentLength is long n && n > 40L << 20) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            string tmp = cache + ".part";
            await using (var fs = File.Create(tmp))
                await r.Content.CopyToAsync(fs);
            File.Move(tmp, cache, true);
            return cache;
        }
        catch (Exception ex) { Log.Info("sci-packs: " + ex.Message + " " + url); return null; }
    }

    /// <summary>طلب بحث (JSON) من غير حفظ.</summary>
    public static async Task<(int Status, byte[] Body, string Type)> GetAsync(string url)
    {
        try
        {
            using var r = await Http.GetAsync(url);
            string type = r.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            // نسمح بأنواع البيانات والصور بس (مش صفحات HTML بسكربتات من مواقع تانية)
            if (!(type.StartsWith("application/json") || type.StartsWith("text/plain") || type.StartsWith("chemical/") || type.StartsWith("image/png") || type.StartsWith("image/jpeg") || type.StartsWith("application/octet-stream")))
                type = "text/plain; charset=utf-8";
            return ((int)r.StatusCode, await r.Content.ReadAsByteArrayAsync(), type);
        }
        catch (Exception ex) { Log.Info("sci-packs: " + ex.Message); return (502, Array.Empty<byte>(), "text/plain"); }
    }
}
