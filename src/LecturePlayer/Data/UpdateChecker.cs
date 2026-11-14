using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace LecturePlayer.Data;

/// <summary>
/// التحقق من وجود إصدار جديد على GitHub Releases (بعد ما المشروع يتنشر).
/// المستودع بيتكتب في الإعدادات (owner/repo)، ولو فاضي مفيش تحقق.
/// </summary>
public static class UpdateChecker
{
    public sealed record Release(Version Version, string Tag, string Url, string Notes, string? ZipAssetUrl,
        string? SetupUrl = null, string? SetupName = null, long SetupSize = 0, string? SetupSha256 = null);

    /// <summary>المستودع المكتوب جوه البرنامج وقت البناء (LpGitHubRepo في الـ csproj).</summary>
    public static string? BuiltInRepo =>
        typeof(UpdateChecker).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateRepo")?.Value is { Length: > 0 } r ? r.Trim() : null;

    public static Version CurrentVersion =>
        typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(1, 0);

    public static async Task<Release?> GetLatestAsync(string repo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/')) return null;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Masar-UpdateCheck");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        string json = await http.GetStringAsync($"https://api.github.com/repos/{repo.Trim()}/releases/latest", ct);
        return ParseRelease(json);
    }

    /// <summary>تحليل رد GitHub (public عشان يتجرّب لوحده).</summary>
    public static Release? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!TryParseVersion(tag, out var version)) return null;

        string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string? zip = null, setupUrl = null, setupName = null, sha = null;
        long setupSize = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string? download = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                if (download == null) continue;
                if (zip == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip = download;
                // المثبّت: Masar-Setup-4.1.0.exe (أو أي exe فيه Setup)
                if (setupUrl == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                {
                    setupUrl = download;
                    setupName = name;
                    setupSize = a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : 0;
                    // GitHub بيحط «sha256:...» لكل ملف: بنتأكد إن الملف اتنزّل سليم ومحدش لعب فيه
                    if (a.TryGetProperty("digest", out var dg) && dg.GetString() is { } digest &&
                        digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        sha = digest[7..].Trim().ToLowerInvariant();
                }
            }
        }
        return new Release(version, tag, url, notes, zip, setupUrl, setupName, setupSize, sha);
    }

    /// <summary>
    /// ينزّل المثبّت في فولدر مؤقت ويتأكد منه (المصدر GitHub نفس المستودع، والحجم، وبصمة sha256 لو موجودة).
    /// </summary>
    public static async Task<string> DownloadSetupAsync(Release r, string repo, IProgress<int> percent, CancellationToken ct)
    {
        if (r.SetupUrl == null || r.SetupName == null) throw new InvalidOperationException("الإصدار ده مالوش مثبّت على GitHub");
        string expectedPrefix = $"https://github.com/{repo.Trim().Trim('/')}/releases/download/";
        if (!r.SetupUrl.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("رابط التحديث مش من مستودع البرنامج، فمش هنزّله.");

        string dir = Path.Combine(Path.GetTempPath(), "Masar-Update");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, string.Concat(r.SetupName.Split(Path.GetInvalidFileNameChars())));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Masar-Updater");
        using (var resp = await http.GetAsync(r.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? r.SetupSize;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(file);
            var buffer = new byte[81920];
            long done = 0;
            int read, last = -1;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0)
                {
                    int p = (int)(done * 100 / total);
                    if (p != last) { last = p; percent.Report(p); }
                }
            }
        }

        var info = new FileInfo(file);
        if (r.SetupSize > 0 && info.Length != r.SetupSize)
        {
            File.Delete(file);
            throw new InvalidOperationException("ملف التحديث اتنزّل ناقص. جرّب تاني.");
        }
        if (r.SetupSha256 != null)
        {
            await using var fs = File.OpenRead(file);
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            if (hash != r.SetupSha256)
            {
                fs.Close();
                File.Delete(file);
                throw new InvalidOperationException("بصمة ملف التحديث مش مطابقة لـ GitHub، فمش هثبّته.");
            }
        }
        return file;
    }

    /// <summary>«v2.1.0» أو «2.1» ← Version.</summary>
    public static bool TryParseVersion(string tag, out Version version)
    {
        string s = tag.Trim().TrimStart('v', 'V');
        int dash = s.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0) s = s[..dash];
        if (Version.TryParse(s, out var v)) { version = v; return true; }
        if (int.TryParse(s, out int major)) { version = new Version(major, 0); return true; }
        version = new Version(0, 0);
        return false;
    }

    public static bool IsNewer(Version candidate, Version current) =>
        Normalize(candidate) > Normalize(current);

    private static Version Normalize(Version v) =>
        new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
