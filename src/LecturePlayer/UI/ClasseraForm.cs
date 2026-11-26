using LecturePlayer.Data;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LecturePlayer.UI;

/// <summary>دورة قُرئت من صفحة المنصة: المنهج، وما أكملته، والاسم، ورابط صفحة الدورة.</summary>
internal sealed record ClasseraImport(ClasseraCourse Course, ClasseraProgress? Progress, string Title, string CourseUrl);

/// <summary>
/// شباك «منصة الإبداع» جوه «مسار»: تدخل بحسابك (الجلسة تُحفظ في بروفايل البرنامج على جهازك مثل أي متصفح)،
/// وتفتح دورتك من «رحلتي التعليمية»، وتضغط «استورد هذه الدورة». «مسار» لا يطلب من سيرفر المنصة شيئًا بنفسه:
/// يقرأ الردود التي وصلت للصفحة وهي تعرض الدورة (المنهج وما أكملته). يمكنك استيراد أكثر من دورة قبل إغلاق النافذة.
/// </summary>
internal sealed class ClasseraForm : Form
{
    public WebView2 Web { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Back };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0), TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Theme.Text, Font = new Font("Segoe UI", 10f)
    };
    private readonly Button _import = new() { Text = "⬇  استورد هذه الدورة", Dock = DockStyle.Left, Width = 200, Enabled = false, FlatStyle = FlatStyle.Flat };
    private readonly string? _refreshLecture;
    private readonly string _start;
    private readonly Dictionary<string, ClasseraCourse> _lessons = new();
    private readonly Dictionary<string, ClasseraProgress> _progress = new();
    private readonly List<ClasseraEnrolled> _enrolled = new();
    private readonly HashSet<string> _imported = new();
    private bool _busy;

    /// <summary>كل دورة تُستورد (النافذة تبقى مفتوحة لدورة أخرى، إلا في وضع التحديث).</summary>
    public event Action<ClasseraImport>? Imported;
    /// <summary>وضع التحديث: الدورة التي قُرئت.</summary>
    public ClasseraImport? Result { get; private set; }
    public int ImportedCount => _imported.Count;

    internal static Action<CoreWebView2>? SmokeSetup;

    /// <param name="refreshCourseUrl">تحديث دورة موجودة: يفتح صفحتها ويقرؤها وحده ثم يُغلق.</param>
    public ClasseraForm(string? refreshCourseUrl = null, string? startUrl = null)
    {
        _refreshLecture = Classera.CourseLectureOf(refreshCourseUrl);
        _start = startUrl ?? refreshCourseUrl ?? (Classera.TestHost != null ? Classera.SiteBase + "/mylearning?tab=courses" : Classera.MyLearning);
        Text = _refreshLecture != null ? Classera.Platform + " — تحديث الدورة" : Classera.Platform + " — دوراتي";
        Icon = Theme.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1200, 820);
        MinimumSize = new Size(760, 520);
        BackColor = Theme.Window;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        _import.FlatAppearance.BorderSize = 0;
        _import.BackColor = Theme.Accent;
        _import.ForeColor = Color.White;
        _import.Click += async (_, _) => await ImportAsync();
        var bar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Panel, Padding = new Padding(6) };
        bar.Controls.Add(_status);
        bar.Controls.Add(_import);
        Controls.Add(Web);
        Controls.Add(bar);
        Status(_refreshLecture != null ? "جارٍ فتح الدورة…" : "سجّل الدخول بحسابك، ثم افتح الدورة من «رحلتي التعليمية» واضغط «استورد هذه الدورة».");
        Shown += async (_, _) => await StartAsync();
    }

    private void Status(string s) { if (!IsDisposed) _status.Text = s; }

    private async Task StartAsync()
    {
        try
        {
            await UdemyWeb.InitAsync(Web);
            var core = Web.CoreWebView2;
            core.WebResourceResponseReceived += OnResponse;
            core.SourceChanged += (_, _) => UpdateButton();
            core.NavigationCompleted += (_, _) => UpdateButton();
            core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); };
            SmokeSetup?.Invoke(core);
            core.Navigate(_start);
        }
        catch (Exception ex)
        {
            Log.Error("classera window", ex);
            Status("WebView2 لا يعمل: " + ex.Message);
        }
    }

    private async void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            string uri = e.Request.Uri;
            var kind = Classera.KindOf(uri);
            if (kind == Classera.Api.None || e.Response.StatusCode != 200) return;
            string? page = Web.CoreWebView2?.Source;
            using var stream = await e.Response.GetContentAsync();
            if (stream == null || IsDisposed) return;
            using var reader = new StreamReader(stream);
            string body = await reader.ReadToEndAsync();
            switch (kind)
            {
                case Classera.Api.Lessons when Classera.ParseLessons(body) is { } c:
                    _lessons[c.LectureId] = c;
                    break;
                case Classera.Api.Progress when Classera.ParseProgress(body) is { } p && Classera.CourseLectureOf(page) is { } lec:
                    _progress[lec] = p; // الرد لا يذكر الدورة: هو للدورة المفتوحة في الصفحة
                    break;
                case Classera.Api.Enrolled:
                    foreach (var x in Classera.ParseEnrolled(body))
                        if (_enrolled.All(y => y.LectureId != x.LectureId)) _enrolled.Add(x);
                    break;
            }
            UpdateButton();
            if (_refreshLecture != null && !_busy && Result == null && _lessons.ContainsKey(_refreshLecture)) _ = ImportAsync();
        }
        catch (Exception ex) { Log.Info("classera response: " + ex.Message); }
    }

    private string? OpenLecture => Classera.CourseLectureOf(Web.CoreWebView2?.Source);

    private void UpdateButton()
    {
        if (IsDisposed) return;
        string? src = Web.CoreWebView2?.Source;
        string? lec = OpenLecture;
        bool ready = lec != null && _lessons.ContainsKey(lec);
        _import.Enabled = !_busy && ready;
        _import.Text = lec != null && _imported.Contains(lec) ? "⬇  حدّث هذه الدورة" : "⬇  استورد هذه الدورة";
        if (_busy) return;
        if (src != null && src.Contains("/login", StringComparison.OrdinalIgnoreCase))
            Status("سجّل الدخول بحساب المنصة (يُحفظ الدخول على هذا الجهاز كما في المتصفح).");
        else if (lec == null)
            Status(_imported.Count > 0
                ? $"✓ استوردت {_imported.Count} دورة — افتح دورة أخرى من «رحلتي التعليمية» لاستيرادها، أو أغلق النافذة."
                : "افتح الدورة التي تريدها من «رحلتي التعليمية»، ثم اضغط «استورد هذه الدورة».");
        else if (!ready) Status("جارٍ قراءة الدورة…");
        else if (_imported.Contains(lec))
            Status("✓ استوردت هذه الدورة — افتح دورة أخرى من «رحلتي التعليمية» لاستيرادها، أو أغلق النافذة.");
        else
        {
            var c = _lessons[lec];
            int n = c.Lessons.Sum(l => l.Items.Count);
            Status($"✓ الدورة مفتوحة: {c.Lessons.Count(l => l.Items.Count > 0)} درس و{n} عنصر (فيديو، ملف، اختبار…) — اضغط «استورد هذه الدورة».");
        }
    }

    private async Task ImportAsync()
    {
        string? lec = _refreshLecture ?? OpenLecture;
        if (_busy || lec == null || !_lessons.TryGetValue(lec, out var course)) return;
        _busy = true;
        _import.Enabled = false;
        try
        {
            Status("جارٍ قراءة ما أكملته…");
            // ردّ التقدّم يصل مع المنهج أو بعده بلحظة
            for (int i = 0; i < 40 && !_progress.ContainsKey(lec) && !IsDisposed; i++) await Task.Delay(100);
            if (IsDisposed) return;
            _progress.TryGetValue(lec, out var progress);
            if (progress == null) Log.Info("classera: no progress response for " + lec);
            string src = Web.CoreWebView2?.Source ?? "";
            string title = _enrolled.FirstOrDefault(e => e.LectureId == lec)?.Title
                           ?? Classera.CourseTitleOf(src)
                           ?? "دورة من " + Classera.Platform;
            string courseUrl = Classera.CourseLectureOf(src) == lec ? src : "";
            var result = new ClasseraImport(course, progress, title, courseUrl);
            _imported.Add(lec);
            Log.Info($"classera: «{title}» {course.Lessons.Count} lessons, {course.Lessons.Sum(l => l.Items.Count)} items, done {progress?.Completed.Count ?? 0}");
            if (_refreshLecture != null)
            {
                Result = result;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            Imported?.Invoke(result);
        }
        catch (Exception ex)
        {
            Log.Error("classera import", ex);
            Status("⚠ " + ex.Message);
        }
        finally
        {
            _busy = false;
            UpdateButton();
        }
    }
}
