using LecturePlayer.Data;
using LecturePlayer.UI;
using LibVLCSharp.Shared;

namespace LecturePlayer;

/// <summary>
/// كورسات يوديمي اللي إنت شاريها:
/// الدخول بحسابك (على موقع يوديمي نفسه جوه «مسار»)، استيراد الكورسات (القسم = محاضرة، والدرس = فيديو)،
/// والتشغيل جوه مساحة الفيديو بصفحة يوديمي الحقيقية و«مسار» بيتحكم فيها ويسجّل مكانك ووقت المذاكرة.
/// مفيش تنزيل ولا فك حماية: الفيديو بيفضل محمي جوه يوديمي.
/// </summary>
internal sealed partial class MainForm
{
    private UdemyPane? _udemyPane;
    private long _dueUdemyPoll;
    private const string WebView2Download = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>درس يوديمي هو اللي شغال دلوقتي (الصفحة ظاهرة في مساحة الفيديو).</summary>
    /// (بفلاج خاص مش Visible: Visible بيرجع false لو أي أب مستخبي، زي وقت تغيير الشكل)
    private bool UdemyActive => _udemyShown && _udemyPane != null && _currentVideo is { IsWebLesson: true };
    private bool _udemyShown;

    /// <summary>فيه تشغيل فعلًا (VLC أو صفحة يوديمي).</summary>
    private bool PlayingNow => UdemyActive ? _udemyPane!.State?.Playing == true : _mp.State == VLCState.Playing;

    /// <summary>إيقاف مؤقت لأي حاجة شغالة (مؤقت النوم، البريك…).</summary>
    private void PausePlayback()
    {
        if (UdemyActive) _udemyPane!.Pause();
        else if (_mp.IsPlaying) _mp.SetPause(true);
    }

    // =====================================================================
    //  المشغّل
    // =====================================================================

    private UdemyPane EnsureUdemyPane()
    {
        if (_udemyPane != null) return _udemyPane;
        var pane = new UdemyPane { Dock = DockStyle.Fill };
        _videoArea.Controls.Add(pane);
        pane.KeyForwarded += key =>
        {
            if (IsDisposed) return;
            var msg = new Message();
            ProcessCmdKey(ref msg, key);
        };
        pane.ApiRequested += OnItLegendApiRequest;
        pane.FullscreenChanged += fs => { if (fs != _fullscreen && !IsDisposed) ToggleFullscreen(); };
        pane.Navigated += (ok, url) =>
        {
            if (!ok && UdemyActive && url != "about:blank")
                ShowBanner("صفحة يوديمي مافتحتش — اتأكد من النت", BannerKind.Warning, "↻  حاول تاني", () => pane.Web.CoreWebView2?.Reload(), seconds: 0, key: "udemy");
        };
        _udemyPane = pane;
        return pane;
    }

    private async Task PlayUdemyAsync(int request, VideoItem v, long startMs)
    {
        if (UdemyWeb.RuntimeVersion == null)
        {
            ShowWebView2Missing();
            return;
        }
        var pane = EnsureUdemyPane();
        pane.Visible = true;
        _udemyShown = true;
        pane.BringToFront();
        _videoView.Visible = false;
        HideBanner("udemy");
        try
        {
            string platform = v.IsItLegend ? "IT Legend" : "يوديمي";
            if (v.IsItLegend) { await pane.EnsureReadyAsync(); pane.WatchApi(ItLegendClient.TestApi ?? ItLegend.Api); }
            if (v.IsItLegend && ItLegend.ParseLesson(v.Path) is { } l && ItLegend.CourseIdOf(pane.Web.CoreWebView2?.Source) == l.CourseId)
                pane.Retarget(startMs, _activeRate, _muted ? 0 : _settings.Volume); // نفس الكورس مفتوح: نختار الدرس بس من غير تحميل الصفحة
            else
                await pane.OpenAsync(v.IsItLegend ? ItLegend.CourseUrl(ItLegend.ParseLesson(v.Path)!.CourseId) : v.Path, startMs, _activeRate, _muted ? 0 : _settings.Volume);
            if (request != _playRequest) return;
            if (v.IsItLegend) BeginItLegendSelect(v);
            if (_muted) pane.SetMuted(true);
            ShowStatus(startMs > 3000 ? $"{platform} — استكمال من {TimeFormat.Format(startMs)}" : $"{platform} — بيفتح الدرس…", 4);
        }
        catch (Exception ex)
        {
            Log.Error("udemy open", ex);
            if (request == _playRequest) ShowWebView2Missing(ex.Message);
        }
    }

    private void ShowWebView2Missing(string? detail = null)
    {
        HideUdemyPane();
        ShowBanner("دروس يوديمي محتاجة «Microsoft Edge WebView2» (موجود في ويندوز 10/11 عادةً)" + (detail != null ? " — " + detail : ""),
            BannerKind.Warning, "تنزيله", () => OpenInBrowser(WebView2Download), seconds: 0, key: "udemy");
    }

    /// <summary>بيرجّع شاشة VLC ويقفل صفحة يوديمي (لو كانت مفتوحة).</summary>
    private void HideUdemyPane()
    {
        if (_udemyPane == null) return;
        if (_udemyShown || _udemyPane.Visible)
        {
            _udemyPane.Stop();
            _udemyPane.Visible = false;
            _udemyShown = false;
        }
        _qualityChip.Visible = false;
        _videoView.Visible = true;
    }

    private void UdemyToggle()
    {
        var pane = _udemyPane!;
        if (pane.State is not { HasVideo: true })
        {
            ShowStatus(pane.State == null ? "لسه بيفتح الدرس…" : "الدرس ده مش فيديو (مقال/اختبار) — كمّله في الصفحة", 2);
            return;
        }
        if (pane.State.Playing) pane.Pause(); else pane.Play();
    }

    private void SaveUdemyPosition()
    {
        var v = _currentVideo;
        var s = _udemyPane?.State;
        if (v == null || s is not { HasVideo: true } || s.TimeMs <= 0) return;
        if (v.IsUdemy && Udemy.ItemKey(s.Href) is { } k && k != Udemy.ItemKey(v.Path)) return; // الصفحة على درس تاني
        if (v.IsItLegend && _itlPendingNode != null) return; // لسه بنختار الدرس: الوقت ده بتاع الدرس اللي قبله
        if (Math.Abs(v.LastPositionMs - s.TimeMs) > 1000) v.Touch();
        v.LastPositionMs = s.TimeMs;
        MarkDirty();
        RefreshVideo(v);
    }

    /// <summary>من تايمر الواجهة: نقرا الفيديو، نحدّث الشريط والوقت، ونحفظ المكان و«اتشاف».</summary>
    private void UdemyTick(VideoItem v)
    {
        var pane = _udemyPane!;
        if (Due(ref _dueUdemyPoll, 450)) pane.Poll();
        var s = pane.State;
        if (s == null) { UpdateQualityChip(null); return; }
        WebQualityTick(pane, s);

        // يوديمي بيروح للدرس التالي لوحده (أو إنت اخترت درس من جوه الصفحة): «مسار» يمشي معاه
        if (v.IsItLegend) { ItLegendSelectTick(v); if (_itlPendingNode != null || pane.HoldPending) return; }
        string? pageKey = v.IsUdemy ? Udemy.ItemKey(s.Href) : null;
        if (pageKey != null && pageKey != Udemy.ItemKey(v.Path) && pane.MsSinceOpen > 3000)
        {
            if (FollowUdemyLecture(v, pageKey)) return;
        }

        if (!s.HasVideo) return;
        long len = s.DurationMs > 0 ? s.DurationMs : v.DurationMs, t = s.TimeMs;
        if (len <= 0) return;
        if (!_seekBar.IsDragging)
        {
            _seekBar.Value = Math.Clamp((double)t / len, 0, 1);
            _timeLabel.Set(t, len);
        }
        if (s.DurationMs > 0 && Math.Abs(v.DurationMs - s.DurationMs) > 2000) { v.DurationMs = s.DurationMs; v.Touch(); MarkDirty(); }

        if (s.Playing && Due(ref _duePosition, 5000))
        {
            v.LastPositionMs = t;
            v.Touch();
            if (!v.Watched && t >= len * 0.92) MarkUdemyWatched(v);
            MarkPositionDirty();
            RefreshVideo(v);
        }
        else if (s.Ended && !v.Watched) MarkUdemyWatched(v);
    }

    private void MarkUdemyWatched(VideoItem v)
    {
        v.Watched = true;
        v.Touch();
        LogWatched(v);
        RefreshGroup(_currentGroup);
        RefreshVideo(v);
        MarkDirty();
    }

    /// <summary>الصفحة بقت على درس تاني: نخلّي الدرس ده هو «الحالي» في الشجرة (من غير ما نفتح الصفحة تاني).</summary>
    private bool FollowUdemyLecture(VideoItem from, string itemKey)
    {
        var course = _library.FindCourseOf(from);
        var target = course?.AllVideos.FirstOrDefault(x => Udemy.ItemKey(x.Path) == itemKey);
        if (target == null || _library.FindGroupOf(target) is not { } g) return false;
        FollowWebLesson(from, g, target);
        return true;
    }

    /// <summary>الدرس اتغيّر جوه الصفحة: «مسار» يخلّيه هو الحالي في الشجرة من غير ما يفتح الصفحة تاني.</summary>
    private void FollowWebLesson(VideoItem from, LectureGroup g, VideoItem target)
    {

        // اللي قبله خلص لو يوديمي مشي للتالي لوحده
        if (!from.Watched && from.DurationMs > 0 && from.LastPositionMs >= from.DurationMs * 0.85) MarkUdemyWatched(from);
        CommitNotes();
        var previousGroup = _currentGroup;
        _currentGroup = g;
        _currentVideo = target;
        _udemyPane!.AdoptLecture(target.IsUdemy ? Udemy.LectureId(target.Path) : 0);
        if (target.IsNew) { target.IsNew = false; target.Touch(); }
        _library.LastGroupId = g.Id;
        _library.LastVideoPath = target.Path;
        MarkDirty();
        RefreshVideo(from);
        if (previousGroup != g) RefreshGroup(previousGroup);
        RefreshVideo(target);
        RefreshGroup(g);
        if (_videoNodes.TryGetValue(target, out var vn)) { _tree.SelectedNode = vn; vn.EnsureVisible(); }
        UpdateNowPlaying();
        UpdateTitle();
        RefreshNotesPane();
    }

    // =====================================================================
    //  الحساب والاستيراد
    // =====================================================================

    /// <summary>
    /// يوديمي ← كورساتي: بيدخل (لو محتاج)، يعرض كورساتك تختار منها، ويستوردها أو يحدّثها.
    /// refresh = كورس موجود: بيحدّث منهجه وتقدّمك بس.
    /// </summary>
    private void UdemyImport(Course? refresh = null, ImportTarget? target = null)
    {
        if (UdemyWeb.RuntimeVersion == null) { ShowWebView2Missing(); return; }

        var results = new List<(Course Course, Udemy.SyncResult Sync, bool New)>();
        string? error = null;
        using var form = new UdemyAccountForm(refresh != null ? $"يوديمي — تحديث «{refresh.Name}»" : "يوديمي — كورساتي");
        using var cts = new CancellationTokenSource();
        form.FormClosing += (_, _) => cts.Cancel();
        form.Shown += async (_, _) =>
        {
            try
            {
                string? name = await form.SignInAsync(cts.Token);
                if (name == null) return;
                form.Status($"أهلًا {name} — بجيب كورساتك…", Theme.Success);
                var all = await FetchUdemyCoursesAsync(form, cts.Token);

                List<UdemyCourse> chosen;
                Topic? topic = null;
                string? topicName = null;
                if (refresh != null)
                {
                    chosen = all.Where(c => c.Id == refresh.UdemyId).ToList();
                    if (chosen.Count == 0) { error = $"«{refresh.Name}» مش لاقيه في كورسات الحساب ده على يوديمي."; form.Close(); return; }
                }
                else
                {
                    if (all.Count == 0) { error = "الحساب ده مافيهوش كورسات مشتراة على يوديمي."; form.Close(); return; }
                    var pick = PickUdemyCourses(form, all, target);
                    if (pick == null) { form.Close(); return; }
                    (chosen, topicName) = pick.Value;
                    topic = _library.Topics.FirstOrDefault(t => string.Equals(t.Name, topicName, StringComparison.OrdinalIgnoreCase));
                }

                Checkpoint(refresh != null ? "تحديث من يوديمي" : "استيراد من يوديمي");
                for (int i = 0; i < chosen.Count; i++)
                {
                    var uc = chosen[i];
                    form.Status($"({i + 1}/{chosen.Count}) بجيب منهج «{uc.Title}»…");
                    var chapters = await FetchUdemyCurriculumAsync(form, uc.Id, cts.Token);
                    var done = await FetchUdemyProgressAsync(form, uc.Id, cts.Token);
                    var course = refresh ?? _library.Courses.FirstOrDefault(c => c.UdemyId == uc.Id);
                    bool isNew = course == null;
                    if (course == null)
                    {
                        course = new Course { Name = uc.Title };
                        topic = PlaceImportedCourse(course, target, topic, topicName ?? "كورسات يوديمي", "Udemy", uc.Instructor);
                        _library.Courses.Add(course);
                    }
                    var sync = Udemy.Apply(course, uc, chapters, done, firstImport: isNew);
                    results.Add((course, sync, isNew));
                }
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error("udemy import", ex);
                if (!form.IsDisposed) form.Status("حصلت مشكلة: " + ex.Message + " — اقفل الشباك وجرّب تاني.", Theme.Danger);
            }
        };
        form.ShowDialog(this);

        if (error != null) { Dialogs.Info(this, error); return; }
        if (results.Count == 0) return;
        MarkDirty();
        RebuildTree();
        SelectItem(results[0].Course);
        int added = results.Count(r => r.New);
        int lessons = results.Sum(r => r.Sync.AddedLectures), watched = results.Sum(r => r.Sync.MarkedWatched);
        ShowBanner(added > 0
                ? $"🎓 اتضاف {added} كورس من يوديمي ({results.Where(r => r.New).Sum(r => r.Course.VideoCount)} درس) — دوس على أي درس يشتغل هنا"
                : $"🎓 اتحدّث من يوديمي: {lessons} درس جديد، و{watched} درس اتعلّم «اتشاف» من تقدّمك هناك",
            BannerKind.Success, seconds: 10);
    }

    private static async Task<string> GetUdemyJsonAsync(UdemyAccountForm form, string path, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            var (code, body) = await UdemyWeb.FetchAsync(form.Web, path, ct, 40);
            if (code == 200) return body;
            if (code is 401 or 403) throw new InvalidOperationException("يوديمي رفض الطلب (" + code + ") — جرّب تخرج وتدخل تاني");
            if (attempt >= 3) throw new InvalidOperationException("يوديمي رد بـ " + code);
            await Task.Delay(1500 * attempt, ct); // 429/5xx: نستنى ونعيد
        }
    }

    private static async Task<List<UdemyCourse>> FetchUdemyCoursesAsync(UdemyAccountForm form, CancellationToken ct)
    {
        var all = new List<UdemyCourse>();
        for (int page = 1; page <= 30; page++)
        {
            var (list, next) = Udemy.ParseSubscribed(await GetUdemyJsonAsync(form, Udemy.SubscribedApi(page), ct));
            foreach (var c in list) if (all.All(x => x.Id != c.Id)) all.Add(c);
            form.Status($"بجيب كورساتك… ({all.Count})");
            if (!next) break;
        }
        return all;
    }

    private static async Task<List<UdemyChapter>> FetchUdemyCurriculumAsync(UdemyAccountForm form, long courseId, CancellationToken ct)
    {
        var chapters = new List<UdemyChapter>();
        for (int page = 1; page <= 30; page++)
        {
            var (_, next) = Udemy.ParseCurriculum(await GetUdemyJsonAsync(form, Udemy.CurriculumApi(courseId, page), ct), chapters);
            if (!next) break;
        }
        return chapters;
    }

    private static async Task<HashSet<long>> FetchUdemyProgressAsync(UdemyAccountForm form, long courseId, CancellationToken ct)
    {
        try { return Udemy.ParseProgress(await GetUdemyJsonAsync(form, Udemy.ProgressApi(courseId), ct)); }
        catch (InvalidOperationException ex) { Log.Info("udemy progress: " + ex.Message); return new HashSet<long>(); } // التقدّم مش أساسي
    }

    /// <summary>اختيار الكورسات + الموضوع اللي هتتحط فيه.</summary>
    private (List<UdemyCourse> Courses, string Topic)? PickUdemyCourses(IWin32Window owner, List<UdemyCourse> all, ImportTarget? target = null)
    {
        var pick = PickPlatformCourses(owner, "يوديمي", "Udemy",
            all.Select(c => (c.Id, c.Title, c.Instructor, _library.Courses.Any(x => x.UdemyId == c.Id))).ToList(), "كورسات يوديمي",
            "والكورس جواه أقسام يوديمي كمحاضرات، والدروس كفيديوهات.", target);
        if (pick == null) return null;
        return (all.Where(c => pick.Value.Ids.Contains(c.Id)).ToList(), pick.Value.Topic);
    }



    private void UdemySignOut()
    {
        if (UdemyWeb.RuntimeVersion == null) { ShowStatus("مفيش جلسة يوديمي على الجهاز ده"); return; }
        if (!Dialogs.Confirm(this, "تخرج من حساب يوديمي على الجهاز ده؟\nالكورسات اللي استوردتها بتفضل في مكتبتك، ولما تشغّل درس هيطلب منك تدخل تاني.")) return;
        if (UdemyActive) StopPlayback();
        using var form = new UdemyAccountForm("يوديمي — خروج") { Size = new Size(520, 200) };
        form.Shown += async (_, _) =>
        {
            try
            {
                form.Status("بخرج…");
                await UdemyWeb.SignOutAsync(form.Web);
                ShowStatus("خرجت من حساب يوديمي على الجهاز ده ✓", 4);
            }
            catch (Exception ex) { Log.Error("udemy sign out", ex); ShowStatus("الخروج مانجحش: " + ex.Message, 6); }
            form.Close();
        };
        form.ShowDialog(this);
    }

}
