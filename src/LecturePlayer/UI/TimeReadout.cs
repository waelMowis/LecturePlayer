using LecturePlayer.Data;
using System.ComponentModel;

namespace LecturePlayer.UI;

/// <summary>
/// وقت الفيديو تحت الشريط: اللي اتشاف بخط واضح، وجنبه الباقي (أو المدة كلها) بلون باهت.
/// دوسة على الرقم بتبدّل بين «الباقي» و«المدة كلها».
/// </summary>
internal sealed class TimeReadout : Control
{
    private long _pos, _len;
    private bool _live;
    private Font? _smallFont;

    /// <summary>true = يعرض المدة كلها، false = يعرض الباقي.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowTotal { get; set; }

    public event EventHandler? ModeChanged;

    public TimeReadout()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        BackColor = Theme.Panel;
    }

    /// <summary>النص زي ما بيبان (للفحص والقارئ الصوتي).</summary>
    public override string Text
    {
        get => _live ? "● مباشر" : $"{TimeFormat.Format(_pos)} {Secondary()}".Trim();
        set { }
    }

    private string Secondary() =>
        _len <= 0 ? "" : ShowTotal ? "/ " + TimeFormat.Format(_len) : "−" + TimeFormat.Format(Math.Max(0, _len - _pos));

    public void Set(long positionMs, long lengthMs)
    {
        // بنرسم بس لما الثواني تتغير
        if (!_live && _pos / 1000 == positionMs / 1000 && _len / 1000 == lengthMs / 1000) return;
        _live = false; _pos = positionMs; _len = lengthMs;
        Invalidate();
    }

    public void SetLive() { if (_live) return; _live = true; Invalidate(); }

    public void Reset() { _live = false; _pos = 0; _len = 0; Invalidate(); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (_live || _len <= 0) return;
        ShowTotal = !ShowTotal;
        Invalidate();
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        if (_live)
        {
            TextRenderer.DrawText(g, "● مباشر", Font, ClientRectangle, Theme.Danger, flags | TextFormatFlags.Right);
            return;
        }
        _smallFont ??= new Font(Font.FontFamily, Font.Size * 0.88f);
        string main = TimeFormat.Format(_pos), second = Secondary();
        var mainSize = TextRenderer.MeasureText(g, main, Font, Size.Empty, flags);
        var secondSize = second.Length > 0 ? TextRenderer.MeasureText(g, second, _smallFont, Size.Empty, flags) : Size.Empty;
        int gap = second.Length > 0 ? 8 : 0;
        int x = Width - mainSize.Width - gap - secondSize.Width - 2;
        TextRenderer.DrawText(g, main, Font, new Rectangle(x, 0, mainSize.Width, Height), Theme.Text, flags);
        if (second.Length > 0)
            TextRenderer.DrawText(g, second, _smallFont, new Rectangle(x + mainSize.Width + gap, 0, secondSize.Width + 2, Height), Theme.Muted, flags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _smallFont?.Dispose();
        base.Dispose(disposing);
    }
}
