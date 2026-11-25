using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// جودة دروس المنصات (يوديمي، IT Legend، …):
///  • «أعلى جودة تلقائيًا»: بعد ما الدرس يشتغل كام ثانية، لو الفيديو أقل من الحد، «مسار» يختار أعلى جودة من قايمة ⚙ في مشغّل الموقع.
///    مرة واحدة لكل درس — لو إنت غيّرتها بإيدك بعد كده مانرجّعهاش.
///  • شارة الدقة الحقيقية جنب السرعة (1080p / 720p …) من الفيديو نفسه، وبالضغط عليها تختار الحد أو تعيد الاختيار.
/// </summary>
internal sealed partial class MainForm
{
    private readonly Label _qualityChip = new();
    private string? _qKey;
    private double _qPlayedSec;
    private long _qLastTick;
    private int _qAttempts, _qToken;
    private long _qNextTry;
    private bool _qDone, _qForced;

    /// <summary>ثواني تشغيل قبل ما نبص على الجودة (Auto بيطلع لوحده في الأول، فبنستناه).</summary>
    private const double QualityWaitSec = 4;

    private void SetupQualityChip(FlowLayoutPanel host)
    {
        _qualityChip.AutoSize = true;
        _qualityChip.Visible = false;
        _qualityChip.Cursor = Cursors.Hand;
        _qualityChip.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _qualityChip.ForeColor = Theme.Muted;
        _qualityChip.BackColor = Theme.PanelLight;
        _qualityChip.Padding = new Padding(6, 3, 6, 3);
        _qualityChip.Margin = new Padding(4, 9, 6, 4);
        _qualityChip.RightToLeft = RightToLeft.No;
        _qualityChip.Click += (_, _) => ShowQualityMenu();
        host.Controls.Add(_qualityChip);
        host.Controls.SetChildIndex(_qualityChip, 0);
    }

    /// <summary>من UdemyTick: الشارة + محاولة «أعلى جودة» مرة لكل درس.</summary>
    private void WebQualityTick(UdemyPane pane, UdemyPane.PlayerState s)
    {
        long now = Environment.TickCount64;
        string key = pane.Generation + "|" + s.Href;
        if (key != _qKey)
        {
            _qKey = key;
            _qPlayedSec = 0;
            _qAttempts = 0;
            _qNextTry = 0;
            _qDone = false;
            _qForced = false;
            _qLastTick = now;
        }
        double dt = Math.Clamp((now - _qLastTick) / 1000.0, 0, 1);
        _qLastTick = now;
        if (s.Playing) _qPlayedSec += dt;
        UpdateQualityChip(s);

        int cap = _settings.WebQualityCap;
        if (pane.HoldPending) return; // لسه بنختار الدرس: الفيديو الظاهر مش بتاعنا
        if (_qDone || !s.HasVideo || (cap <= 0 && !_qForced)) return;
        if (cap <= 0) cap = WebQuality.Max; // «اختار أعلى جودة دلوقتي» وإنت سايبها للموقع

        var q = s.Quality;
        if (_qAttempts > 0 && q != null && q.Token == _qToken)
        {
            if (q.Status == "run") return;
            if (q.Status == "ok") { _qDone = true; ReportQuality(q, cap); return; }
            if (_qAttempts >= 2)
            {
                _qDone = true;
                Log.Info($"web quality: no quality menu ({q.Status}{(q.Error != null ? ": " + q.Error : "")}) on {s.Href}");
                if (_qForced) ShowStatus("مالقيتش قايمة جودة في المشغّل ده — غيّرها من ⚙ في المشغّل نفسه", 5);
                return;
            }
        }
        if (!_qForced && _qPlayedSec < QualityWaitSec) return;
        // الفيديو أصلًا على الحد أو أعلى: مانفتحش القايمة على الفاضي (ماتعرفش تفرق بين Auto و1080p من برّه، بس النتيجة واحدة)
        int have = WebQuality.NominalHeight(s.VideoWidth, s.VideoHeight);
        if (!_qForced && _qAttempts == 0 && have > 0 && have >= Math.Min(cap, 1080)) { _qDone = true; return; }
        if (now < _qNextTry) return;
        _qAttempts++;
        _qToken++;
        _qNextTry = now + 6000;
        pane.ApplyBestQuality(cap, _qToken);
    }

    private void ReportQuality(UdemyPane.QualityResult q, int cap)
    {
        string picked = string.IsNullOrEmpty(q.Picked) ? "" : q.Picked;
        Log.Info($"web quality: {(q.Changed ? "picked" : q.Already ? "already" : "kept")} '{picked}' (max {q.Max}, list {string.Join(",", q.Available)}, cap {cap})");
        if (q.Changed || _qForced)
        {
            string top = q.Max > 0 && q.Max <= cap && q.Max < 1080 ? $" — أعلى جودة اترفع بيها الدرس ده {q.Max}p" : cap < WebQuality.Max && q.Max > cap ? $" (حدّك {cap}p)" : " (أعلى المتاح)";
            ShowStatus($"🎚️ الجودة: {picked}{top}", 5);
        }
    }

    private void UpdateQualityChip(UdemyPane.PlayerState? s)
    {
        bool show = UdemyActive && !_miniMode && s is { HasVideo: true, VideoHeight: > 0 };
        if (!show) { if (_qualityChip.Visible) _qualityChip.Visible = false; return; }
        int p = WebQuality.NominalHeight(s!.VideoWidth, s.VideoHeight);
        string text = WebQuality.Label(s.VideoWidth, s.VideoHeight);
        var color = p >= 1080 ? Theme.Success : p >= 720 ? Theme.Text : Theme.Folder;
        if (_qualityChip.Text != text) _qualityChip.Text = text;
        if (_qualityChip.ForeColor != color) _qualityChip.ForeColor = color;
        string tip = $"دقة الفيديو دلوقتي: {s.VideoWidth}×{s.VideoHeight}" +
                     (s.Quality is { Max: > 0 } q ? $"\nأعلى اختيار في المشغّل: {q.Max}p" : "") +
                     $"\nالجودة: {WebQuality.Caps[WebQuality.CapIndex(_settings.WebQualityCap)].Label} — دوس للتغيير";
        if (_toolTip.GetToolTip(_qualityChip) != tip) _toolTip.SetToolTip(_qualityChip, tip);
        if (!_qualityChip.Visible) _qualityChip.Visible = true;
    }

    private void ShowQualityMenu()
    {
        var m = new ContextMenuStrip { Renderer = new DarkRenderer(), RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        var s = _udemyPane?.State;
        if (s is { VideoHeight: > 0 })
        {
            string info = $"الفيديو دلوقتي: {s.VideoWidth}×{s.VideoHeight}" + (s.Quality is { Max: > 0 } q ? $"  •  أعلى اختيار: {q.Max}p" : "");
            m.Items.Add(new ToolStripMenuItem(info) { Enabled = false });
            m.Items.Add(new ToolStripSeparator());
        }
        m.Items.Add(new ToolStripMenuItem("🎚️  اختار أعلى جودة دلوقتي", null, (_, _) => ForceBestQuality()) { Enabled = UdemyActive });
        m.Items.Add(new ToolStripSeparator());
        foreach (var (cap, label) in WebQuality.Caps)
        {
            int c = cap;
            m.Items.Add(new ToolStripMenuItem(label, null, (_, _) => SetWebQualityCap(c)) { Checked = _settings.WebQualityCap == c });
        }
        m.Closed += (_, _) => BeginInvoke(() => m.Dispose());
        m.Show(_qualityChip, new Point(0, -m.PreferredSize.Height));
    }

    private void SetWebQualityCap(int cap)
    {
        if (_settings.WebQualityCap == cap) return;
        _settings.WebQualityCap = cap;
        SaveSettings();
        ShowStatus(cap <= 0 ? "الجودة بقت على اختيار الموقع (Auto)" : $"الجودة: {WebQuality.Caps[WebQuality.CapIndex(cap)].Label} — بتتطبّق على الدرس ده والجاي", 4);
        if (cap > 0) ForceBestQuality(quiet: true);
    }

    /// <summary>«اختار أعلى جودة دلوقتي»: من غير استنى ومن غير ما نبص على الدقة الحالية.</summary>
    private void ForceBestQuality(bool quiet = false)
    {
        if (!UdemyActive || _udemyPane?.State is not { HasVideo: true }) { if (!quiet) ShowStatus("شغّل درس من المنصة الأول", 3); return; }
        _qDone = false;
        _qForced = true;
        _qAttempts = 0;
        _qNextTry = 0;
        if (!quiet) ShowStatus("بختار أعلى جودة من قايمة المشغّل…", 3);
    }
}
