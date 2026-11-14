using LecturePlayer.UI;

namespace LecturePlayer;

internal enum BannerKind { Info, Success, Warning, Error }

/// <summary>
/// شريط التنبيهات فوق الفيديو: للرسايل المهمة اللي المستخدم لازم يشوفها
/// (بدل شريط الحالة تحت، اللي محدش بيبص عليه). فيه زرار إجراء اختياري وزرار قفل.
/// </summary>
internal sealed partial class MainForm
{
    private Panel? _banner;
    private Label _bannerIcon = null!, _bannerText = null!;
    private PillButton _bannerAction = null!;
    private Action? _bannerActionHandler;
    private readonly System.Windows.Forms.Timer _bannerTimer = new();
    private string? _bannerKey;

    private void BuildBanner()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top, Height = LogicalToDeviceUnits(46), Visible = false,
            BackColor = Theme.PanelLight, Padding = new Padding(LogicalToDeviceUnits(10), 0, LogicalToDeviceUnits(4), 0),
            RightToLeft = RightToLeft.Yes
        };
        _bannerIcon = new Label
        {
            Dock = DockStyle.Right, Width = LogicalToDeviceUnits(36), Font = Glyphs.Font(14f),
            TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent
        };
        _bannerText = new Label
        {
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 10f),
            TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, BackColor = Color.Transparent,
            RightToLeft = RightToLeft.Yes, UseMnemonic = false
        };
        var close = new GlyphButton("", 10f)
        {
            Dock = DockStyle.Left, Width = LogicalToDeviceUnits(36), BackColor = Color.Transparent
        };
        _toolTip.SetToolTip(close, "قفل");
        close.Click += (_, _) => HideBanner();
        _bannerAction = new PillButton("", PillStyle.Primary) { Visible = false };
        var actionHost = new Panel
        {
            Dock = DockStyle.Left, Width = LogicalToDeviceUnits(10), BackColor = Color.Transparent,
            Padding = new Padding(0, LogicalToDeviceUnits(7), LogicalToDeviceUnits(10), LogicalToDeviceUnits(7))
        };
        _bannerAction.Dock = DockStyle.Fill;
        actionHost.Controls.Add(_bannerAction);
        _bannerAction.Click += (_, _) =>
        {
            var h = _bannerActionHandler;
            HideBanner();
            h?.Invoke();
        };

        // الترتيب مهم مع Dock: الـ Fill الأول في الإضافة
        bar.Controls.Add(_bannerText);
        bar.Controls.Add(_bannerIcon);
        bar.Controls.Add(actionHost);
        bar.Controls.Add(close);
        bar.Paint += (_, e) =>
        {
            // خط ملوّن على الطرف بلون نوع التنبيه
            using var b = new SolidBrush(_bannerIcon.ForeColor);
            e.Graphics.FillRectangle(b, bar.Width - LogicalToDeviceUnits(4), 0, LogicalToDeviceUnits(4), bar.Height);
        };
        _bannerTimer.Tick += (_, _) => HideBanner();

        _videoArea.Controls.Add(bar);
        bar.SendToBack(); // يتحسب الأول في الـ Dock فياخد الحتة اللي فوق، والفيديو تحته
        _banner = bar;
    }

    /// <param name="seconds">0 = يفضل لحد ما المستخدم يقفله أو يتشال من الكود</param>
    /// <param name="key">اسم للتنبيه عشان نقدر نشيله بعدين (HideBanner(key))</param>
    private void ShowBanner(string text, BannerKind kind = BannerKind.Info, string? actionText = null, Action? action = null,
        int seconds = 10, string? key = null)
    {
        if (_banner == null || IsDisposed) { ShowStatus(text, Math.Max(seconds, 6)); return; }
        (string glyph, Color color) = kind switch
        {
            BannerKind.Success => ("", Theme.Success),
            BannerKind.Warning => ("", Theme.Folder),
            BannerKind.Error => ("", Theme.Danger),
            _ => ("", Theme.Accent)
        };
        _bannerKey = key;
        _bannerIcon.Text = glyph;
        _bannerIcon.ForeColor = color;
        _bannerText.Text = text.Replace("\n", "  ·  ");
        _toolTip.SetToolTip(_bannerText, text);

        _bannerActionHandler = action;
        _bannerAction.Visible = actionText != null && action != null;
        var host = _bannerAction.Parent!;
        if (_bannerAction.Visible)
        {
            _bannerAction.Text = actionText!;
            _bannerAction.AutoSizeToText();
            host.Width = _bannerAction.Width + host.Padding.Horizontal + LogicalToDeviceUnits(8);
        }
        else host.Width = LogicalToDeviceUnits(4);

        _banner.Visible = true;
        _banner.Invalidate();
        _bannerTimer.Stop();
        if (seconds > 0)
        {
            _bannerTimer.Interval = seconds * 1000;
            _bannerTimer.Start();
        }
    }

    private void HideBanner(string? key = null)
    {
        if (_banner == null) return;
        if (key != null && _bannerKey != key) return; // التنبيه اللي ظاهر دلوقتي مش هو اللي عايزين نشيله
        _bannerTimer.Stop();
        _banner.Visible = false;
        _bannerActionHandler = null;
        _bannerKey = null;
    }
}
