using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// منصة الإبداع في التعليم الإلكتروني (كلاسيرا): استيراد دوراتك بكل ما فيها (فيديوهات، ملفات، مواقع، اختبارات، استبيانات)،
/// والتشغيل بصفحة المنصة نفسها جوه «مسار» فيُحسب حضورك وتقدّمك للشهادة، و«مسار» يتابع كل عنصر:
/// مكانك في الفيديو، وما فتحته من ملفات، ومحاولات الاختبار ودرجته، وما أكملته على المنصة.
/// </summary>
internal sealed partial class MainForm
{
    private long _clsOpenedTick;
    /// <summary>مدة فتح ملف أو موقع ليُعدّ «اطّلعت عليه».</summary>
    internal static int ClasseraSeenMs = 15_000;
    private VideoItem? _clsOpenedItem;

    private void ClasseraImport(Course? refresh = null, ImportTarget? target = null)
    {
        if (UdemyWeb.RuntimeVersion == null) { ShowWebView2Missing(); return; }
        if (refresh != null)
        {
            using var rf = new ClasseraForm(refresh.SourceUrl);
            if (rf.ShowDialog(this) == DialogResult.OK && rf.Result is { } r) ApplyClassera(r, refresh, null, null);
            return;
        }
        string? topicName = null;
        bool asked = false;
        using var form = new ClasseraForm();
        form.Imported += r =>
        {
            bool exists = FindClasseraCourse(r.Course.LectureId) != null;
            if (!exists && !asked && target is not ({ Topic: not null } or { Place: not null }))
            {
                var pick = PickPlatformCourses(form, Classera.Platform, Classera.Platform,
                    new List<(long, string, string?, bool)> { (1, r.Title, null, false) }, "دورات " + Classera.Platform,
                    "والدروس محاضرات، وفيها كل عنصر بترتيبه: الفيديوهات، والملفات 📄، والمواقع 🌐، والاختبارات 📝، والاستبيانات 📋.");
                if (pick == null) return;
                topicName = pick.Value.Topic;
                asked = true;
            }
            ApplyClassera(r, null, topicName, target);
        };
        form.ShowDialog(this);
    }

    private Course? FindClasseraCourse(string lectureId) =>
        _library.Courses.FirstOrDefault(c => Classera.CourseLectureOf(c.SourceUrl) == lectureId);

    /// <summary>بيضع الدورة في المكتبة أو يحدّثها، ويعلّم ما أكملته على المنصة.</summary>
    private void ApplyClassera(ClasseraImport r, Course? refresh, string? topicName, ImportTarget? target)
    {
        var course = refresh ?? FindClasseraCourse(r.Course.LectureId);
        bool isNew = course == null;
        Checkpoint((isNew ? "استيراد من " : "تحديث من ") + Classera.Platform);
        if (course == null)
        {
            course = new Course { Name = r.Title };
            PlaceImportedCourse(course, target, null, topicName ?? "دورات " + Classera.Platform, Classera.Platform, null);
            _library.Courses.Add(course);
        }
        string url = r.CourseUrl.Length > 0 ? r.CourseUrl : course.SourceUrl ?? "";
        var sync = Classera.Apply(course, r.Course, r.Progress, url, firstImport: isNew);
        MarkDirty();
        RebuildTree();
        SelectItem(course);
        string pct = r.Progress != null ? $" · تقدّمك على المنصة {Math.Round(r.Progress.Percent, 1)}%" + (r.Progress.Passed ? " (ناجح ✓)" : "") : "";
        ShowBanner(isNew
                ? $"🎓 أُضيف «{course.Name}» من {Classera.Platform}: {sync.Total} عنصر (فيديوهات وملفات واختبارات)، أكملت منها {sync.Done}{pct} — اضغط أي عنصر ليُفتح هنا ويُحسب على المنصة"
                : $"🎓 حُدِّث من {Classera.Platform}: {sync.AddedItems} عنصر جديد، و{sync.MarkedDone} أُنجز حسب المنصة — أكملت {sync.Done} من {sync.Total}{pct}",
            BannerKind.Success, seconds: 14);
    }

    // =====================================================================
    //  أثناء العمل: «مسار» يقرأ ما تعرضه الصفحة
    // =====================================================================

    /// <summary>عنصر من المنصة فُتح في مساحة الفيديو.</summary>
    private void BeginClasseraItem(VideoItem v)
    {
        _clsOpenedItem = v;
        _clsOpenedTick = Environment.TickCount64;
    }

    /// <summary>من تايمر الواجهة: ملف أو موقع فتحته 15 ثانية = «اطّلعت عليه» (والمنصة هي الحكم عند المزامنة).</summary>
    private void ClasseraTick(VideoItem v, bool hasVideo)
    {
        if (hasVideo || v != _clsOpenedItem || v.Watched || v.SourceRef?.StartsWith("cls-att:", StringComparison.Ordinal) != true || v.PlatformInfo == null) return;
        if (Environment.TickCount64 - _clsOpenedTick < ClasseraSeenMs) return;
        MarkUdemyWatched(v);
    }

    /// <summary>انتقلت من الصفحة إلى عنصر آخر («المحتويات التالية»): «مسار» يجعله الحالي في الشجرة.</summary>
    private bool FollowClasseraPage(VideoItem from, string href)
    {
        if (Classera.ItemIdOf(href) is not { } id || id == Classera.ItemIdOf(from.Path)) return false;
        string sref = (Classera.IsAssignmentUrl(href) ? "cls-asg:" : "cls-att:") + id;
        var course = _library.FindCourseOf(from);
        var target = course?.AllVideos.FirstOrDefault(x => x.SourceRef == sref);
        if (target == null || _library.FindGroupOf(target) is not { } g) return false;
        FollowWebLesson(from, g, target);
        BeginClasseraItem(target);
        return true;
    }

    /// <summary>ردود المنصة للصفحة المفتوحة: نتيجة الاختبار، والعناصر المكتملة، وأي عنصر جديد في المنهج.</summary>
    private void OnClasseraResponse(string uri, string body)
    {
        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            try
            {
                var cur = _currentVideo;
                var course = cur != null && cur.IsClassera ? _library.FindCourseOf(cur) : null;
                if (course == null) return;
                switch (Classera.KindOf(uri))
                {
                    case Classera.Api.AssignmentInfo when Classera.ParseAssignmentInfo(body) is { } info:
                        var item = course.AllVideos.FirstOrDefault(x => x.SourceRef == "cls-asg:" + info.Id);
                        if (item == null) return;
                        string text = Classera.AssignmentText(info);
                        if (item.PlatformInfo != text) { item.PlatformInfo = text; item.Touch(); MarkDirty(); }
                        if (info.Submissions > 0 && !item.Watched) MarkUdemyWatched(item);
                        RefreshVideo(item);
                        break;
                    case Classera.Api.Lessons when Classera.ParseLessons(body) is { } src && Classera.CourseLectureOf(course.SourceUrl) == src.LectureId:
                        var sync = Classera.Apply(course, src, null, course.SourceUrl ?? "", firstImport: false);
                        if (sync.AddedItems > 0)
                        {
                            MarkDirty();
                            RebuildTree();
                            ShowStatus($"🎓 {sync.AddedItems} عنصر جديد في «{course.Name}» على {Classera.Platform}", 6);
                        }
                        break;
                    case Classera.Api.Progress when Classera.ParseProgress(body) is { } p
                                                  && Classera.CourseLectureOf(_udemyPane?.Web.CoreWebView2?.Source) is { } lec
                                                  && Classera.CourseLectureOf(course.SourceUrl) == lec:
                        int marked = 0;
                        foreach (var v in course.AllVideos)
                            if (!v.Watched && v.SourceRef is { } sr && sr.StartsWith("cls-", StringComparison.Ordinal) && p.Completed.Contains(sr[(sr.IndexOf(':') + 1)..]))
                            { v.Watched = true; v.Touch(); marked++; RefreshVideo(v); }
                        if (marked > 0) { MarkDirty(); RefreshCourse(course); }
                        break;
                }
            }
            catch (Exception ex) { Log.Info("classera live: " + ex.Message); }
        });
    }
}
