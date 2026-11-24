using LecturePlayer.Data;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LecturePlayer.UI;

/// <summary>
/// شباك IT Legend جوه «مسار»: بتدخل بالإيميل والباسورد (الجلسة بتتحفظ في بروفايل البرنامج على جهازك)،
/// وتفتح الكورس، وتدوس «استورد الكورس ده». المنهج بيتقري من نفس واجهة الموقع بتوكن جلستك اللي الصفحة نفسها بتستخدمه
/// (بيفضل في الذاكرة بس ومابيتحفظش).
/// </summary>
internal sealed class ItLegendForm : Form
{
    public WebView2 Web { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Back };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0), TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Theme.Text, Font = new Font("Segoe UI", 10f)
    };
    private readonly Button _import = new() { Text = "⬇  استورد الكورس ده", Dock = DockStyle.Left, Width = 190, Enabled = false, FlatStyle = FlatStyle.Flat };
    private readonly string? _refreshCourseId;
    private readonly string _start;
    private string? _bearer;
    private bool _busy;

    /// <summary>النتيجة: الكورس ومنهجه (لو اتستورد).</summary>
    public (ItLegendCourse Course, List<ItLegendModule> Modules)? Result { get; private set; }

    internal static Action<CoreWebView2>? SmokeSetup;

    /// <param name="refreshCourseId">تحديث كورس موجود: بيفتحه ويقراه لوحده أول ما الدخول يبقى جاهز.</param>
    public ItLegendForm(string? refreshCourseId = null, string? startUrl = null)
    {
        _refreshCourseId = refreshCourseId;
        _start = startUrl ?? (refreshCourseId != null ? ItLegend.CourseUrl(refreshCourseId) : ItLegend.LearnPath);
        Text = refreshCourseId != null ? "IT Legend — تحديث الكورس" : "IT Legend — كورساتي";
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
        Status(refreshCourseId != null ? "بفتح الكورس…" : "ادخل بالإيميل والباسورد، وافتح الكورس اللي عايزه، ودوس «استورد الكورس ده».");
        Shown += async (_, _) => await StartAsync();
    }

    private void Status(string s) { if (!IsDisposed) _status.Text = s; }

    private async Task StartAsync()
    {
        try
        {
            await UdemyWeb.InitAsync(Web);
            var core = Web.CoreWebView2;
            // توكن الجلسة من طلبات الصفحة نفسها لـ api.itlegend.net
            core.AddWebResourceRequestedFilter((ItLegendClient.TestApi ?? ItLegend.Api) + "/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                try
                {
                    string auth = e.Request.Headers.GetHeader("Authorization");
                    if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && auth.Length > 20)
                    {
                        bool first = _bearer == null;
                        _bearer = auth;
                        if (first) BeginInvoke(OnSignedIn);
                    }
                }
                catch (Exception) { } // الهيدر مش موجود
            };
            core.SourceChanged += (_, _) => UpdateButton();
            core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); };
            SmokeSetup?.Invoke(core);
            core.Navigate(_start);
        }
        catch (Exception ex)
        {
            Log.Error("itlegend window", ex);
            Status("WebView2 مش شغال: " + ex.Message);
        }
    }

    private void OnSignedIn()
    {
        UpdateButton();
        if (_refreshCourseId != null && !_busy) _ = ImportAsync();
    }

    private string? OpenCourseId => ItLegend.CourseIdOf(Web.CoreWebView2?.Source) ?? _refreshCourseId;

    private void UpdateButton()
    {
        if (IsDisposed) return;
        string? id = ItLegend.CourseIdOf(Web.CoreWebView2?.Source);
        _import.Enabled = !_busy && _bearer != null && id != null;
        if (_busy) return;
        if (_bearer == null) Status("ادخل بالإيميل والباسورد (لو حسابك بجوجل: اعمل «نسيت كلمة المرور؟» مرة واحدة يبقى ليه باسورد).");
        else if (id == null) Status("✓ داخل — افتح الكورس اللي عايزه (من «مساراتي»)، وبعدين دوس «استورد الكورس ده».");
        else Status("✓ الكورس مفتوح — دوس «استورد الكورس ده».");
    }

    private async Task ImportAsync()
    {
        string? id = OpenCourseId;
        if (_busy || _bearer == null || id == null) return;
        _busy = true;
        _import.Enabled = false;
        try
        {
            Status("بقرا الكورس والمنهج…");
            using var client = new ItLegendClient(_bearer);
            var course = ItLegend.ParseCourse(await client.GetAsync($"/MemberCoursePlayer/{id}", CancellationToken.None))
                         ?? throw new InvalidOperationException("مش لاقي بيانات الكورس — اتأكد إنك مشترك فيه");
            var modules = ItLegend.ParseNodes(await client.GetAsync($"/CourseNode/{id}/nodes", CancellationToken.None));
            if (modules.Count == 0) throw new InvalidOperationException("المنهج فاضي أو مقفول عليك");
            Log.Info($"itlegend: «{course.Title}» {modules.Count} modules, {modules.Sum(m => m.Nodes.Count)} nodes");
            Result = (course, modules);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("itlegend import", ex);
            Status("⚠ " + ex.Message);
            if (ex is UnauthorizedAccessException) _bearer = null;
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) _import.Enabled = _bearer != null && ItLegend.CourseIdOf(Web.CoreWebView2?.Source) != null;
        }
    }
}
