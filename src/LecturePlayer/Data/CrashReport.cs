using System.Text;

namespace LecturePlayer.Data;

/// <summary>
/// لو البرنامج وقع: بنكتب ملخص في Data\last-crash.txt، والمرة الجاية اللي يفتح فيها بيقول للمستخدم
/// وبيجهّزله تقرير يقدر ينسخه ويبعته. مفيش أي حاجة بتتبعت لوحدها (خصوصية).
/// </summary>
public static class CrashReport
{
    public static string MarkerPath => Path.Combine(Storage.AppDataDir, "last-crash.txt");

    public static string Version =>
        typeof(CrashReport).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion?.Split('+')[0]
        ?? typeof(CrashReport).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>بيتسجل لما البرنامج يقع (أو خطأ مش متوقع في الواجهة).</summary>
    public static void Record(string where, Exception ex, bool fatal)
    {
        Log.Error(where, ex);
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"الوقت: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"الإصدار: {Version}");
            sb.AppendLine($"ويندوز: {Environment.OSVersion.VersionString}  ({(Environment.Is64BitProcess ? "64" : "32")}-bit)");
            sb.AppendLine($"المكان: {where}{(fatal ? " (أُغلق البرنامج)" : "")}");
            sb.AppendLine();
            sb.AppendLine(ex.ToString());
            Directory.CreateDirectory(Storage.AppDataDir);
            File.WriteAllText(MarkerPath, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    /// <summary>ملخص آخر وقعة (لو فيه) — وبيمسح العلامة عشان مايتعرضش تاني.</summary>
    public static string? TakeLast()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return null;
            string text = File.ReadAllText(MarkerPath, Encoding.UTF8);
            File.Delete(MarkerPath);
            return text;
        }
        catch { return null; }
    }

    /// <summary>تقرير كامل للنسخ: ملخص الوقعة + آخر سطور السجل (من غير ما نغيّر حاجة).</summary>
    public static string BuildReport(string crash)
    {
        var sb = new StringBuilder();
        sb.AppendLine("تقرير عطل «مسار»");
        sb.AppendLine("================");
        sb.AppendLine(crash.Trim());
        sb.AppendLine();
        sb.AppendLine("آخر سطور السجل:");
        sb.AppendLine("---------------");
        try
        {
            var file = Directory.Exists(Log.LogDir)
                ? Directory.EnumerateFiles(Log.LogDir, "*.log").OrderByDescending(f => f).FirstOrDefault()
                : null;
            if (file != null)
            {
                string[] lines;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                    lines = sr.ReadToEnd().Split('\n');
                foreach (var l in lines.TakeLast(80)) sb.AppendLine(l.TrimEnd('\r'));
            }
        }
        catch { }
        return sb.ToString();
    }
}
