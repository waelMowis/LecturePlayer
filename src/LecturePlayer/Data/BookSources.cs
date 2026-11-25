using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>
/// كتب من روابط (Google Drive / Google Docs / Dropbox / OneDrive / رابط مباشر):
/// الرابط بيتحوّل لرابط تنزيل، والملف بيتنزل في كاش على الجهاز ده، وبنعرف لو اتحدّث على المصدر.
/// </summary>
public static class BookSources
{
    public enum Kind { Direct, GoogleDrive, GoogleDocs, Dropbox, OneDrive }

    public sealed record Link(Kind Kind, string Original, string Download, string? Id);

    private static readonly Regex DriveFileRx = new(@"drive\.google\.com/(?:file/d/|open\?id=|uc\?(?:[^#]*&)?id=)(?<id>[\w-]{10,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DriveUserContentRx = new(@"drive\.usercontent\.google\.com/download\?(?:[^#]*&)?id=(?<id>[\w-]{10,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DocsRx = new(@"docs\.google\.com/(?<kind>document|presentation|spreadsheets)/d/(?<id>[\w-]{10,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>بيفهم الرابط ويطلع رابط التنزيل المباشر (null = مش رابط).</summary>
    public static Link? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string url = text.Trim().Trim('"', '\'', '<', '>');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https")) return null;
        string host = u.Host.ToLowerInvariant();

        if (DocsRx.Match(url) is { Success: true } dm)
        {
            string id = dm.Groups["id"].Value;
            string kind = dm.Groups["kind"].Value.ToLowerInvariant();
            return new Link(Kind.GoogleDocs, url, $"https://docs.google.com/{kind}/d/{id}/export?format=pdf", id);
        }
        var gm = DriveFileRx.Match(url);
        if (!gm.Success) gm = DriveUserContentRx.Match(url);
        if (gm.Success)
        {
            string id = gm.Groups["id"].Value;
            return new Link(Kind.GoogleDrive, url, $"https://drive.usercontent.google.com/download?id={id}&export=download&confirm=t", id);
        }
        if (host is "dropbox.com" or "www.dropbox.com" or "dl.dropbox.com" or "dl.dropboxusercontent.com")
        {
            // dl=0 (صفحة المعاينة) ← dl=1 (الملف نفسه)، والـ rlkey بيفضل
            var q = System.Web.HttpUtility.ParseQueryString(u.Query);
            q.Remove("dl");
            q.Remove("raw");
            q["dl"] = "1";
            var b = new UriBuilder(u) { Query = q.ToString() ?? "dl=1" };
            if (host == "dl.dropbox.com") b.Host = "www.dropbox.com";
            return new Link(Kind.Dropbox, url, b.Uri.AbsoluteUri, null);
        }
        if (host is "1drv.ms" or "onedrive.live.com" or "api.onedrive.com")
        {
            // روابط المشاركة الشخصية: واجهة shares بترجّع الملف نفسه
            string token = "u!" + Convert.ToBase64String(Encoding.UTF8.GetBytes(url)).TrimEnd('=').Replace('/', '_').Replace('+', '-');
            return new Link(Kind.OneDrive, url, $"https://api.onedrive.com/v1.0/shares/{token}/root/content", null);
        }
        if (host.EndsWith(".sharepoint.com") || host.EndsWith("-my.sharepoint.com"))
        {
            // OneDrive للشغل/الجامعة: download=1 بيرجّع الملف (لو الرابط مفتوح للكل)
            string sep = string.IsNullOrEmpty(u.Query) ? "?" : "&";
            return new Link(Kind.OneDrive, url, url.Contains("download=1", StringComparison.OrdinalIgnoreCase) ? url : url + sep + "download=1", null);
        }
        return new Link(Kind.Direct, url, url, null);
    }

    public static string KindName(Kind k) => k switch
    {
        Kind.GoogleDrive => "Google Drive",
        Kind.GoogleDocs => "Google Docs",
        Kind.Dropbox => "Dropbox",
        Kind.OneDrive => "OneDrive",
        _ => "رابط"
    };

    /// <summary>مكان النسخة المتنزلة على الجهاز ده (فولدر Data جنب البرنامج).</summary>
    public static string CacheDir => Path.Combine(Storage.AppDataDir, "books");

    /// <summary>مكان النسخة المتنزلة (.pdf أو .epub حسب اللي نزل فعلًا).</summary>
    public static string CachePathFor(string url)
    {
        string stem = Path.Combine(CacheDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url.Trim())))[..24].ToLowerInvariant());
        if (File.Exists(stem + ".epub")) return stem + ".epub";
        if (File.Exists(stem + ".pdf")) return stem + ".pdf";
        return stem + (BookFiles.FormatOf(url) == "epub" ? ".epub" : ".pdf");
    }

    /// <summary>ملف كتاب (PDF أو EPUB) من أول بايتات فيه.</summary>
    public static bool LooksLikeBook(byte[] head, int count) => LooksLikePdf(head, count) || LooksLikeEpub(head, count);

    public static bool LooksLikeEpub(byte[] head, int count) =>
        count > 50 && head[0] == (byte)'P' && head[1] == (byte)'K' && Encoding.Latin1.GetString(head, 0, Math.Min(count, 120)).Contains("application/epub+zip", StringComparison.Ordinal);

    /// <summary>علامة النسخة المتنزلة على الجهاز ده (جنب الملف في الكاش).</summary>
    public static string? ReadStamp(string cachePath)
    {
        try { return File.Exists(cachePath + ".stamp") ? File.ReadAllText(cachePath + ".stamp").Trim() : null; } catch { return null; }
    }

    public static void WriteStamp(string cachePath, string stamp)
    {
        try { File.WriteAllText(cachePath + ".stamp", stamp); } catch { }
    }

    /// <summary>الملف PDF فعلًا (مش صفحة HTML بتقول «سجّل دخول»).</summary>
    public static bool LooksLikePdf(byte[] head, int count)
    {
        // %PDF- ممكن ييجي بعد شوية بايتات (BOM/مسافات) — المواصفة بتسمح لحد 1024
        string s = Encoding.Latin1.GetString(head, 0, Math.Min(count, 1024));
        return s.Contains("%PDF-", StringComparison.Ordinal);
    }

    public sealed class NotPdfException(string message, bool needsSignIn) : Exception(message)
    {
        public bool NeedsSignIn { get; } = needsSignIn;
    }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10, AutomaticDecompression = DecompressionMethods.All };
        var c = new HttpClient(h) { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Masar");
        return c;
    }

    /// <summary>نتيجة التنزيل: المسار، وعلامة النسخة (ETag/التاريخ/الحجم) عشان نعرف بعدين لو اتغيّر.</summary>
    public sealed record Download(string Path, string Stamp, long Bytes, string? FileName);

    /// <summary>
    /// بيتنزّل الكتاب في الكاش (ملف مؤقت وبعدين يتنقل، عشان النسخة القديمة ماتبوظش لو التنزيل وقف).
    /// progress = (المتنزل، الإجمالي أو -1). بيرمي NotPdfException لو الرد مش PDF (غالبًا محتاج تسجيل دخول).
    /// </summary>
    public static async Task<Download> DownloadAsync(string url, string target, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        var link = Parse(url) ?? throw new ArgumentException("رابط غير مفهوم");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var req = new HttpRequestMessage(HttpMethod.Get, link.Download);
        req.Headers.Accept.ParseAdd("application/pdf,application/octet-stream;q=0.9,*/*;q=0.5");
        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            throw new NotPdfException(res.StatusCode == HttpStatusCode.NotFound ? "الملف غير موجود على هذا الرابط (أو أنه خاص بك)" : "الملف خاص — يحتاج إلى تسجيل الدخول", needsSignIn: true);
        res.EnsureSuccessStatusCode();
        long total = res.Content.Headers.ContentLength ?? -1;
        string tmp = target + ".part";
        long done = 0;
        var head = new byte[1024];
        int headCount = 0;
        await using (var src = await res.Content.ReadAsStreamAsync(ct))
        await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buf = new byte[1 << 16];
            int n;
            long lastReport = 0;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                if (headCount < head.Length) { int c = Math.Min(n, head.Length - headCount); Array.Copy(buf, 0, head, headCount, c); headCount += c; }
                if (headCount >= 8 && done == 0 && !LooksLikeBook(head, headCount) && headCount >= Math.Min(head.Length, (int)Math.Max(8, total)))
                    break; // مش PDF: مانكمّلش تنزيل صفحة HTML كبيرة
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (done - lastReport > 256 * 1024 || done == total) { progress?.Report((done, total)); lastReport = done; }
            }
        }
        if (!LooksLikeBook(head, headCount))
        {
            try { File.Delete(tmp); } catch { }
            string? type = res.Content.Headers.ContentType?.MediaType;
            bool html = type?.Contains("html", StringComparison.OrdinalIgnoreCase) == true || Encoding.UTF8.GetString(head, 0, headCount).Contains("<html", StringComparison.OrdinalIgnoreCase);
            throw new NotPdfException(html ? "الرابط أعاد صفحة وليس ملف PDF — غالبًا الملف خاص ويحتاج إلى تسجيل الدخول" : "هذا الملف ليس PDF", needsSignIn: html);
        }
        // EPUB من رابط مالوش امتداد (درايف مثلًا): يتحفظ .epub عشان القارئ يفتحه صح
        string ext = LooksLikeEpub(head, headCount) ? ".epub" : ".pdf";
        if (!target.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(target); } catch { }
            target = Path.ChangeExtension(target, ext);
        }
        File.Move(tmp, target, overwrite: true);
        progress?.Report((done, done));
        return new Download(target, StampOf(res), done, FileNameOf(res));
    }

    /// <summary>هل الملف على المصدر اتغيّر عن النسخة اللي عندنا؟ (null = مش عارفين، مثلًا من غير نت)</summary>
    public static async Task<bool?> ChangedAsync(string url, string? stamp, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(stamp) || Parse(url) is not { } link) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, link.Download);
            req.Headers.Range = new RangeHeaderValue(0, 0); // من غير ما ننزّل الملف كله
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) return null;
            string now = StampOf(res);
            if (now.Length == 0) return null;
            return !SameStamp(now, stamp);
        }
        catch (Exception) { return null; }
    }

    /// <summary>ETag أو التاريخ أو الحجم الكلي (أيهم موجود).</summary>
    private static string StampOf(HttpResponseMessage res)
    {
        var parts = new List<string>();
        if (res.Headers.ETag?.Tag is { } etag) parts.Add("e:" + etag.Trim('"').Replace("W/", ""));
        if (res.Content.Headers.LastModified is { } lm) parts.Add("m:" + lm.UtcDateTime.ToString("yyyyMMddHHmmss"));
        long? size = res.Content.Headers.ContentRange?.Length ?? (res.StatusCode == HttpStatusCode.PartialContent ? null : res.Content.Headers.ContentLength);
        if (size is > 0) parts.Add("s:" + size);
        return string.Join("|", parts);
    }

    /// <summary>نفس النسخة: أي جزء مشترك بنفس القيمة ومفيش جزء مشترك بيختلف.</summary>
    public static bool SameStamp(string a, string b)
    {
        var x = a.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(':', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        var y = b.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(':', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        var common = x.Keys.Intersect(y.Keys).ToList();
        if (common.Count == 0) return true; // مفيش حاجة نقارن بيها: نعتبره زي ما هو
        // الـ ETag لوحده كفاية لو موجود عند الاتنين
        if (x.ContainsKey("e") && y.ContainsKey("e")) return x["e"] == y["e"];
        return common.All(k => x[k] == y[k]);
    }

    private static string? FileNameOf(HttpResponseMessage res)
    {
        var cd = res.Content.Headers.ContentDisposition;
        string? name = cd?.FileNameStar ?? cd?.FileName;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim('"');
    }

    /// <summary>اسم كتاب معقول من الرابط/اسم الملف اللي رجع.</summary>
    public static string TitleFor(Link link, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName)) return BookFiles.TitleFromFile(fileName);
        if (link.Kind == Kind.Direct && Uri.TryCreate(link.Original, UriKind.Absolute, out var u))
        {
            string last = Uri.UnescapeDataString(u.Segments.LastOrDefault() ?? "").Trim('/');
            if (last.Length > 0) return BookFiles.TitleFromFile(last);
        }
        return "كتاب من " + KindName(link.Kind);
    }
}
