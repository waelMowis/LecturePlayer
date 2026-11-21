using System.Drawing.Drawing2D;
using System.Globalization;

namespace LecturePlayer.UI;

/// <summary>
/// أيقونات المواضيع والمصادر والكورسات اللي المستخدم بيختارها.
/// مرسومة كمسارات (من غير ملفات صور ولا خطوط)، فبتتلوّن بأي لون وبتبقى حادة في أي مقاس/DPI.
/// المسارات في <c>IconGlyphs.g.cs</c> في مربع 0..1 (M/L/H/V/Q/Z مطلقة).
/// </summary>
internal static partial class IconGlyphs
{
    private static readonly Dictionary<string, (string Category, string Label, string Path)> ByKey =
        All.ToDictionary(x => x.Key, x => (x.Category, x.Label, x.Path), StringComparer.Ordinal);
    private static readonly Dictionary<string, GraphicsPath> Cache = new(StringComparer.Ordinal);

    public static bool Exists(string? key) => key != null && ByKey.ContainsKey(key);
    public static string? LabelOf(string? key) => key != null && ByKey.TryGetValue(key, out var x) ? x.Label : null;
    public static IEnumerable<string> Categories => All.Select(x => x.Category).Distinct();

    /// <summary>بيرسم الأيقونة جوه المستطيل باللون المطلوب.</summary>
    public static void Draw(Graphics g, string key, RectangleF r, Color color)
    {
        var unit = PathOf(key);
        if (unit == null) return;
        using var p = (GraphicsPath)unit.Clone();
        using (var m = new Matrix(r.Width, 0, 0, r.Height, r.X, r.Y)) p.Transform(m);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(color)) g.FillPath(b, p);
        g.SmoothingMode = old;
    }

    public static GraphicsPath? PathOf(string key)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var p)) return p;
            if (!ByKey.TryGetValue(key, out var x)) return null;
            p = Parse(x.Path);
            Cache[key] = p;
            return p;
        }
    }

    /// <summary>
    /// SVG path بسيط (أوامر مطلقة: M L H V Q C Z). الـ Q بيتحوّل لـ Bezier تكعيبي.
    /// الملء Winding (nonzero) زي الخطوط — Alternate كان هيعمل فتحات غلط في الأشكال المتداخلة.
    /// </summary>
    internal static GraphicsPath Parse(string d)
    {
        var path = new GraphicsPath(FillMode.Winding);
        int i = 0;
        char cmd = 'M';
        PointF cur = PointF.Empty, start = PointF.Empty;
        bool open = false;

        bool More()
        {
            while (i < d.Length && (d[i] == ' ' || d[i] == ',')) i++;
            return i < d.Length && (char.IsDigit(d[i]) || d[i] is '-' or '.' or '+');
        }
        float Num()
        {
            while (i < d.Length && (d[i] == ' ' || d[i] == ',')) i++;
            int s = i;
            if (i < d.Length && d[i] is '-' or '+') i++;
            bool dot = false;
            while (i < d.Length && (char.IsDigit(d[i]) || (d[i] == '.' && !dot)))
            {
                if (d[i] == '.') dot = true;
                i++;
            }
            if (i < d.Length && d[i] is 'e' or 'E')
            {
                i++;
                if (i < d.Length && d[i] is '-' or '+') i++;
                while (i < d.Length && char.IsDigit(d[i])) i++;
            }
            return float.Parse(d.AsSpan(s, i - s), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        while (true)
        {
            while (i < d.Length && (d[i] == ' ' || d[i] == ',')) i++;
            if (i >= d.Length) break;
            if (char.IsLetter(d[i]) && d[i] is not ('e' or 'E')) cmd = d[i++];
            else if (!More()) { i++; continue; }

            switch (cmd)
            {
                case 'M':
                    if (open) path.CloseFigure();
                    path.StartFigure();
                    cur = start = new PointF(Num(), Num());
                    open = true;
                    cmd = 'L'; // أرقام بعد M = خطوط
                    break;
                case 'L':
                {
                    var p = new PointF(Num(), Num());
                    path.AddLine(cur, p); cur = p;
                    break;
                }
                case 'H':
                {
                    var p = new PointF(Num(), cur.Y);
                    path.AddLine(cur, p); cur = p;
                    break;
                }
                case 'V':
                {
                    var p = new PointF(cur.X, Num());
                    path.AddLine(cur, p); cur = p;
                    break;
                }
                case 'Q':
                {
                    var q = new PointF(Num(), Num());
                    var p = new PointF(Num(), Num());
                    var c1 = new PointF(cur.X + 2f / 3f * (q.X - cur.X), cur.Y + 2f / 3f * (q.Y - cur.Y));
                    var c2 = new PointF(p.X + 2f / 3f * (q.X - p.X), p.Y + 2f / 3f * (q.Y - p.Y));
                    path.AddBezier(cur, c1, c2, p); cur = p;
                    break;
                }
                case 'C':
                {
                    var c1 = new PointF(Num(), Num());
                    var c2 = new PointF(Num(), Num());
                    var p = new PointF(Num(), Num());
                    path.AddBezier(cur, c1, c2, p); cur = p;
                    break;
                }
                case 'Z':
                case 'z':
                    if (open) path.CloseFigure();
                    open = false;
                    cur = start;
                    break;
                default:
                    throw new FormatException($"أمر مش مدعوم في مسار الأيقونة: {cmd}");
            }
        }
        if (open) path.CloseFigure();
        return path;
    }

    // ---------------- الألوان ----------------

    /// <summary>ألوان جاهزة واضحة على الخلفية الغامقة.</summary>
    public static readonly (string Label, Color Color)[] Palette =
    {
        ("أزرق", Color.FromArgb(79, 195, 247)),
        ("بنفسجي", Color.FromArgb(177, 128, 215)),
        ("تركواز", Color.FromArgb(86, 182, 194)),
        ("أخضر", Color.FromArgb(126, 200, 120)),
        ("زيتوني", Color.FromArgb(190, 205, 100)),
        ("أصفر", Color.FromArgb(232, 196, 110)),
        ("برتقالي", Color.FromArgb(240, 156, 90)),
        ("أحمر", Color.FromArgb(232, 104, 112)),
        ("وردي", Color.FromArgb(240, 120, 170)),
        ("بني", Color.FromArgb(190, 145, 110)),
        ("رمادي", Color.FromArgb(160, 166, 172)),
        ("أبيض", Color.FromArgb(232, 232, 232)),
    };

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return null;
        return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
    }

    /// <summary>اللون بعد مايتفتّح لو غامق أوي (عشان الاسم يتقري على الخلفية الغامقة).</summary>
    public static Color Readable(Color c)
    {
        double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return lum >= 0.5 ? c : Modern.Blend(c, Color.White, Math.Min(0.7, (0.5 - lum) * 1.6 + 0.1));
    }
}
