namespace LecturePlayer;

/// <summary>
/// فحص إن البرنامج بيفتح من غير ما يقع: بيبني الشباك الرئيسي، ويعرضه، ويجرّب الوضع البسيط والمتقدم،
/// وشريط التنبيهات، ومنيو المذاكرة، ومبدّل المكتبات، والجولة — وبعدين يقفل.
/// بيتشغّل في GitHub Actions قبل النشر: LecturePlayer.exe --smoke-test
/// </summary>
internal static class SmokeTest
{
    public static bool Active { get; private set; }

    public static int Run()
    {
        Active = true;
        Exception? failure = null;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => { failure ??= e.Exception; Application.ExitThread(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine("SMOKE FAIL (unhandled): " + e.ExceptionObject);
            Environment.Exit(1);
        };

        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            using var form = new MainForm(null, Array.Empty<string>());
            form.Shown += (_, _) =>
            {
                var t = new System.Windows.Forms.Timer { Interval = 1500 };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    try { form.RunSmokeChecks(); }
                    catch (Exception ex) { failure ??= ex; }
                    form.Close();
                };
                t.Start();
            };
            Application.Run(form);
        }
        catch (Exception ex) { failure ??= ex; }

        if (failure != null)
        {
            Console.Error.WriteLine("SMOKE FAIL: " + failure);
            return 1;
        }
        Console.WriteLine("SMOKE OK");
        return 0;
    }
}
