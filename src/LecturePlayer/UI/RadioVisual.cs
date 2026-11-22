using System.Drawing.Drawing2D;

namespace LecturePlayer.UI;

/// <summary>
/// شاشة الراديو: بتظهر مكان الفيديو وإنت بتسمع محطة (أو قناة لسه بتتصل / مفيهاش صورة)،
/// عشان يبقى واضح إيه اللي شغال وإن الصوت طالع من البرنامج: اللوجو، الاسم، «مباشر»،
/// اللي بيتذاع دلوقتي، وأعمدة صوت متحركة.
/// </summary>
internal sealed class RadioVisual : Control
{
    public enum Status { Connecting, Playing, Paused, Reconnecting }

    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 50 };
    private readonly float[] _bars = new float[36];
    private readonly float[] _targets = new float[36];
    private readonly Random _rnd = new();
    private int _frame;
    private Status _status = Status.Connecting;
    private string _station = "", _subtitle = "", _nowPlaying = "";
    private bool _isTv;
    private Image? _logo;
    private DateTime _startedUtc = DateTime.UtcNow;

    public RadioVisual()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
        _anim.Tick += (_, _) => Animate();
        // الشباك متصغّر: مفيش حد شايف الموجات، فمانصحّيش المعالج 20 مرة في الثانية عشان نرسمها.
        // الشباك بيتلاقي متأخر: لما الكنترول بيتضاف لأبوه، أبوه نفسه لسه ممكن يكون مش جوه الشباك.
        VisibleChanged += (_, _) => UpdateAnimation();
        ParentChanged += (_, _) => UpdateAnimation();
        HandleCreated += (_, _) => UpdateAnimation();
    }

    private Form? _hookedForm;
    private void OnFormResize(object? sender, EventArgs e) => UpdateAnimation();

    /// <summary>الموجات بتتحرك بس لما تكون باينة فعلًا.</summary>
    internal bool AnimationRunning => _anim.Enabled;

    private void UpdateAnimation()
    {
        var f = FindForm();
        if (!ReferenceEquals(f, _hookedForm))
        {
            if (_hookedForm != null) _hookedForm.Resize -= OnFormResize;
            _hookedForm = f;
            if (f != null) f.Resize += OnFormResize;
        }
        _anim.Enabled = Visible && f is not { WindowState: FormWindowState.Minimized };
    }

    /// <summary>محطة جديدة.</summary>
    public void Start(string station, string subtitle, bool isTv)
    {
        _station = station;
        _subtitle = subtitle;
        _isTv = isTv;
        _nowPlaying = "";
        _status = Status.Connecting;
        _startedUtc = DateTime.UtcNow;
        SetLogo(null);
        Invalidate();
    }

    public void SetLogo(Image? logo)
    {
        var old = _logo;
        _logo = logo;
        if (!ReferenceEquals(old, logo)) old?.Dispose();
        Invalidate();
    }

    public void SetNowPlaying(string? text)
    {
        _nowPlaying = text ?? "";
        Invalidate();
    }

    public void SetStatus(Status s)
    {
        if (_status == s) return;
        _status = s;
        Invalidate();
    }

    private void Animate()
    {
        _frame++;
        for (int i = 0; i < _bars.Length; i++)
        {
            float target;
            switch (_status)
            {
                case Status.Playing:
                    // أعلى في النص زي شكل الصوت الحقيقي، ومتغيرة كل شوية
                    if (_frame % 3 == 0 || _targets[i] == 0)
                    {
                        double center = 1 - Math.Abs(i - _bars.Length / 2.0) / (_bars.Length / 2.0);
                        _targets[i] = (float)(0.15 + 0.85 * _rnd.NextDouble() * (0.45 + 0.55 * center));
                    }
                    target = _targets[i];
                    break;
                case Status.Paused:
                    target = 0.06f;
                    break;
                default: // بيتصل: موجة ماشية
                    target = (float)(0.12 + 0.25 * (1 + Math.Sin(_frame * 0.25 - i * 0.45)) / 2);
                    break;
            }
            _bars[i] += (target - _bars[i]) * (_status == Status.Playing ? 0.45f : 0.2f);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var rc = ClientRectangle;
        if (rc.Width < 10 || rc.Height < 10) return;
        float f = DeviceDpi / 96f;
        bool small = rc.Height < 330 * f; // المشغل الصغير

        using (var bg = new LinearGradientBrush(rc, Color.FromArgb(16, 26, 44), Color.FromArgb(8, 8, 10), LinearGradientMode.Vertical))
            g.FillRectangle(bg, rc);
        // هالة زرقا خفيفة ورا اللوجو
        int glow = (int)(Math.Min(rc.Width, rc.Height) * 0.9);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(rc.Width / 2 - glow / 2, (int)(rc.Height * 0.36) - glow / 2, glow, glow);
            using var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(55, 0, 120, 212), SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) } };
            g.FillPath(pg, path);
        }

        int logoSize = (int)((small ? 64 : 132) * f);
        int y = small ? (int)(14 * f) : Math.Max((int)(20 * f), (int)(rc.Height * 0.36) - logoSize / 2 - (int)(40 * f));
        var logoRect = new Rectangle(rc.Width / 2 - logoSize / 2, y, logoSize, logoSize);
        DrawLogo(g, logoRect, f);
        y = logoRect.Bottom + (int)(14 * f);

        // الاسم
        using (var nameFont = new Font("Segoe UI", small ? 14f : 24f, FontStyle.Bold))
        {
            int h = TextRenderer.MeasureText(g, "Ag", nameFont).Height;
            TextRenderer.DrawText(g, _station, nameFont, new Rectangle(20, y, rc.Width - 40, h), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            y += h + (int)(4 * f);
        }

        // «مباشر» + الحالة
        string state = _status switch
        {
            Status.Connecting => "بيتصل…",
            Status.Reconnecting => "بيعيد الاتصال…",
            Status.Paused => "متوقف مؤقتًا",
            _ => "مباشر"
        };
        using (var badgeFont = new Font("Segoe UI", small ? 8.5f : 10f, FontStyle.Bold))
        {
            var sz = TextRenderer.MeasureText(g, state, badgeFont);
            int bw = sz.Width + (int)(30 * f), bh = sz.Height + (int)(6 * f);
            var badge = new Rectangle(rc.Width / 2 - bw / 2, y, bw, bh);
            Color badgeColor = _status switch
            {
                Status.Playing => Color.FromArgb(200, 40, 40),
                Status.Paused => Color.FromArgb(70, 70, 76),
                _ => Color.FromArgb(150, 110, 30)
            };
            Modern.FillRound(g, badgeColor, badge, bh / 2f);
            // النقطة بتنبض وهو شغال
            int dot = (int)(7 * f);
            int alpha = _status == Status.Playing ? 150 + (int)(105 * (0.5 + 0.5 * Math.Sin(_frame * 0.2))) : 255;
            using (var db = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                g.FillEllipse(db, badge.Right - (int)(12 * f) - dot, badge.Top + bh / 2 - dot / 2, dot, dot);
            TextRenderer.DrawText(g, state, badgeFont, new Rectangle(badge.Left + (int)(6 * f), badge.Top, badge.Width - (int)(22 * f), bh), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            y = badge.Bottom + (int)(10 * f);
        }

        // اللي بيتذاع دلوقتي / البلد والنوع
        if (!small || _nowPlaying.Length > 0)
        {
            string line = _nowPlaying.Length > 0 ? "🎵  " + _nowPlaying : _subtitle;
            if (line.Length > 0)
            {
                using var f2 = new Font("Segoe UI", small ? 9f : 12.5f, _nowPlaying.Length > 0 ? FontStyle.Bold : FontStyle.Regular);
                int h = TextRenderer.MeasureText(g, "Ag", f2).Height;
                TextRenderer.DrawText(g, line, f2, new Rectangle(30, y, rc.Width - 60, h),
                    _nowPlaying.Length > 0 ? Color.FromArgb(140, 200, 255) : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                y += h + (int)(14 * f);
            }
        }

        // أعمدة الصوت
        int barsWidth = Math.Min(rc.Width - (int)(60 * f), (int)((small ? 220 : 420) * f));
        int barsHeight = (int)((small ? 34 : 70) * f);
        if (y + barsHeight < rc.Height - (int)(8 * f))
        {
            float slot = barsWidth / (float)_bars.Length;
            float bw = Math.Max(2f, slot * 0.62f);
            float x0 = rc.Width / 2f - barsWidth / 2f;
            float baseY = y + barsHeight;
            using var barBrush = new LinearGradientBrush(new RectangleF(0, y, 10, barsHeight + 1),
                Color.FromArgb(90, 210, 255), Color.FromArgb(0, 120, 212), LinearGradientMode.Vertical);
            for (int i = 0; i < _bars.Length; i++)
            {
                float h = Math.Max(3 * f, _bars[i] * barsHeight);
                var r = new RectangleF(x0 + i * slot + (slot - bw) / 2, baseY - h, bw, h);
                using var path = RoundedBar(r, bw / 2);
                g.FillPath(barBrush, path);
            }
            y = (int)baseY + (int)(14 * f);
        }

        // مدة الاستماع
        if (!small && _status != Status.Connecting)
        {
            var t = DateTime.UtcNow - _startedUtc;
            string text = (_isTv ? "بتتفرج من " : "بتسمع من ") + (t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}");
            using var f3 = new Font("Segoe UI", 9.5f);
            int h = TextRenderer.MeasureText(g, "Ag", f3).Height;
            if (y + h < rc.Height)
                TextRenderer.DrawText(g, text, f3, new Rectangle(20, y, rc.Width - 40, h), Color.FromArgb(120, 120, 128),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawLogo(Graphics g, Rectangle r, float f)
    {
        int radius = (int)(r.Width * 0.22);
        Modern.FillRound(g, Color.FromArgb(70, 0, 0, 0), new Rectangle(r.X + (int)(3 * f), r.Y + (int)(5 * f), r.Width, r.Height), radius);

        if (_logo != null)
        {
            Modern.FillRound(g, Color.White, r, radius);
            using var clip = new GraphicsPath();
            AddRound(clip, r, radius);
            var state = g.Save();
            g.SetClip(clip);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // اللوجو جوه المربع من غير ما يتمط
            float scale = Math.Min((float)r.Width / _logo.Width, (float)r.Height / _logo.Height);
            float w = _logo.Width * scale, h = _logo.Height * scale;
            g.DrawImage(_logo, r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
            g.Restore(state);
        }
        else
        {
            using (var grad = new LinearGradientBrush(r, Color.FromArgb(31, 111, 209), Color.FromArgb(11, 64, 130), LinearGradientMode.ForwardDiagonal))
            using (var tile = new GraphicsPath())
            {
                AddRound(tile, r, radius);
                g.FillPath(grad, tile);
            }
            using var glyph = new Font(Glyphs.Family, r.Width * 0.30f);
            TextRenderer.DrawText(g, _isTv ? Glyphs.Video : Glyphs.Radio, glyph, r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private static GraphicsPath RoundedBar(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d < 1) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        p.CloseFigure();
        return p;
    }

    private static void AddRound(GraphicsPath p, Rectangle r, int radius)
    {
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_hookedForm != null) _hookedForm.Resize -= OnFormResize;
            _anim.Dispose();
            _logo?.Dispose();
        }
        base.Dispose(disposing);
    }
}
