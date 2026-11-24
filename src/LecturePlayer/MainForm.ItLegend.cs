using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// IT Legend (itlegend.net): استيراد الكورس بمنهجه (الأقسام محاضرات، والدروس فيديوهات بمددها، واللي خلّصته «اتشاف»)،
/// والتشغيل بصفحة الكورس الحقيقية جوه مساحة الفيديو («مسار» بيختار الدرس من المنهج ويتحكم في مشغّل VdoCipher).
/// </summary>
internal sealed partial class MainForm
{
    private const string ItLegendPlatform = "IT Legend";

    private void ItLegendImport(Course? refresh = null)
    {
        if (UdemyWeb.RuntimeVersion == null) { ShowWebView2Missing(); return; }
        string? refreshId = refresh != null ? ItLegend.CourseIdOf(refresh.SourceUrl) : null;
        using var form = new ItLegendForm(refreshId);
        if (form.ShowDialog(this) != DialogResult.OK || form.Result is not { } r) return;
        ApplyItLegend(r.Course, r.Modules, refresh);
    }

    /// <summary>بيحط الكورس في المكتبة (أول مرة: يسألك على الموضوع) أو يحدّثه.</summary>
    private void ApplyItLegend(ItLegendCourse src, List<ItLegendModule> modules, Course? refresh, string? topicForTest = null)
    {
        string url = ItLegend.CourseUrl(src.Id);
        var course = refresh ?? _library.Courses.FirstOrDefault(c => ItLegend.CourseIdOf(c.SourceUrl) == src.Id);
        bool isNew = course == null;
        string? topicName = topicForTest;
        if (isNew && topicName == null)
        {
            var pick = PickPlatformCourses(this, ItLegendPlatform, ItLegendPlatform,
                new List<(long, string, string?, bool)> { (1, src.Title, src.Instructor, false) }, "كورسات IT Legend",
                "والأقسام محاضرات، والدروس فيديوهات بمددها، والامتحانات ❓ في مكانها.");
            if (pick == null) return;
            topicName = pick.Value.Topic;
        }
        Checkpoint((isNew ? "استيراد من " : "تحديث من ") + ItLegendPlatform);
        if (course == null)
        {
            var topic = _library.Topics.FirstOrDefault(t => string.Equals(t.Name, topicName, StringComparison.OrdinalIgnoreCase)) ?? NewTopic(topicName!);
            course = new Course { Name = src.Title };
            PlacePlatformCourse(course, topic, ItLegendPlatform, src.Instructor);
            _library.Courses.Add(course);
        }
        var sync = ItLegend.Apply(course, src, modules, firstImport: isNew);
        MarkDirty();
        RebuildTree();
        SelectItem(course);
        ShowBanner(isNew
                ? $"🎓 اتضاف «{course.Name}» من IT Legend ({course.VideoCount} درس، {course.AllVideos.Count(v => v.Watched)} منهم خلّصته) — دوس على أي درس يشتغل هنا"
                : $"🎓 اتحدّث من IT Legend: {sync.AddedItems} درس جديد، و{sync.MarkedWatched} اتعلّم «اتشاف» من تقدّمك هناك",
            BannerKind.Success, seconds: 12);
    }

    // ---------------- التشغيل ----------------

    private string? _itlPendingNode;
    private long _itlSelectStarted;
    private long _dueItlSelect;
    private int _itlSelectTries;
    private string? _itlLastSelect;
    private long _itlConfirmedAt;
    private long _itlStartMs;

    /// <summary>بعد ما صفحة الكورس تفتح: نختار الدرس من المنهج لحد ما الصفحة تطلبه من السيرفر.</summary>
    private void BeginItLegendSelect(VideoItem v)
    {
        _itlPendingNode = ItLegend.ParseLesson(v.Path)?.NodeId;
        _itlStartMs = v.LastPositionMs;
        _itlConfirmedAt = 0;
        if (_udemyPane != null) _udemyPane.HoldPending = true;
        _itlSelectStarted = Environment.TickCount64;
        _itlSelectTries = 0;
        _itlLastSelect = null;
        _dueItlSelect = 0;
    }

    /// <summary>طلبات الصفحة لـ api.itlegend.net: تأكيد اختيار الدرس، ولو اخترت درس تاني من جوه الصفحة «مسار» بيمشي معاك.</summary>
    private void OnItLegendApiRequest(string uri)
    {
        int i = uri.IndexOf("/videos/", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return;
        string node = uri[(i + 8)..].Split('?', '/')[0].ToLowerInvariant();
        BeginInvoke(() =>
        {
            if (node == _itlPendingNode)
            {
                _itlPendingNode = null;
                _itlConfirmedAt = Environment.TickCount64;
                Log.Info($"itlegend: lesson selected after {_itlSelectTries} tries ({_itlLastSelect})");
                return;
            }
            if (_itlPendingNode != null || _currentVideo is not { IsItLegend: true } cur) return;
            if (ItLegend.ParseLesson(cur.Path)?.NodeId == node) return;
            // إنت اخترت درس تاني من منهج الصفحة
            var course = _library.FindCourseOf(cur);
            var target = course?.AllVideos.FirstOrDefault(x => x.SourceRef == ItLegend.NodeRef(node));
            if (target != null && _library.FindGroupOf(target) is { } g) FollowWebLesson(cur, g, target);
        });
    }

    /// <summary>من تايمر الواجهة: محاولات اختيار الدرس (كل ~0.8 ثانية لحد 20 ثانية).</summary>
    private async void ItLegendSelectTick(VideoItem v)
    {
        // اتأكد الاختيار: نستنى المشغّل الجديد يتحمّل، وبعدين نرجّع مكانك وسرعتك عليه
        if (_itlPendingNode == null && _itlConfirmedAt > 0 && _udemyPane is { HoldPending: true } p && Environment.TickCount64 - _itlConfirmedAt > 1500)
        {
            p.Retarget(_itlStartMs, _activeRate, _muted ? 0 : _settings.Volume);
            p.HoldPending = false;
            _itlConfirmedAt = 0;
            return;
        }
        if (_itlPendingNode == null || _udemyPane?.Web.CoreWebView2 == null || !Due(ref _dueItlSelect, 800)) return;
        if (Environment.TickCount64 - _itlSelectStarted > 20_000)
        {
            Log.Info($"itlegend: could not select «{v.Title}» ({_itlSelectTries} tries, last: {_itlLastSelect})");
            ShowStatus("مقدرتش أختار الدرس من المنهج لوحدي — اختاره من قايمة الصفحة", 8);
            _itlPendingNode = null;
            _udemyPane.HoldPending = false;
            return;
        }
        if (ItLegend.ParseLesson(v.Path) is not { } l) { _itlPendingNode = null; return; }
        _itlSelectTries++;
        try
        {
            string raw = await _udemyPane.Web.CoreWebView2.ExecuteScriptAsync(ItLegend.SelectScript(l));
            _itlLastSelect = UdemyWeb.Unwrap(raw) ?? raw;
        }
        catch (Exception ex) { _itlLastSelect = ex.Message; }
    }
}
