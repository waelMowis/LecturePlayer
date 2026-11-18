using System.Collections.Concurrent;

namespace LecturePlayer.Data;

/// <summary>
/// «الملف موجود؟» بذاكرة قصيرة. على Google Drive / OneDrive كل سؤال للديسك ممكن ياخد وقت،
/// والشجرة والحفظ كانوا بيسألوا عن كل فيديو في كل مرة (آلاف المرات على الواجهة).
/// الموجود بيتفتكر 5 دقايق، والمش موجود 20 ثانية (عشان لو الدرايف لسه بينزّل يبان بسرعة).
/// </summary>
public static class FileCache
{
    private static readonly ConcurrentDictionary<string, (bool Exists, long Ticks)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly long FoundTtl = TimeSpan.FromMinutes(5).Ticks, MissingTtl = TimeSpan.FromSeconds(20).Ticks;

    public static bool Exists(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        long now = DateTime.UtcNow.Ticks;
        if (Cache.TryGetValue(path, out var hit) && now - hit.Ticks < (hit.Exists ? FoundTtl : MissingTtl)) return hit.Exists;
        bool exists;
        try { exists = File.Exists(path); } catch { exists = false; }
        Cache[path] = (exists, now);
        return exists;
    }

    /// <summary>بعد ما ملف اتنقل أو اتحدد مكانه الجديد.</summary>
    public static void Forget(string? path) { if (!string.IsNullOrEmpty(path)) Cache.TryRemove(path, out _); }

    public static void Clear() => Cache.Clear();
}
