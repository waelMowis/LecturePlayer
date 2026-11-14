using System.Diagnostics;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// التحديث التلقائي من GitHub Releases:
/// البرنامج يشوف آخر إصدار ← يسأل المستخدم ← ينزّل المثبّت ويتأكد منه ← يقفل ← المثبّت يحدّث بهدوء ← البرنامج يفتح تاني.
/// </summary>
internal sealed partial class MainForm
{
    private UpdateChecker.Release? _availableUpdate;
    private bool _updating;
    private string? _pendingInstaller;

    /// <summary>المستودع: اللي في الإعدادات، وإلا اللي متكتب جوه البرنامج وقت البناء.</summary>
    private string? UpdateRepo =>
        !string.IsNullOrWhiteSpace(_settings.UpdateRepo) ? _settings.UpdateRepo.Trim().Trim('/') : UpdateChecker.BuiltInRepo;

    /// <summary>بعد ما الشباك يفتح: «اتحدّث ✓» لو ده أول تشغيل بعد تحديث، وبعد ثواني نتحقق من الجديد.</summary>
    private async void AfterStartupUpdateCheck(bool firstRunTour = false)
    {
        string current = UpdateChecker.CurrentVersion.ToString(3);
        if (string.IsNullOrEmpty(_settings.LastRunVersion))
        {
            if (!firstRunTour)
                ShowBanner("أهلًا بيك في «مسار» 👋  ابدأ بإنك تضيف فولدر فيديوهات أو رابط يوتيوب",
                    BannerKind.Info, "جولة سريعة", StartTour, seconds: 20, key: "welcome");
        }
        else if (_settings.LastRunVersion != current)
            ShowBanner($"اتحدّث «مسار» لـ {current} ✓", BannerKind.Success, "إيه الجديد؟", OpenReleaseNotes, seconds: 12, key: "updated");
        if (_settings.LastRunVersion != current) { _settings.LastRunVersion = current; SaveSettings(); }

        if (!_settings.CheckUpdates || UpdateRepo == null) return;
        await Task.Delay(TimeSpan.FromSeconds(6)); // مانبطّأش فتح البرنامج
        if (!_closing) await CheckForUpdatesAsync(manual: false);
    }

    /// <summary>صفحة الإصدار الحالي على GitHub (فيها اللي اتغيّر).</summary>
    private void OpenReleaseNotes()
    {
        string? repo = UpdateRepo;
        if (repo == null) { HelpGuide.Open(this); return; }
        OpenInBrowser($"https://github.com/{repo}/releases/tag/v{UpdateChecker.CurrentVersion.ToString(3)}");
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        string? repo = UpdateRepo;
        if (repo == null)
        {
            if (manual) SetUpdateRepoDialog();
            return;
        }
        if (_updating) { ShowStatus("التحديث شغال بالفعل…"); return; }

        try
        {
            if (manual) ShowStatus("بشوف فيه تحديث ولا لأ…", 20);
            var latest = await UpdateChecker.GetLatestAsync(repo, CancellationToken.None);
            if (_closing) return;
            if (latest == null || !UpdateChecker.IsNewer(latest.Version, UpdateChecker.CurrentVersion))
            {
                if (manual) { ShowStatus(""); Dialogs.Info(this, $"إنت على آخر إصدار ({UpdateChecker.CurrentVersion.ToString(3)}) ✓", "التحديثات"); }
                return;
            }

            _availableUpdate = latest;
            // الإصدار اللي المستخدم قال «تخطّاه» مايتعرضش لوحده تاني (لكن يتعرض لو طلب التحقق بنفسه)
            if (!manual && string.Equals(_settings.SkippedUpdate, latest.Tag, StringComparison.OrdinalIgnoreCase))
            {
                ShowBanner($"فيه إصدار جديد من «مسار» ({latest.Tag})", BannerKind.Info, "حدّث دلوقتي",
                    () => _ = CheckForUpdatesAsync(manual: true), seconds: 15, key: "update");
                return;
            }
            // لو بيتفرج دلوقتي مانقاطعهوش: نقول في الشريط بس
            if (!manual && _mp.IsPlaying)
            {
                ShowBanner($"فيه إصدار جديد من «مسار» ({latest.Tag}) — مش هقاطعك وإنت بتتفرج", BannerKind.Info, "حدّث دلوقتي",
                    () => _ = CheckForUpdatesAsync(manual: true), seconds: 0, key: "update");
                return;
            }

            switch (ShowUpdatePrompt(latest))
            {
                case DialogResult.OK:
                    await DownloadAndInstallAsync(latest, repo);
                    break;
                case DialogResult.Ignore:
                    _settings.SkippedUpdate = latest.Tag;
                    SaveSettings();
                    ShowBanner($"مش هفكّرك بـ {latest.Tag} تاني. تقدر تحدّث وقت ما تحب من: مساعدة ← التحقق من التحديثات", BannerKind.Info, seconds: 8);
                    break;
                default:
                    ShowBanner("التحديث متاح وقت ما تحب", BannerKind.Info, "حدّث دلوقتي", () => _ = CheckForUpdatesAsync(manual: true), seconds: 10, key: "update");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("update-check", ex);
            if (manual) { ShowStatus(""); Dialogs.Error(this, "مقدرتش أتحقق من التحديثات:\n" + ex.Message + "\n\nاتأكد إن النت شغال."); }
        }
    }

    /// <summary>OK = حدّث دلوقتي، Ignore = تخطّى الإصدار ده، Cancel = بعدين.</summary>
    private DialogResult ShowUpdatePrompt(UpdateChecker.Release r)
    {
        using var kit = new FormKit("تحديث جديد", 540);
        kit.Header($"«مسار» {r.Version.ToString(3)} متاح");
        kit.Note($"إنت على {UpdateChecker.CurrentVersion.ToString(3)}. التحديث بيتنزّل ويتثبت لوحده، والبرنامج بيفتح تاني بعده.\n" +
                 "مكتبتك وإعداداتك وتقدّمك مش بيتلمسوا.");
        if (!string.IsNullOrWhiteSpace(r.Notes))
        {
            kit.Header("الجديد في الإصدار ده");
            var notes = kit.Full(new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                Text = Truncate(r.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n"), 4000),
                RightToLeft = RightToLeft.Yes
            }, 170);
            notes.SelectionStart = 0;
        }
        if (r.SetupUrl == null)
            kit.Note("الإصدار ده مالوش مثبّت على GitHub، فهيتفتح صفحة التحميل في المتصفح.", color: Theme.Folder);
        else if (r.SetupSize > 0)
            kit.Note($"حجم التحديث: {r.SetupSize / 1024.0 / 1024.0:0} ميجا");

        kit.Buttons("حدّث دلوقتي", "بعدين", ("تخطّى الإصدار ده", () => kit.Form.DialogResult = DialogResult.Ignore));
        return kit.Show(this);
    }

    private async Task DownloadAndInstallAsync(UpdateChecker.Release r, string repo)
    {
        if (r.SetupUrl == null) { OpenInBrowser(r.Url); return; }
        if (_updating) return;
        _updating = true;
        try
        {
            var progress = new Progress<int>(p => ShowStatus($"بنزّل التحديث {r.Tag}… {p}%", 60));
            string file = await UpdateChecker.DownloadSetupAsync(r, repo, progress, CancellationToken.None);
            Log.Info($"update downloaded: {r.Tag} -> {file}");
            if (_closing) return;
            ShowStatus("التحديث اتنزّل ✓", 5);

            if (!Dialogs.Confirm(this,
                    $"التحديث {r.Version.ToString(3)} جاهز.\n\n" +
                    "البرنامج هيقفل (بعد ما يحفظ كل حاجة)، والتحديث هيتثبت، وبعدين يفتح تاني لوحده.\n\nنكمّل؟",
                    "تحديث"))
            {
                ShowBanner("التحديث متنزّل وجاهز يتثبت لما توافق", BannerKind.Info, "ثبّته", () => _ = CheckForUpdatesAsync(manual: true), seconds: 0, key: "update");
                return;
            }

            _pendingInstaller = file;
            Close(); // القفل العادي بيحفظ المكتبة والإعدادات والمكان، وبعدين OnFormClosed يشغّل المثبّت
        }
        catch (Exception ex)
        {
            Log.Error("update-download", ex);
            ShowStatus("");
            if (Dialogs.Confirm(this, "مقدرتش أنزّل التحديث:\n" + ex.Message + "\n\nتحب تفتح صفحة التحميل في المتصفح؟", "تحديث"))
                OpenInBrowser(r.Url);
        }
        finally { _updating = false; }
    }

    /// <summary>بعد ما البرنامج يقفل: نشغّل المثبّت بهدوء، و/UPDATE=1 بيخلّيه يفتح البرنامج تاني في الآخر.</summary>
    private void LaunchPendingInstaller()
    {
        if (_pendingInstaller == null || !File.Exists(_pendingInstaller)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_pendingInstaller)
            {
                UseShellExecute = true,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1"
            });
            Log.Info("update installer launched: " + _pendingInstaller);
        }
        catch (Exception ex) { Log.Error("update-launch", ex); }
    }

    private void SetUpdateRepoDialog()
    {
        string? repo = Dialogs.Input(this, "مستودع التحديثات",
            "مستودع GitHub بتاع البرنامج (owner/repo):", _settings.UpdateRepo.Length > 0 ? _settings.UpdateRepo : UpdateChecker.BuiltInRepo ?? "");
        if (repo == null) return;
        _settings.UpdateRepo = repo.Trim().Replace("https://github.com/", "", StringComparison.OrdinalIgnoreCase).Trim('/');
        SaveSettings();
        _ = CheckForUpdatesAsync(manual: true);
    }
}
