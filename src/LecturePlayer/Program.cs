
namespace LecturePlayer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // وضع التركيز: البرنامج بيشغّل نفسه كمسؤول (UAC) عشان يعدّل ملف hosts ويقفل على طول
        if (args.Length > 0 && args[0] is "--focus-block" or "--focus-unblock")
            return LecturePlayer.Data.FocusBlocker.Apply(args[0] == "--focus-block", args.Skip(1));

        ApplicationConfiguration.Initialize();

        // فحص تشغيل آلي (GitHub Actions): يفتح الشباك فعلًا ويجرّب الشاشات الجديدة ويقفل.
        // أي Exception = كود خروج 1، فالإصدار مايتنشرش لو البرنامج بيقع وهو بيفتح.
        if (args.Contains("--smoke-test")) return SmokeTest.Run();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LecturePlayer.Data.CrashReport.Record("unhandled", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), fatal: true);
        Application.ThreadException += (_, e) =>
        {
            LecturePlayer.Data.CrashReport.Record("ui", e.Exception, fatal: false);
            MessageBox.Show("حصل خطأ مش متوقع، بس بياناتك ومكتبتك سليمة وتقدر تكمّل عادي.\n\n" + e.Exception.Message +
                            "\n\nاتسجل تقرير بالتفاصيل، وهيتعرض عليك المرة الجاية اللي تفتح فيها البرنامج عشان تبعته.",
                "«مسار»", MessageBoxButtons.OK, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        };

        // ملف قائمة (.lpl) أو فيديوهات اتفتحت بـ «فتح بواسطة»
        string? libraryFile = args.FirstOrDefault(a => File.Exists(a) &&
            string.Equals(Path.GetExtension(a), LecturePlayer.Data.Storage.LibraryExtension, StringComparison.OrdinalIgnoreCase));
        var mediaFiles = args.Where(a => File.Exists(a) && LecturePlayer.Data.MediaFiles.IsMedia(a))
            .Select(Path.GetFullPath).ToList();

        using var instance = new LecturePlayer.Data.SingleInstance();
        // البرنامج شغال أصلًا: ابعت الفيديوهات للشباك المفتوح بدل ما نفتح شباك تاني على نفس المكتبة
        if (!instance.IsFirst && mediaFiles.Count > 0 && LecturePlayer.Data.SingleInstance.Send(mediaFiles))
            return 0;

        MainForm form;
        try
        {
            // تحميل مكتبة VLC الأصلية (libvlc) اللي جاية مع باكدج VideoLAN.LibVLC.Windows
            LibVLCSharp.Shared.Core.Initialize();
            form = new MainForm(libraryFile, mediaFiles);
        }
        catch (Exception ex)
        {
            // البرنامج مقدرش يفتح خالص: بدل ما يختفي من غير ولا كلمة، نقول للمستخدم ونسيبله تقرير
            LecturePlayer.Data.CrashReport.Record("startup", ex, fatal: true);
            var answer = MessageBox.Show(
                "«مسار» مقدرش يفتح بسبب عطل:\n\n" + ex.Message +
                "\n\nبياناتك ومكتباتك سليمة ومحدش لمسها. التحديث الجاي غالبًا هيصلّح ده.\n\nتحب تفتح فولدر السجل عشان تبعت التفاصيل؟",
                "«مسار»", MessageBoxButtons.YesNo, MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            if (answer == DialogResult.Yes)
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LecturePlayer.Data.Log.LogDir) { UseShellExecute = true }); } catch { }
            return 1;
        }
        form.Shown += (_, _) => instance.Listen(paths =>
        {
            try { form.BeginInvoke(() => form.OpenExternalFiles(paths)); } catch { /* الشباك بيقفل */ }
        });
        Application.Run(form);
        return 0;
    }
}
