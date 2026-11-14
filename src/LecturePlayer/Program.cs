
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
            LecturePlayer.Data.Log.Error("unhandled", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        Application.ThreadException += (_, e) =>
        {
            LecturePlayer.Data.Log.Error("ui", e.Exception);
            MessageBox.Show(e.Exception.Message, "حصل خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error,
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

        // تحميل مكتبة VLC الأصلية (libvlc) اللي جاية مع باكدج VideoLAN.LibVLC.Windows
        LibVLCSharp.Shared.Core.Initialize();

        var form = new MainForm(libraryFile, mediaFiles);
        form.Shown += (_, _) => instance.Listen(paths =>
        {
            try { form.BeginInvoke(() => form.OpenExternalFiles(paths)); } catch { /* الشباك بيقفل */ }
        });
        Application.Run(form);
        return 0;
    }
}
