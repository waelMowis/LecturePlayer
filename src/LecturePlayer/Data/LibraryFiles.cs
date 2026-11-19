using System.Text;
using System.Text.Json;

namespace LecturePlayer.Data;

/// <summary>
/// كل اللي يخص «ملف المكتبة» كملف: اسمها اللي بيظهر، مكانها بكلام مفهوم (الجهاز / Google Drive…)،
/// والملفات التابعة ليها (سجل المذاكرة، النصوص، الملخصات) لما المكتبة تتحفظ في مكان تاني.
/// </summary>
public static class LibraryFiles
{
    private static readonly Dictionary<string, (DateTime Stamp, string Name)> NameCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// اسم المكتبة المكتوب جوه الملف (من غير ما نقرا الملف كله). لو مقدرناش ← اسم الملف.
    /// </summary>
    public static string PeekName(string path)
    {
        string fallback = Path.GetFileNameWithoutExtension(path);
        try
        {
            if (!File.Exists(path)) return fallback;
            var stamp = File.GetLastWriteTimeUtc(path);
            lock (NameCache)
                if (NameCache.TryGetValue(path, out var hit) && hit.Stamp == stamp) return hit.Name;

            // «name» من أول خصائص الملف: نقرا أول 64KB بس
            byte[] buffer = new byte[64 * 1024];
            int read;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                read = fs.Read(buffer, 0, buffer.Length);
            var span = new ReadOnlySpan<byte>(buffer, 0, read);
            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF) span = span[3..];
            var reader = new Utf8JsonReader(span, isFinalBlock: false, state: default);
            string? name = null;
            int depth = 0;
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) { depth++; continue; }
                if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) { depth--; continue; }
                if (depth == 1 && reader.TokenType == JsonTokenType.PropertyName &&
                    string.Equals(reader.GetString(), "name", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.Read() && reader.TokenType == JsonTokenType.String) name = reader.GetString();
                    break;
                }
            }
            name = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
            lock (NameCache) NameCache[path] = (stamp, name);
            return name;
        }
        catch { return fallback; }
    }

    /// <summary>بعد ما اسم مكتبة يتغيّر أو تتحفظ.</summary>
    public static void ForgetName(string path) { lock (NameCache) NameCache.Remove(path); }

    public static void ClearNames() { lock (NameCache) NameCache.Clear(); }

    /// <summary>مكان المكتبة بكلام مفهوم: «على الجهاز» أو «Google Drive» أو «OneDrive» أو «Dropbox» أو الفولدر نفسه.</summary>
    public static string LocationLabel(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full) ?? "";
            if (dir.StartsWith(Path.GetFullPath(Storage.AppDataDir), StringComparison.OrdinalIgnoreCase)) return "على الجهاز";
            string low = full.ToLowerInvariant();
            if (low.Contains("my drive") || low.Contains("google drive") || low.Contains(".shortcut-targets-by-id") || low.Contains("shared drives"))
                return "Google Drive";
            if (low.Contains("onedrive")) return "OneDrive";
            if (low.Contains("dropbox")) return "Dropbox";
            return dir;
        }
        catch { return Path.GetDirectoryName(path) ?? path; }
    }

    /// <summary>
    /// المكتبة اتحفظت في مكان/اسم جديد: ننسخ الملفات التابعة ليها (سجل المذاكرة من كل الأجهزة، والنصوص والملخصات
    /// اللي جنبها) عشان الإحصائيات والنصوص متضيعش. مش بنكتب فوق ملف موجود بالفعل في المكان الجديد.
    /// </summary>
    public static int CopyCompanions(string oldLibrary, string newLibrary, IEnumerable<VideoItem> videos)
    {
        int copied = 0;
        string oldDir = Path.GetDirectoryName(Path.GetFullPath(oldLibrary)) ?? ".";
        string newDir = Path.GetDirectoryName(Path.GetFullPath(newLibrary)) ?? ".";
        string oldBase = Path.GetFileNameWithoutExtension(oldLibrary), newBase = Path.GetFileNameWithoutExtension(newLibrary);

        void Copy(string from, string to)
        {
            try
            {
                if (!File.Exists(from) || File.Exists(to)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to);
                copied++;
            }
            catch (Exception ex) { Log.Error("copy companion " + from, ex); }
        }

        // سجل المذاكرة: <الاسم>.sessions.<الجهاز>.jsonl
        try
        {
            if (Directory.Exists(oldDir))
                foreach (string f in Directory.EnumerateFiles(oldDir, oldBase + ".sessions.*.jsonl"))
                    Copy(f, Path.Combine(newDir, newBase + Path.GetFileName(f)[oldBase.Length..]));
        }
        catch (Exception ex) { Log.Error("copy journal", ex); }

        // النصوص والملخصات: فولدر transcripts جنب المكتبة (بس لو الفولدر اتغيّر، وبس فيديوهات المكتبة دي)
        string oldT = TranscriptStore.Dir(oldLibrary), newT = TranscriptStore.Dir(newLibrary);
        if (!string.Equals(Path.GetFullPath(oldT), Path.GetFullPath(newT), StringComparison.OrdinalIgnoreCase) && Directory.Exists(oldT))
        {
            var keys = new HashSet<string>(videos.Select(TranscriptStore.Key), StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string f in Directory.EnumerateFiles(oldT))
                {
                    string name = Path.GetFileName(f);
                    int dot = name.IndexOf('.');
                    if (dot > 0 && keys.Contains(name[..dot])) Copy(f, Path.Combine(newT, name));
                }
            }
            catch (Exception ex) { Log.Error("copy transcripts", ex); }
        }
        return copied;
    }
}
