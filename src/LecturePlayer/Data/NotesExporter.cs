using System.Globalization;
using System.Text;

namespace LecturePlayer.Data;

/// <summary>تصدير ملاحظات وعلامات كورس كامل لملف Markdown مرتب بالمحاضرات.</summary>
public static class NotesExporter
{
    public static string BuildMarkdown(Course course, bool includeEmptyVideos = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {course.Name}");
        sb.AppendLine();
        int percent = course.VideoCount == 0 ? 0 : (int)Math.Round(course.WatchedCount * 100.0 / course.VideoCount);
        sb.AppendLine($"> {course.WatchedCount} من {course.VideoCount} فيديو اتشافوا ({percent}%) · اتصدّر من «مسار» يوم {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine();

        int written = 0;
        foreach (var g in course.Groups)
        {
            var videos = g.Videos.Where(v => includeEmptyVideos || HasContent(v)).ToList();
            if (videos.Count == 0) continue;

            sb.AppendLine($"## {g.Name}");
            sb.AppendLine();
            foreach (var v in videos)
            {
                sb.AppendLine($"### {v.Title}{(v.Watched ? " ✓" : "")}");
                sb.AppendLine();

                foreach (var b in v.Bookmarks.OrderBy(b => b.TimeMs))
                {
                    string time = TimeFormat.Format(b.TimeMs);
                    string stamp = v.IsOnline ? $"[{time}]({LinkAt(v.Path, b.TimeMs)})" : $"**{time}**";
                    sb.AppendLine(string.IsNullOrWhiteSpace(b.Note) ? $"- {stamp}" : $"- {stamp} — {b.Note.Trim()}");
                }
                if (v.Bookmarks.Count > 0) sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(v.Notes))
                {
                    sb.AppendLine(v.Notes.Trim());
                    sb.AppendLine();
                }
                written++;
            }
        }

        if (written == 0)
        {
            sb.AppendLine("_لسه مفيش ملاحظات أو علامات في الكورس ده._");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static bool HasContent(VideoItem v) => v.Bookmarks.Count > 0 || !string.IsNullOrWhiteSpace(v.Notes);

    /// <summary>رابط يوتيوب يفتح على الثانية دي (t=123s).</summary>
    public static string LinkAt(string url, long ms)
    {
        long seconds = Math.Max(0, ms / 1000);
        string sep = url.Contains('?') ? "&" : "?";
        return $"{url}{sep}t={seconds.ToString(CultureInfo.InvariantCulture)}s";
    }

    /// <summary>اسم ملف آمن من اسم فيديو أو كورس.</summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|', '/', '\\' }).ToHashSet();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (cleaned.Length > 120) cleaned = cleaned[..120];
        return cleaned.Length == 0 ? "file" : cleaned;
    }
}
