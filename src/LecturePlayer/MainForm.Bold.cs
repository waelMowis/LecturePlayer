using System.Diagnostics;
using LecturePlayer.Data;
using LecturePlayer.UI;
using LibVLCSharp.Shared;

namespace LecturePlayer;

/// <summary>
/// المرحلة 4 — الأفكار الجريئة: تخطي السكوت، المراجعة المتباعدة، الأجزاء الصعبة، مؤشر الفهم،
/// وضع التركيز الصارم، «اللي فات»، صفحة الإنجازات، ومعمل الكود.
/// </summary>
internal sealed partial class MainForm
{
    // تخطي السكوت
    private List<SilenceDetector.Range>? _silence;
    private VideoItem? _silenceFor;
    private CancellationTokenSource? _silenceCts;

    // وضع التركيز
    private DateTime? _focusUntilUtc;
    private bool _focusBlocked;

    private readonly HashSet<string> _recapShown = new();

    private ToolStripMenuItem BuildBoldMenu()
    {
        var skip = Item("تخطي السكوت تلقائيًا", ToggleSkipSilence, display: "Shift+S");
        skip.Checked = _settings.SkipSilence;
        var menu = SubMenu("تركيز ومراجعة",
            Item("مراجعة النهارده…", ShowReview, Keys.F7),
            Item("علامة مراجعة عند اللحظة دي…", AddReviewBookmark, display: "R"),
            Item("الأجزاء الصعبة في الفيديو ده", ShowDifficultSpots),
            new ToolStripSeparator(),
            Item("وضع التركيز الصارم…", StartFocusDialog),
            Item("إنهاء وضع التركيز", () => EndFocus(early: true)),
            skip,
            new ToolStripSeparator(),
            Item("«اللي فات» في الكورس ده", () => { var c = CurrentOrSelectedCourse(); if (c != null) ShowRecap(c, force: true); }),
            Item("صفحة إنجازاتي (تفتح في المتصفح)…", ExportAchievements),
            Item("معمل الكود…", () => OpenCodeLab(""), Keys.Control | Keys.L));
        menu.DropDownOpening += (_, _) => skip.Checked = _settings.SkipSilence;
        return menu;
    }

    private Course? CurrentOrSelectedCourse() =>
        (_currentGroup != null ? _library.FindCourseOf(_currentGroup) : null) ?? SelectedCourse();

    // =====================================================================
    //  (2) تخطي السكوت
    // =====================================================================

    private void ToggleSkipSilence()
    {
        _settings.SkipSilence = !_settings.SkipSilence;
        if (_settings.SkipSilence && Tools.FindFfmpeg() == null)
        {
            _ = EnsureFfmpegAsync("تخطي السكوت").ContinueWith(t =>
            {
                if (t.Result) Ui(() => { _silenceFor = null; PrepareSilence(); });
            });
        }
        ShowStatus(_settings.SkipSilence
            ? $"⏭ تخطي السكوت شغال (وفّر لحد دلوقتي {TimeFormat.Format(_settings.SilenceSavedSeconds * 1000)})"
            : "تخطي السكوت اتقفل", 4);
        _silenceFor = null;
        PrepareSilence();
    }

    private string SilencePath(VideoItem v) => Path.ChangeExtension(TranscriptStore.PathFor(_libraryPath, v), ".silence.txt");

    /// <summary>بيتنادى لما فيديو يبدأ: يحمّل فترات السكوت أو يحسبها في الخلفية.</summary>
    private void PrepareSilence()
    {
        var v = _currentVideo;
        if (!_settings.SkipSilence || v == null) { _silence = null; _silenceFor = null; return; }
        if (_silenceFor == v) return;
        _silenceFor = v;
        _silence = null;
        _silenceCts?.Cancel();

        string cache = SilencePath(v);
        if (File.Exists(cache))
        {
            try { _silence = SilenceDetector.Deserialize(File.ReadAllText(cache)); return; }
            catch { /* نحسبه تاني */ }
        }
        string? file = PlayableLocalFile(v);
        if (file == null || Tools.FindFfmpeg() == null) return; // فيديو أونلاين: لازم يتنزل الأول

        var cts = _silenceCts = new CancellationTokenSource();
        long duration = v.DurationMs;
        ShowStatus("بدوّر على فترات السكوت في الفيديو (في الخلفية)…", 4);
        _ = Task.Run(async () =>
        {
            try
            {
                var ranges = await SilenceDetector.DetectAsync(file, duration, cts.Token);
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                File.WriteAllText(cache, SilenceDetector.Serialize(ranges));
                long total = ranges.Where(r => r.Length >= 1200).Sum(r => r.Length);
                Ui(() =>
                {
                    if (_silenceFor != v) return;
                    _silence = ranges;
                    ShowStatus($"⏭ هيتخطى {TimeFormat.Format(total)} سكوت في الفيديو ده", 5);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Error("silence", ex); }
        });
    }

    private void SilenceTick()
    {
        if (_silence == null || _silenceFor != _currentVideo || _mp.State != VLCState.Playing) return;
        long t = _mp.Time;
        if (SilenceDetector.SkipTo(_silence, t) is not { } target) return;
        _mp.Time = target;
        _settings.SilenceSavedSeconds += (target - t) / 1000;
    }

    // =====================================================================
    //  (16) الأجزاء الصعبة: تسجيل الرجوع لورا
    // =====================================================================

    /// <summary>بيتنادى قبل أي تقديم/رجوع يدوي.</summary>
    private void NoteSeek(long fromMs, long toMs)
    {
        var v = _currentVideo;
        if (v == null || _mp.State != VLCState.Playing || !DifficultSpots.IsRewind(fromMs, toMs)) return;
        var g = _currentGroup;
        LogJournal(new JournalEntry
        {
            Kind = JournalEntry.Rewind, StartUtc = DateTime.UtcNow, VideoPath = v.Path, FromMs = fromMs, ToMs = toMs,
            GroupId = g?.Id, CourseId = g != null ? _library.FindCourseOf(g)?.Id : null
        });
    }

    private void ShowDifficultSpots()
    {
        var v = TargetVideo();
        if (v == null) { ShowStatus("شغّل أو حدد فيديو الأول"); return; }
        var spots = DifficultSpots.Detect(JournalEntries, v.Path);
        if (spots.Count == 0)
        {
            Dialogs.Info(this, "مفيش أجزاء صعبة في الفيديو ده لسه.\n\nالبرنامج بيعتبر الجزء «صعب» لما ترجع له لورا مرتين أو أكتر (من 3 ثواني لـ 3 دقايق).");
            return;
        }
        ClearNodeMenu();
        foreach (var s in spots)
        {
            var spot = s;
            var item = MenuItem($"⚠ {Transcript.Short(s.StartMs)} – {Transcript.Short(s.EndMs)}   (رجعتله {s.Count} مرات)", () => PlayAt(v, spot.StartMs));
            item.DropDownItems.Add(MenuItem("▶ شغّله", () => PlayAt(v, spot.StartMs)));
            item.DropDownItems.Add(MenuItem("📌 حطله علامة مراجعة", () => AddReviewAt(v, spot.StartMs, "جزء صعب")));
            _nodeMenu.Items.Add(item);
        }
        _nodeMenu.Show(this, PointToClient(Cursor.Position));
    }

    // =====================================================================
    //  (8) المراجعة المتباعدة
    // =====================================================================

    private void AddReviewBookmark()
    {
        var v = _currentVideo;
        if (v == null || !IsActive) { ShowStatus("شغّل فيديو الأول"); return; }
        AddReviewAt(v, _mp.Time, null);
    }

    private void AddReviewAt(VideoItem v, long timeMs, string? defaultNote)
    {
        bool wasPlaying = _mp.State == VLCState.Playing && v == _currentVideo;
        if (wasPlaying) _mp.SetPause(true);
        string? note = Dialogs.Input(this, "علامة مراجعة", $"الفكرة اللي عايز تفتكرها عند {Transcript.Short(timeMs)} (اكتبها كسؤال أحسن):", defaultNote ?? "");
        if (wasPlaying) _mp.SetPause(false);
        if (note == null) return;

        var b = new Bookmark { TimeMs = timeMs, Note = "📌 " + note };
        ReviewScheduler.Schedule(b, DateTime.UtcNow);
        v.Bookmarks.Add(b);
        v.Touch();
        MarkDirty();
        SaveLibrary();
        RefreshNotesPane();
        RefreshVideo(v);
        ShowStatus("📌 هتراجعها بكره، وبعدين بعد 3 أيام، أسبوع، 3 أسابيع، وشهرين ✓", 5);
    }

    private void ToggleReviewForSelectedBookmark()
    {
        var b = SelectedBookmark();
        var v = _notesVideo;
        if (b == null || v == null) return;
        if (b.ReviewDueUtc != null) { ReviewScheduler.Unschedule(b); ShowStatus("اتشالت من المراجعة"); }
        else { ReviewScheduler.Schedule(b, DateTime.UtcNow); ShowStatus("📌 اتضافت للمراجعة — أول مراجعة بكره"); }
        v.Touch();
        MarkDirty();
        SaveLibrary();
        RefreshNotesPane();
    }

    private void ShowReview()
    {
        var due = ReviewScheduler.Due(_library, DateTime.UtcNow);
        if (due.Count == 0)
        {
            var next = ReviewScheduler.All(_library).Where(i => i.Bookmark.ReviewDueUtc != null).Min(i => i.Bookmark.ReviewDueUtc);
            Dialogs.Info(this, "مفيش مراجعة النهارده ✓" + (next is { } n ? $"\n\nالمراجعة الجاية: {n.ToLocalTime():yyyy/MM/dd}" :
                "\n\nحط علامة مراجعة بـ R وإنت بتتفرج على فكرة عايز تثبّتها."));
            return;
        }
        new ReviewForm(due, it => PlayAt(it.Video, Math.Max(0, it.Bookmark.TimeMs - 5000)), (it, remembered) =>
        {
            if (remembered == null) ReviewScheduler.Unschedule(it.Bookmark);
            else
            {
                ReviewScheduler.Grade(it.Bookmark, remembered.Value, DateTime.UtcNow);
                LogJournal(new JournalEntry
                {
                    Kind = JournalEntry.Review, StartUtc = DateTime.UtcNow, VideoPath = it.Video.Path, GroupId = it.Group.Id,
                    CourseId = it.Course.Id, Score = remembered.Value ? 1 : 0, Total = 1, FromMs = it.Bookmark.TimeMs
                });
            }
            it.Video.Touch();
            MarkDirty();
            SaveLibrary();
            RefreshNotesPane();
        }).Show(this);
    }

    // =====================================================================
    //  (9) وضع التركيز الصارم
    // =====================================================================

    private bool FocusActive => _focusUntilUtc is { } u && u > DateTime.UtcNow;

    private void StartFocusDialog()
    {
        if (FocusActive) { ShowStatus("وضع التركيز شغال بالفعل"); return; }
        using var kit = new FormKit("وضع التركيز الصارم", 520);
        kit.Note("ملء الشاشة، المكتبة مستخبية، ومينفعش تخرج قبل الوقت غير لما تكتب جملة تأكيد.", 40, Theme.Text);
        var minutes = kit.Row("المدة (دقيقة)", new NumberBox { Minimum = 10, Maximum = 240, Increment = 5, Value = Math.Clamp(_settings.FocusMinutes, 10, 240) });
        var block = kit.Row("حجب المواقع", new ToggleSwitch { Text = "احجب المواقع دي طول المدة", Checked = _settings.FocusBlockSites });
        var sites = kit.Row("المواقع", new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, RightToLeft = RightToLeft.No, Text = string.Join("\r\n", _settings.FocusSites) }, 110);
        kit.Note("عشان يقفل المواقع دي، الويندوز هيسألك «تسمح للبرنامج يعمل تغييرات؟» — دوس «نعم». هيسألك مرة في الأول ومرة في الآخر.\nيوتيوب مش في قايمة الحجب عشان الكورسات.", 40);
        kit.Buttons("ابدأ");
        if (kit.Show(this) != DialogResult.OK) return;

        _settings.FocusMinutes = (int)minutes.Value;
        _settings.FocusBlockSites = block.Checked;
        _settings.FocusSites = sites.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        SaveSettings();

        if (_settings.FocusBlockSites)
        {
            _focusBlocked = RunElevatedFocus(block: true);
            if (!_focusBlocked) ShowStatus("مقدرتش أحجب المواقع (الصلاحية اترفضت) — التركيز هيكمل من غير حجب", 6);
        }

        _focusUntilUtc = DateTime.UtcNow.AddMinutes(_settings.FocusMinutes);
        if (!_split.Panel2Collapsed) ToggleSidePanel();
        if (!_fullscreen && _currentVideo != null) ToggleFullscreen();
        if (!_pomoRunning) TogglePomodoro();
        Notify("🎯 وضع التركيز بدأ", $"{_settings.FocusMinutes} دقيقة. بالتوفيق!");
        if (_currentVideo == null && _library.Courses.FirstOrDefault(c => !c.IsCompleted && c.Status == CourseStatus.Active) is { } next)
            PlayCourse(next);
    }

    /// <summary>بيشغّل البرنامج نفسه كمسؤول عشان يعدّل hosts.</summary>
    private bool RunElevatedFocus(bool block)
    {
        try
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" };
            psi.ArgumentList.Add(block ? "--focus-block" : "--focus-unblock");
            if (block) foreach (var s in _settings.FocusSites) psi.ArgumentList.Add(s);
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(30_000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error("focus elevate", ex); // المستخدم رفض الـ UAC
            return false;
        }
    }

    /// <summary>بيتنادى من التايمر.</summary>
    private void FocusTick()
    {
        if (_focusUntilUtc == null) return;
        if (DateTime.UtcNow >= _focusUntilUtc) { EndFocus(early: false); return; }
        if (_tick % 4 == 0) UpdateStudyStatus();
    }

    /// <summary>true = مسموح يخرج (خلّص أو كتب جملة التأكيد).</summary>
    private bool ConfirmLeaveFocus()
    {
        if (!FocusActive) return true;
        var left = _focusUntilUtc!.Value - DateTime.UtcNow;
        const string phrase = "هسيب التركيز";
        string? typed = Dialogs.Input(this, "وضع التركيز",
            $"لسه فاضل {(int)left.TotalMinutes} دقيقة. لو متأكد إنك عايز تخرج اكتب: {phrase}");
        if (typed?.Trim() != phrase) return false;
        EndFocus(early: true);
        return true;
    }

    private void EndFocus(bool early)
    {
        if (_focusUntilUtc == null) { if (early) ShowStatus("وضع التركيز مش شغال"); return; }
        _focusUntilUtc = null;
        if (_focusBlocked) { RunElevatedFocus(block: false); _focusBlocked = false; }
        if (!early) Notify("🎯 خلصت جلسة التركيز", "شغل عظيم! خد بريك محترم.");
        else ShowStatus("وضع التركيز اتقفل", 4);
        UpdateStudyStatus();
    }

    /// <summary>عند فتح البرنامج: لو الحجب فضل من جلسة اتقفلت غلط، نعرض نشيله.</summary>
    private void CheckLeftoverFocusBlock()
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(FocusBlocker.HostsPath)) return;
            if (!FocusBlocker.IsBlocked(File.ReadAllText(FocusBlocker.HostsPath))) return;
            if (Dialogs.Confirm(this, "فيه مواقع لسه محجوبة من جلسة تركيز قديمة (البرنامج اتقفل قبل ما يشيل الحجب).\n\nأشيل الحجب دلوقتي؟", "وضع التركيز"))
                RunElevatedFocus(block: false);
        }
        catch (Exception ex) { Log.Error("focus leftover", ex); }
    }

    // =====================================================================
    //  (15) «اللي فات»
    // =====================================================================

    /// <summary>بيتنادى لما تبدأ كورس: لو غايب عنه كام يوم، نفكّرك وقفت فين.</summary>
    private void MaybeShowRecap(Course c)
    {
        if (_settings.RecapAfterDays <= 0 || !Recap.ShouldOffer(c, DateTime.UtcNow, _settings.RecapAfterDays)) return;
        if (!_recapShown.Add(c.Id)) return;
        ShowRecap(c, force: false);
    }

    private void ShowRecap(Course c, bool force)
    {
        if (c.WatchedCount == 0 && !force) return;
        string text = Recap.LocalRecap(c, DateTime.UtcNow);
        new TextViewerForm("اللي فات: " + c.Name, text, NotesExporter.SafeFileName(c.Name) + " - اللي فات.md",
            Act("ملخص أذكى بـ Claude", text => { _ = RecapWithClaudeAsync(c); })).Show(this);
    }

    private async Task RecapWithClaudeAsync(Course c)
    {
        var cts = BeginSmart();
        if (cts == null) return;
        try
        {
            var recent = Recap.RecentlyWatched(c).AsEnumerable().Reverse()
                .Select(v => (v, TranscriptStore.Load(_libraryPath, v))).ToList();
            if (recent.All(x => x.Item2 == null && string.IsNullOrWhiteSpace(x.v.Notes)))
            {
                Dialogs.Info(this, "آخر فيديوهات كلامها متحوّلش لنص ومفيهاش ملاحظات — حوّل كلامها لنص الأول (منيو ذكاء) عشان Claude يقدر يلخصها.");
                return;
            }
            string? text = await RunClaudeAsync("اللي فات", Recap.Prompt(c, recent), cts.Token);
            if (text != null) new TextViewerForm("اللي فات (Claude): " + c.Name, text, NotesExporter.SafeFileName(c.Name) + " - اللي فات.md").Show(this);
        }
        catch (OperationCanceledException) { }
        catch (ClaudeException ex) { Dialogs.Error(this, ex.Message); }
        catch (Exception ex) { Log.Error("recap", ex); Dialogs.Error(this, ex.Message); }
        finally { EndSmart(cts); }
    }

    // =====================================================================
    //  (12) صفحة الإنجازات  ·  (6) معمل الكود
    // =====================================================================

    private void ExportAchievements()
    {
        FlushSession();
        using var dlg = new SaveFileDialog
        {
            Filter = "صفحة ويب (*.html)|*.html",
            FileName = $"إنجازاتي-{DateTime.Now:yyyy-MM-dd}.html",
            InitialDirectory = Path.GetDirectoryName(_libraryPath)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, AchievementPage.BuildHtml(_library, JournalEntries, _settings.DailyGoalMinutes, TodayLocal),
                new System.Text.UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) { Dialogs.Error(this, "مقدرتش أعمل الصفحة:\n" + ex.Message); }
    }

    private void OpenCodeLab(string code)
    {
        string dir = Path.Combine(Storage.AppDataDir, "codelab");
        new CodeLabForm(code.Length > 0 ? code : "// اكتب أو الصق كود C# وشغّله بـ F5\nConsole.WriteLine(\"أهلًا من معمل الكود!\");\n", dir).Show(this);
    }
}
