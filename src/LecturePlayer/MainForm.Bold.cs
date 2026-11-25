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
    private long _dueFocusStatus;
    private VideoItem? _silenceFor;
    private CancellationTokenSource? _silenceCts;

    // وضع التركيز
    private DateTime? _focusUntilUtc;
    private bool _focusBlocked;

    private readonly HashSet<string> _recapShown = new();


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
            _ = EnsureFfmpegAsync("تخطّي الصمت").ContinueWith(t =>
            {
                if (t.Result) Ui(() => { _silenceFor = null; PrepareSilence(); });
            });
        }
        ShowStatus(_settings.SkipSilence
            ? $"⏭ تخطّي الصمت يعمل (وفّر حتى الآن {TimeFormat.Format(_settings.SilenceSavedSeconds * 1000)})"
            : "أُوقف تخطّي الصمت", 4);
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
        ShowStatus("جارٍ البحث عن فترات الصمت في الفيديو (في الخلفية)…", 4);
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
                    ShowStatus($"⏭ سيُتخطّى {TimeFormat.Format(total)} من الصمت في هذا الفيديو", 5);
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
        if (v == null) { ShowStatus("شغّل فيديو أو حدّده أولًا"); return; }
        var spots = DifficultSpots.Detect(JournalEntries, v.Path);
        if (spots.Count == 0)
        {
            Dialogs.Info(this, "لا توجد أجزاء صعبة في هذا الفيديو بعد.\n\nيعدّ البرنامج الجزء «صعبًا» عندما ترجع إليه مرتين أو أكثر (من 3 ثوانٍ إلى 3 دقائق).");
            return;
        }
        ClearNodeMenu();
        foreach (var s in spots)
        {
            var spot = s;
            var item = MenuItem($"⚠ {Transcript.Short(s.StartMs)} – {Transcript.Short(s.EndMs)}   (رجعت إليه {s.Count} مرات)", () => PlayAt(v, spot.StartMs));
            item.DropDownItems.Add(MenuItem("▶ شغّله", () => PlayAt(v, spot.StartMs)));
            item.DropDownItems.Add(MenuItem("📌 ضع له علامة مراجعة", () => AddReviewAt(v, spot.StartMs, "جزء صعب")));
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
        if (v == null || !IsActive) { ShowStatus("شغّل فيديو أولًا"); return; }
        AddReviewAt(v, _mp.Time, null);
    }

    private void AddReviewAt(VideoItem v, long timeMs, string? defaultNote)
    {
        bool wasPlaying = _mp.State == VLCState.Playing && v == _currentVideo;
        if (wasPlaying) _mp.SetPause(true);
        string? note = Dialogs.Input(this, "علامة مراجعة", $"الفكرة التي تريد تذكّرها عند {Transcript.Short(timeMs)} (الأفضل أن تكتبها سؤالًا):", defaultNote ?? "");
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
        ShowStatus("📌 ستراجعها غدًا، ثم بعد 3 أيام، وأسبوع، و3 أسابيع، وشهرين ✓", 5);
    }

    private void ToggleReviewForSelectedBookmark()
    {
        var b = SelectedBookmark();
        var v = _notesVideo;
        if (b == null || v == null) return;
        if (b.ReviewDueUtc != null) { ReviewScheduler.Unschedule(b); ShowStatus("أُزيلت من المراجعة"); }
        else { ReviewScheduler.Schedule(b, DateTime.UtcNow); ShowStatus("📌 أُضيفت إلى المراجعة — أول مراجعة غدًا"); }
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
            Dialogs.Info(this, "لا توجد مراجعة اليوم ✓" + (next is { } n ? $"\n\nالمراجعة القادمة: {n.ToLocalTime():yyyy/MM/dd}" :
                "\n\nضع علامة مراجعة بـ R أثناء المشاهدة على فكرة تريد تثبيتها."));
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
        if (FocusActive) { ShowStatus("وضع التركيز يعمل بالفعل"); return; }
        using var kit = new FormKit("وضع التركيز الصارم", 520);
        kit.Note("ملء الشاشة، والمكتبة مخفية، ولا يمكنك الخروج قبل الوقت إلا بكتابة جملة تأكيد.", 40, Theme.Text);
        var minutes = kit.Row("المدة (دقيقة)", new NumberBox { Minimum = 10, Maximum = 240, Increment = 5, Value = Math.Clamp(_settings.FocusMinutes, 10, 240) });
        var block = kit.Row("حجب المواقع", new ToggleSwitch { Text = "احجب هذه المواقع طوال المدة", Checked = _settings.FocusBlockSites });
        var sites = kit.Row("المواقع", new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, RightToLeft = RightToLeft.No, Text = string.Join("\r\n", _settings.FocusSites) }, 110);
        kit.Note("لحجب هذه المواقع سيسألك ويندوز «هل تسمح للبرنامج بإجراء تغييرات؟» — اضغط «نعم». سيسألك مرة في البداية ومرة في النهاية.\nيوتيوب ليس في قائمة الحجب من أجل الدورات.", 40);
        kit.Buttons("ابدأ");
        if (kit.Show(this) != DialogResult.OK) return;

        _settings.FocusMinutes = (int)minutes.Value;
        _settings.FocusBlockSites = block.Checked;
        _settings.FocusSites = sites.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        SaveSettings();

        if (_settings.FocusBlockSites)
        {
            _focusBlocked = RunElevatedFocus(block: true);
            if (!_focusBlocked) ShowStatus("تعذّر حجب المواقع (رُفضت الصلاحية) — سيستمر التركيز دون حجب", 6);
        }

        _focusUntilUtc = DateTime.UtcNow.AddMinutes(_settings.FocusMinutes);
        if (!_split.Panel2Collapsed) ToggleSidePanel();
        if (!_fullscreen && _currentVideo != null) ToggleFullscreen();
        if (!_pomoRunning) TogglePomodoro();
        Notify("🎯 بدأ وضع التركيز", $"{_settings.FocusMinutes} دقيقة. بالتوفيق!");
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
        if (Due(ref _dueFocusStatus, 1000)) UpdateStudyStatus();
    }

    /// <summary>true = مسموح يخرج (خلّص أو كتب جملة التأكيد).</summary>
    private bool ConfirmLeaveFocus()
    {
        if (!FocusActive) return true;
        var left = _focusUntilUtc!.Value - DateTime.UtcNow;
        const string phrase = "سأترك التركيز";
        string? typed = Dialogs.Input(this, "وضع التركيز",
            $"ما زال متبقيًا {(int)left.TotalMinutes} دقيقة. إن كنت متأكدًا من الخروج فاكتب: {phrase}");
        if (typed?.Trim() != phrase) return false;
        EndFocus(early: true);
        return true;
    }

    private void EndFocus(bool early)
    {
        if (_focusUntilUtc == null) { if (early) ShowStatus("وضع التركيز لا يعمل"); return; }
        _focusUntilUtc = null;
        if (_focusBlocked) { RunElevatedFocus(block: false); _focusBlocked = false; }
        if (!early) Notify("🎯 انتهت جلسة التركيز", "عمل رائع! خذ استراحة جيدة.");
        else ShowStatus("أُنهي وضع التركيز", 4);
        UpdateStudyStatus();
    }

    /// <summary>عند فتح البرنامج: لو الحجب فضل من جلسة اتقفلت غلط، نعرض نشيله.</summary>
    private void CheckLeftoverFocusBlock()
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(FocusBlocker.HostsPath)) return;
            if (!FocusBlocker.IsBlocked(File.ReadAllText(FocusBlocker.HostsPath))) return;
            if (Dialogs.Confirm(this, "توجد مواقع ما زالت محجوبة من جلسة تركيز سابقة (أُغلق البرنامج قبل رفع الحجب).\n\nهل تريد رفع الحجب الآن؟", "وضع التركيز"))
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
        new TextViewerForm("ما سبق: " + c.Name, text, NotesExporter.SafeFileName(c.Name) + " - ما سبق.md",
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
                Dialogs.Info(this, "آخر الفيديوهات لم يُحوَّل كلامها إلى نص ولا توجد عليها ملاحظات — حوّل كلامها إلى نص أولًا (الأدوات ← الذكاء الاصطناعي) ليتمكن Claude من تلخيصها.");
                return;
            }
            string? text = await RunClaudeAsync("ما سبق", Recap.Prompt(c, recent), cts.Token);
            if (text != null) new TextViewerForm("ما سبق (Claude): " + c.Name, text, NotesExporter.SafeFileName(c.Name) + " - ما سبق.md").Show(this);
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
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Dialogs.Error(this, "تعذّر إنشاء الصفحة:\n" + ex.Message); }
    }

    private void OpenCodeLab(string code)
    {
        string dir = Path.Combine(Storage.AppDataDir, "codelab");
        new CodeLabForm(code.Length > 0 ? code : "// اكتب أو الصق كود C# وشغّله بـ F5\nConsole.WriteLine(\"مرحبًا من معمل الكود!\");\n", dir).Show(this);
    }
}
