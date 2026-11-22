using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>
/// تحديث محاضرة جاية من قائمة تشغيل يوتيوب: بنقرا القائمة تاني، وأي فيديو مش عندنا
/// بيتضاف في مكانه حسب ترتيب القائمة ويتعلّم «جديد». مفيش حاجة بتتمسح:
/// الفيديو اللي اتشال من القائمة بيفضل عندك (بتقدمه وملاحظاته)، وبنعدّه بس.
/// </summary>
public static class PlaylistSync
{
    public sealed record Result(List<VideoItem> Added, int MissingFromSource);

    private static readonly Regex VideoIdRx = new(@"(?:[?&]v=|youtu\.be/|/shorts/|/embed/|/live/)([A-Za-z0-9_-]{11})", RegexOptions.Compiled);

    /// <summary>مفتاح المقارنة: ID فيديو يوتيوب لو موجود، وإلا الرابط نفسه.</summary>
    public static string Key(string url)
    {
        var m = VideoIdRx.Match(url);
        return m.Success ? "yt:" + m.Groups[1].Value : url.Trim().ToLowerInvariant();
    }

    /// <summary>الرابط فيه قائمة تشغيل (list=)؟</summary>
    public static bool IsPlaylistUrl(string url) =>
        url.Contains("list=", StringComparison.OrdinalIgnoreCase) || url.Contains("/playlist", StringComparison.OrdinalIgnoreCase);

    public static Result Apply(LectureGroup g, OnlineListing listing, DateTime nowUtc)
    {
        var have = new Dictionary<string, VideoItem>(StringComparer.Ordinal);
        foreach (var v in g.Videos) have.TryAdd(Key(v.Path), v);

        var added = new List<VideoItem>();
        VideoItem? previous = null; // آخر فيديو من القائمة لقيناه عندنا ← الجديد بيتحط بعده
        foreach (var e in listing.Entries)
        {
            string k = Key(e.Url);
            if (have.TryGetValue(k, out var existing))
            {
                previous = existing;
                continue;
            }
            var video = VideoItem.FromUrl(e);
            video.IsNew = true;
            video.UpdatedUtc = nowUtc;
            int at = previous != null ? g.Videos.IndexOf(previous) + 1 : (added.Count == 0 ? FirstOnlineIndex(g) : g.Videos.IndexOf(added[^1]) + 1);
            g.Videos.Insert(Math.Clamp(at, 0, g.Videos.Count), video);
            have[k] = video;
            previous = video;
            added.Add(video);
        }

        var source = new HashSet<string>(listing.Entries.Select(e => Key(e.Url)), StringComparer.Ordinal);
        int missing = g.Videos.Count(v => v.IsOnline && !source.Contains(Key(v.Path)));
        g.LastSyncedUtc = nowUtc;
        g.InfoUpdatedUtc = nowUtc;
        return new Result(added, missing);
    }

    private static int FirstOnlineIndex(LectureGroup g)
    {
        int i = g.Videos.FindIndex(v => v.IsOnline);
        return i < 0 ? g.Videos.Count : i;
    }

    /// <summary>المحاضرات اللي محتاجة تحديث (عدّى عليها أكتر من المدة).</summary>
    public static List<LectureGroup> Due(CourseLibrary lib, DateTime nowUtc, TimeSpan every) =>
        lib.AllGroups.Where(g => g.IsPlaylist && (g.LastSyncedUtc == null || nowUtc - g.LastSyncedUtc >= every)).ToList();
}
