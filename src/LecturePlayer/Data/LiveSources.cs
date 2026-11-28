using System.Text.Json;
using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>محطة من دليل Radio Browser.</summary>
public sealed record RadioStation(string Uuid, string Name, string Url, string Country, string CountryCode,
    string Language, string Tags, string Codec, int Bitrate, int Votes, string Favicon, string Homepage)
{
    public LiveChannel ToChannel() => new()
    {
        Id = "rb:" + Uuid, Name = Name.Trim(), Url = Url, Kind = LiveKind.Radio, Group = Country,
        Tags = Tags, Logo = Favicon, StationUuid = Uuid
    };
}

/// <summary>
/// Radio Browser (radio-browser.info): دليل مجاني ومفتوح لمحطات راديو الإنترنت، من غير مفتاح.
/// ليه كذا سيرفر (mirror)؛ بنجرّب واحد ورا التاني ونفتكر اللي اشتغل.
/// </summary>
public static class RadioBrowser
{
    private static readonly string[] Mirrors =
    {
        "https://de1.api.radio-browser.info", "https://nl1.api.radio-browser.info",
        "https://at1.api.radio-browser.info", "https://de2.api.radio-browser.info", "https://all.api.radio-browser.info"
    };
    private static string? _working;

    private static readonly HttpClient Http = CreateClient();
    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Masar/4 (+https://github.com/waelMowis/LecturePlayer)");
        return h;
    }

    /// <summary>الدول العربية الأول، وبعدها «كل الدول».</summary>
    public static readonly (string Code, string Name)[] Countries =
    {
        ("", "كل الدول"), ("EG", "مصر"), ("SA", "السعودية"), ("AE", "الإمارات"), ("KW", "الكويت"), ("QA", "قطر"),
        ("BH", "البحرين"), ("OM", "عُمان"), ("JO", "الأردن"), ("LB", "لبنان"), ("SY", "سوريا"), ("IQ", "العراق"),
        ("PS", "فلسطين"), ("MA", "المغرب"), ("DZ", "الجزائر"), ("TN", "تونس"), ("LY", "ليبيا"), ("SD", "السودان"),
        ("YE", "اليمن"), ("TR", "تركيا"), ("GB", "بريطانيا"), ("US", "أمريكا"), ("FR", "فرنسا"), ("DE", "ألمانيا")
    };

    /// <summary>أنواع شائعة (tag في Radio Browser).</summary>
    public static readonly (string Tag, string Name)[] Genres =
    {
        ("", "كل الأنواع"), ("quran", "قرآن"), ("islamic", "إسلامي"), ("news", "أخبار"), ("talk", "برامج"),
        ("music", "موسيقى"), ("arabic", "عربي"), ("pop", "بوب"), ("classical", "كلاسيك"), ("jazz", "جاز"), ("sports", "رياضة")
    };

    private static async Task<string> GetAsync(string pathAndQuery, CancellationToken ct)
    {
        var order = _working != null ? new[] { _working }.Concat(Mirrors.Where(m => m != _working)) : Mirrors;
        Exception? last = null;
        foreach (string m in order)
        {
            try
            {
                string json = await Http.GetStringAsync(m + pathAndQuery, ct);
                _working = m;
                return json;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidOperationException("مقدرتش أوصل لدليل الراديو. اتأكد إن النت شغال.", last);
    }

    public static async Task<List<RadioStation>> SearchAsync(string? name, string? countryCode, string? tag, int limit, CancellationToken ct)
    {
        var q = new List<string> { "order=votes", "reverse=true", "hidebroken=true", "limit=" + limit };
        if (!string.IsNullOrWhiteSpace(name)) q.Add("name=" + Uri.EscapeDataString(name.Trim()));
        if (!string.IsNullOrWhiteSpace(countryCode)) q.Add("countrycode=" + countryCode);
        if (!string.IsNullOrWhiteSpace(tag)) q.Add("tag=" + Uri.EscapeDataString(tag));
        return Parse(await GetAsync("/json/stations/search?" + string.Join("&", q), ct));
    }

    /// <summary>تحليل الرد (public عشان يتجرّب لوحده).</summary>
    public static List<RadioStation> Parse(string json)
    {
        var list = new List<RadioStation>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string uuid = Str(e, "stationuuid");
            string url = Str(e, "url_resolved");
            if (url.Length == 0) url = Str(e, "url");
            string name = Str(e, "name").Trim();
            if (uuid.Length == 0 || url.Length == 0 || name.Length == 0) continue;
            if (!seen.Add(name + "|" + url)) continue; // نفس المحطة متكررة
            list.Add(new RadioStation(uuid, name, url, Str(e, "country"), Str(e, "countrycode"), Str(e, "language"),
                Str(e, "tags"), Str(e, "codec"), Int(e, "bitrate"), Int(e, "votes"), Str(e, "favicon"), Str(e, "homepage")));
        }
        return list;
    }

    /// <summary>بيعدّ «سماع» للمحطة في الدليل (ده المطلوب من البرامج اللي بتستخدمه). لو فشل مش مهم.</summary>
    public static async Task ClickAsync(string uuid)
    {
        try { await GetAsync("/json/url/" + Uri.EscapeDataString(uuid), CancellationToken.None); } catch { }
    }

    private static string Str(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;
}

/// <summary>
/// قنوات جاهزة: بث رسمي من القنوات نفسها على يوتيوب (مش منقول).
/// رابط «/live» بتاع القناة بيجيب البث الشغال دلوقتي، فمش بيتأثر لما القناة تبدأ بث جديد.
/// </summary>
public static class LivePresets
{
    public static IReadOnlyList<LiveChannel> Tv { get; } = new[]
    {
        new LiveChannel
        {
            Id = "preset:makkah", Name = "مكة المكرمة — المسجد الحرام (قناة القرآن الكريم)",
            Url = "https://www.youtube.com/channel/UCos52azQNBgW63_9uDJoPDA/live", Kind = LiveKind.Tv,
            Group = "بث رسمي — هيئة الإذاعة والتلفزيون السعودية", Tags = "مكة، الحرم، صلاة، قرآن"
        },
        new LiveChannel
        {
            Id = "preset:madinah", Name = "المدينة المنورة — المسجد النبوي (قناة السنة النبوية)",
            Url = "https://www.youtube.com/watch?v=AsSEENjHds0", Kind = LiveKind.Tv,
            Group = "بث رسمي — هيئة الإذاعة والتلفزيون السعودية", Tags = "المدينة، المسجد النبوي، صلاة"
        }
    };

    /// <summary>نسخة جديدة (عشان التعديل عليها مايغيّرش القايمة الثابتة).</summary>
    public static LiveChannel Copy(LiveChannel p) => new()
    {
        Id = p.Id, Name = p.Name, Url = p.Url, Kind = p.Kind, Group = p.Group, Tags = p.Tags, Logo = p.Logo
    };
}

/// <summary>ملفات قوايم القنوات (M3U / M3U8 بتاعة IPTV).</summary>
public static class M3u
{
    private static readonly Regex Attr = new("([\\w-]+)=\"([^\"]*)\"", RegexOptions.Compiled);

    public static List<LiveChannel> Parse(string text)
    {
        var list = new List<LiveChannel>();
        string? name = null, logo = null, group = null;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                logo = group = null;
                foreach (Match m in Attr.Matches(line))
                {
                    string key = m.Groups[1].Value.ToLowerInvariant();
                    if (key == "tvg-logo") logo = m.Groups[2].Value;
                    else if (key == "group-title") group = m.Groups[2].Value;
                }
                int comma = NameComma(line);
                name = comma >= 0 ? line[(comma + 1)..].Trim() : null;
                continue;
            }
            if (line.StartsWith('#')) continue;
            if (!line.Contains("://", StringComparison.Ordinal)) { name = null; continue; }
            list.Add(new LiveChannel
            {
                Name = string.IsNullOrWhiteSpace(name) ? line : name!,
                Url = line, Kind = LiveKind.Tv, Group = string.IsNullOrWhiteSpace(group) ? null : group,
                Logo = string.IsNullOrWhiteSpace(logo) ? null : logo, InfoUpdatedUtc = DateTime.UtcNow
            });
            name = logo = group = null;
        }
        return list;
    }

    /// <summary>الاسم بيبدأ بعد أول فاصلة بعد آخر خاصية (tvg-…="…")، والاسم نفسه ممكن يكون فيه فواصل.</summary>
    private static int NameComma(string s)
    {
        int lastQuote = s.LastIndexOf('"');
        int start = lastQuote >= 0 ? lastQuote : Math.Max(0, s.IndexOf(':'));
        return s.IndexOf(',', start);
    }

    /// <summary>
    /// الرابط ده ملف بث يشغّله VLC على طول (m3u8، ts، mp4، rtmp…)، ولا صفحة محتاجة yt-dlp (زي يوتيوب لايف)؟
    /// </summary>
    /// <summary>مواقع فيديو (البث فيها صفحة، لا رابط بث).</summary>
    public static bool IsVideoSite(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        string host = u.Host.ToLowerInvariant();
        return host.Contains("youtube.com") || host.Contains("youtu.be") || host.Contains("facebook.com") ||
               host.Contains("twitch.tv") || host.Contains("dailymotion.com") || host.Contains("kick.com");
    }

    public static bool IsDirectStream(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return true;
        if (u.Scheme is "rtmp" or "rtmps" or "rtsp" or "udp" or "rtp" or "mms" or "mmsh") return true;
        string host = u.Host.ToLowerInvariant();
        if (host.Contains("youtube.com") || host.Contains("youtu.be") || host.Contains("facebook.com") ||
            host.Contains("twitch.tv") || host.Contains("dailymotion.com") || host.Contains("kick.com")) return false;
        string ext = Path.GetExtension(u.AbsolutePath).ToLowerInvariant();
        return ext is ".m3u8" or ".m3u" or ".mpd" or ".ts" or ".mp4" or ".mp3" or ".aac" or ".ogg" or ".flv" or ".pls" or ""
               || u.AbsolutePath.Contains("/live", StringComparison.OrdinalIgnoreCase);
    }
}
