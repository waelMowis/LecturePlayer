using System.Diagnostics;
using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>
/// دليل الاستخدام: صفحة HTML جوه البرنامج نفسه (Help/Guide.html)، بتتكتب في فولدر البيانات
/// وتتفتح في المتصفح (عربي كامل، فهرس، وبحث). بتتحدّث مع كل إصدار لوحدها.
/// </summary>
internal static class HelpGuide
{
    /// <param name="section">جزء معيّن: intro, quick, ui, library, playback, online, study, ai, sync, settings, updates, keys, faq, about</param>
    public static void Open(IWin32Window owner, string? section = null)
    {
        try
        {
            string dir = Path.Combine(Storage.AppDataDir, "help");
            Directory.CreateDirectory(dir);
            string guide = Path.Combine(dir, "guide.html");

            string html;
            using (var s = typeof(HelpGuide).Assembly.GetManifestResourceStream("Guide.html")
                           ?? throw new FileNotFoundException("الدليل غير موجود داخل البرنامج"))
            using (var r = new StreamReader(s))
                html = r.ReadToEnd();
            html = html.Replace("{{VERSION}}", UpdateChecker.CurrentVersion.ToString(3))
                       .Replace("{{YEAR}}", DateTime.Now.Year.ToString());
            File.WriteAllText(guide, html, new System.Text.UTF8Encoding(false));

            // الويندوز بيشيل «#جزء» من مسار الملف، فبنفتح صفحة صغيرة بتحوّل للجزء المطلوب
            string target = guide;
            if (!string.IsNullOrWhiteSpace(section))
            {
                target = Path.Combine(dir, "open.html");
                File.WriteAllText(target,
                    $"<!DOCTYPE html><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"0;url=guide.html#{section}\">",
                    new System.Text.UTF8Encoding(false));
            }
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("help-guide", ex);
            Dialogs.Error(owner, "تعذّر فتح دليل الاستخدام:\n" + ex.Message);
        }
    }
}
