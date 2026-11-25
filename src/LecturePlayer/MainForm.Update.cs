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
        if (SmokeTest.Active) return;
        string current = UpdateChecker.CurrentVersion.ToString(3);
        if (string.IsNullOrEmpty(_settings.LastRunVersion))
        {
            if (!firstRunTour)
                ShowBanner("مرحبًا بك في «مسار» 👋  ابدأ بإضافة مجلد فيديوهات أو رابط يوتيوب",
                    BannerKind.Info, "جولة سريعة", StartTour, seconds: 20, key: "welcome");
        }
        else if (_settings.LastRunVersion != current)
            ShowBanner($"حُدِّث «مسار» إلى {current} ✓", BannerKind.Success, "ما الجديد؟", OpenReleaseNotes, seconds: 12, key: "updated");
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

    private Task? _updateCheck;
    private bool _updateCheckManual;
    internal int UpdateChecksStarted { get; private set; }

    /// <summary>
    /// تحقق واحد في نفس الوقت: لو فيه تحقق شغال، الضغطة التانية بتستناه (كانت بتعمل تحقق جديد ورسالة خطأ لكل ضغطة).
    /// </summary>
    private Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateCheck is { IsCompleted: false })
        {
            if (manual)
            {
                _updateCheckManual = true;
                ShowBanner("ما زلت أتحقق من التحديثات… انتظر ثوانيَ", BannerKind.Info, seconds: 0, key: "update-check");
            }
            return _updateCheck;
        }
        _updateCheckManual = manual;
        return _updateCheck = CheckForUpdatesCoreAsync();
    }

    private async Task CheckForUpdatesCoreAsync()
    {
        string? repo = UpdateRepo;
        if (repo == null)
        {
            if (_updateCheckManual) SetUpdateRepoDialog();
            return;
        }
        if (_updating) { ShowStatus("التحديث يعمل بالفعل…"); return; }
        UpdateChecksStarted++;

        try
        {
            if (_updateCheckManual) ShowBanner("جارٍ البحث عن تحديث…", BannerKind.Info, seconds: 0, key: "update-check");
            var latest = await UpdateChecker.GetLatestAsync(repo, CancellationToken.None);
            bool manual = _updateCheckManual;
            HideBanner("update-check");
            if (_closing) return;
            if (latest == null || !UpdateChecker.IsNewer(latest.Version, UpdateChecker.CurrentVersion))
            {
                if (manual) ShowBanner($"أنت على آخر إصدار ({UpdateChecker.CurrentVersion.ToString(3)}) ✓", BannerKind.Success, seconds: 6, key: "update-check");
                return;
            }

            _availableUpdate = latest;
            // الطريق التاني (github.com) مابيجيبش الملاحظات: نحاول نجيبها هي والبصمة من الواجهة (لو اتأخرت نكمّل من غيرها)
            if (latest.SetupSha256 == null)
                try { if (await UpdateChecker.FromApiAsync(repo.Trim().Trim('/'), CancellationToken.None) is { } full && full.Tag == latest.Tag) latest = _availableUpdate = full; }
                catch (Exception ex) { Log.Info("update notes: " + ex.Message); }
            // الإصدار اللي المستخدم قال «تخطّاه» مايتعرضش لوحده تاني (لكن يتعرض لو طلب التحقق بنفسه)
            if (!manual && string.Equals(_settings.SkippedUpdate, latest.Tag, StringComparison.OrdinalIgnoreCase))
            {
                ShowBanner($"يوجد إصدار جديد من «مسار» ({latest.Tag})", BannerKind.Info, "حدّث الآن",
                    () => _ = CheckForUpdatesAsync(manual: true), seconds: 15, key: "update");
                return;
            }
            // لو بيتفرج دلوقتي مانقاطعهوش: نقول في الشريط بس
            if (!manual && _mp.IsPlaying)
            {
                ShowBanner($"يوجد إصدار جديد من «مسار» ({latest.Tag}) — لن أقاطعك أثناء المشاهدة", BannerKind.Info, "حدّث الآن",
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
                    ShowBanner($"لن أذكّرك بـ {latest.Tag} مرة أخرى. يمكنك التحديث متى شئت من: مساعدة ← التحقق من التحديثات", BannerKind.Info, seconds: 8);
                    break;
                default:
                    ShowBanner("التحديث متاح متى شئت", BannerKind.Info, "حدّث الآن", () => _ = CheckForUpdatesAsync(manual: true), seconds: 10, key: "update");
                    break;
            }
        }
        catch (Exception ex)
        {
            // مفيش رسالة بتوقف الشغل: النت البطيء مش غلطة المستخدم. شريط صغير فيه «حاول تاني» لو هو اللي طلب
            if (ex is UpdateChecker.UpdateCheckException) Log.Info("update-check: " + ex.Message + " / " + ex.InnerException?.Message);
            else Log.Error("update-check", ex);
            HideBanner("update-check");
            if (_updateCheckManual && !_closing)
                ShowBanner(ex is UpdateChecker.UpdateCheckException ? ex.Message : "تعذّر التحقق من التحديثات: " + ex.Message,
                    BannerKind.Warning, "حاول مرة أخرى", () => _ = CheckForUpdatesAsync(manual: true), seconds: 20, key: "update-check");
        }
    }

    /// <summary>OK = حدّث دلوقتي، Ignore = تخطّى الإصدار ده، Cancel = بعدين.</summary>
    private DialogResult ShowUpdatePrompt(UpdateChecker.Release r)
    {
        using var kit = new FormKit("تحديث جديد", 540);
        kit.Header($"«مسار» {r.Version.ToString(3)} متاح");
        kit.Note($"أنت على {UpdateChecker.CurrentVersion.ToString(3)}. يُنزَّل التحديث ويُثبَّت تلقائيًا، ويُفتح البرنامج بعده.\n" +
                 "لا تُمس مكتبتك وإعداداتك وتقدّمك.");
        if (!string.IsNullOrWhiteSpace(r.Notes))
        {
            kit.Header("الجديد في هذا الإصدار");
            var notes = kit.Full(new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                Text = Truncate(PlainNotes(r.Notes).Replace("\n", "\r\n"), 4000),
                RightToLeft = RightToLeft.Yes
            }, 170);
            notes.SelectionStart = 0;
        }
        if (r.SetupUrl == null)
            kit.Note("هذا الإصدار ليس له مثبّت على GitHub، لذا ستُفتح صفحة التنزيل في المتصفح.", color: Theme.Folder);
        else if (r.SetupSize > 0)
            kit.Note($"حجم التحديث: {r.SetupSize / 1024.0 / 1024.0:0} ميجابايت");

        kit.Buttons("حدّث الآن", "لاحقًا", ("تخطَّ هذا الإصدار", () => kit.Form.DialogResult = DialogResult.Ignore));
        return kit.Show(this);
    }

    /// <summary>ملاحظات الإصدار (Markdown) لنص عادي يتقري: عناوين وعلامات • من غير ## ولا **.</summary>
    internal static string PlainNotes(string md)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var raw in md.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            int indent = line.Length - line.TrimStart().Length;
            string t = line.Trim();
            if (t.StartsWith("### ")) t = "▸ " + t[4..];
            else if (t.StartsWith("## ")) { if (sb.Length > 0) sb.Append('\n'); t = "■ " + t[3..]; }
            else if (t.StartsWith("# ")) t = t[2..];
            else if (t.StartsWith("- ") || t.StartsWith("* ")) t = (indent >= 2 ? "      ◦ " : "• ") + t[2..];
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\*\*(.+?)\*\*", "$1");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"`([^`]+)`", "$1");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\[([^\]]+)\]\([^)]+\)", "$1");
            sb.Append(t).Append('\n');
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "\n{3,}", "\n\n").Trim();
    }

    private async Task DownloadAndInstallAsync(UpdateChecker.Release r, string repo)
    {
        if (r.SetupUrl == null) { OpenInBrowser(r.Url); return; }
        if (_updating) return;
        if (r.SetupSha256 == null)
        {
            // من غير البصمة مش هنثبّت حاجة لوحدنا: نحاول نجيبها تاني، وإلا صفحة التحميل
            try { if (await UpdateChecker.FromApiAsync(repo.Trim().Trim('/'), CancellationToken.None) is { } full && full.Tag == r.Tag) r = full; }
            catch (Exception ex) { Log.Info("update fingerprint: " + ex.Message); }
            if (r.SetupSha256 == null)
            {
                ShowBanner("تعذّر التحقق من بصمة التحديث من GitHub الآن — نزّله من الصفحة أو حاول بعد قليل.", BannerKind.Warning,
                    "صفحة التنزيل", () => OpenInBrowser(r.Url), seconds: 20, key: "update");
                return;
            }
        }
        _updating = true;
        try
        {
            var progress = new Progress<int>(p => ShowStatus($"جارٍ تنزيل التحديث {r.Tag}… {p}%", 60));
            string file = await UpdateChecker.DownloadSetupAsync(r, repo, progress, CancellationToken.None);
            Log.Info($"update downloaded: {r.Tag} -> {file}");
            if (_closing) return;
            ShowStatus("نُزّل التحديث ✓", 5);

            if (!Dialogs.Confirm(this,
                    $"التحديث {r.Version.ToString(3)} جاهز.\n\n" +
                    "سيُغلق البرنامج (بعد حفظ كل شيء)، ثم يُثبَّت التحديث، ثم يُفتح تلقائيًا.\n\nهل نتابع؟",
                    "تحديث"))
            {
                ShowBanner("التحديث منزّل وجاهز للتثبيت عند موافقتك", BannerKind.Info, "ثبّته", () => _ = CheckForUpdatesAsync(manual: true), seconds: 0, key: "update");
                return;
            }

            _pendingInstaller = file;
            // بعد التحديث نرجع لنفس المكتبة (حتى لو «افتح آخر مكتبة» مقفول): ده مش تشغيل جديد، ده كمّلة
            _settings.ReopenAfterUpdate = HasLibrary ? _libraryPath : null;
            _settings.ReopenAfterUpdateUtc = DateTime.UtcNow;
            Close(); // القفل العادي بيحفظ المكتبة والإعدادات والمكان، وبعدين OnFormClosed يشغّل المثبّت
        }
        catch (Exception ex)
        {
            Log.Error("update-download", ex);
            ShowStatus("");
            if (Dialogs.Confirm(this, "تعذّر تنزيل التحديث:\n" + ex.Message + "\n\nهل تريد فتح صفحة التنزيل في المتصفح؟", "تحديث"))
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
            "مستودع GitHub الخاص بالبرنامج (owner/repo):", _settings.UpdateRepo.Length > 0 ? _settings.UpdateRepo : UpdateChecker.BuiltInRepo ?? "");
        if (repo == null) return;
        _settings.UpdateRepo = repo.Trim().Replace("https://github.com/", "", StringComparison.OrdinalIgnoreCase).Trim('/');
        SaveSettings();
        _ = CheckForUpdatesAsync(manual: true);
    }
}
