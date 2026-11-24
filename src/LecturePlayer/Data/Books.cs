using System.Text;
using System.Text.Json.Serialization;

namespace LecturePlayer.Data;

/// <summary>رف في «الكتب» (ممكن يبقى جوه رف تاني).</summary>
public sealed class Shelf
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? IconKey { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ColorHex { get; set; }
    public bool Expanded { get; set; } = true;
    /// <summary>الرف الأكبر (null = رف أساسي).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ParentId { get; set; }
    /// <summary>فولدر مربوط: الكتب الجديدة فيه بتتضاف لما تحدّث الرف.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceFolder { get; set; }
    public DateTime InfoUpdatedUtc { get; set; }
    public void TouchInfo() => InfoUpdatedUtc = DateTime.UtcNow;
}

/// <summary>فصل من فهرس الكتاب (بيتقري من الـ PDF نفسه).</summary>
public sealed class BookChapter
{
    public string Title { get; set; } = "";
    /// <summary>أول صفحة (من 1).</summary>
    public int Page { get; set; }
    /// <summary>0 = فصل، 1 = جزء جوه الفصل.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Level { get; set; }
}

/// <summary>علامة على صفحة، ومعاها ملاحظة اختيارية (وبتدخل المراجعة المتباعدة).</summary>
public sealed class PageMark
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Page { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTime? ReviewDueUtc { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ReviewStage { get; set; }
}

/// <summary>كتاب على الرف: ملف PDF (على الجهاز أو في فولدر بيتزامن) أو رابط.</summary>
public sealed class Book
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Author { get; set; }

    /// <summary>مسار الملف (زي ما اتضاف على الجهاز اللي ضافه)، أو الرابط.</summary>
    public string Path { get; set; } = "";
    /// <summary>المسار بالنسبة لملف المكتبة (عشان يتفتح على جهاز تاني الدرايف فيه في مكان مختلف).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RelativePath { get; set; }
    /// <summary>بصمة الـ PDF: نفس الكتاب حتى لو اتنقل أو اتغير اسمه.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Fingerprint { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ShelfId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? IconKey { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ColorHex { get; set; }
    public bool Expanded { get; set; }

    // ---------------- التقدّم ----------------
    public int PageCount { get; set; }
    /// <summary>آخر صفحة (من 1).</summary>
    public int LastPage { get; set; }
    /// <summary>المكان جوه الصفحة (إحداثيات PDF) عشان ترجع لنفس السطر.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? LastTop { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? LastLeft { get; set; }
    /// <summary>التكبير: page-width / page-fit / auto / رقم.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Zoom { get; set; }
    /// <summary>0 = صفحة واحدة، 1 = صفحتين.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Spread { get; set; }
    /// <summary>الصفحات من اليمين للشمال (كتاب عربي في وضع الصفحتين).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool RightToLeft { get; set; }
    /// <summary>الصفحات اللي اتقرت فعلًا، مضغوطة كمدى: "1-12,15,20-30".</summary>
    public string ReadPages { get; set; } = "";
    /// <summary>علّمته «اتقرا» بإيدك.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool MarkedDone { get; set; }
    public List<BookChapter> Chapters { get; set; } = new();
    public List<PageMark> Marks { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; set; }

    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTime? LastOpenedUtc { get; set; }
    /// <summary>آخر تغيير في التقدّم (الصفحة/المقروء/العلامات) — للدمج بين الأجهزة.</summary>
    public DateTime ProgressUtc { get; set; }
    /// <summary>آخر تغيير في البيانات (الاسم/الرف/الشكل).</summary>
    public DateTime InfoUpdatedUtc { get; set; }

    /// <summary>المسار الفعلي على الجهاز ده (بيتحسب وقت التحميل).</summary>
    [JsonIgnore] public string LocalPath { get; set; } = "";

    public void TouchInfo() => InfoUpdatedUtc = DateTime.UtcNow;
    public void TouchProgress() => ProgressUtc = DateTime.UtcNow;

    [JsonIgnore] public bool IsOnline => MediaFiles.IsUrl(Path);
    [JsonIgnore] public string Format => BookFiles.FormatOf(Path);
    [JsonIgnore] public PageSet Read => PageSet.Parse(ReadPages);
    [JsonIgnore] public int ReadCount => Read.Count(PageCount);
    [JsonIgnore] public double Fraction => MarkedDone ? 1 : PageCount <= 0 ? 0 : Math.Min(1, ReadCount / (double)PageCount);
    /// <summary>خلص: علّمته بإيدك، أو قريت 95% من الصفحات.</summary>
    [JsonIgnore] public bool IsDone => MarkedDone || PageCount > 0 && ReadCount >= Math.Ceiling(PageCount * 0.95);
    [JsonIgnore] public bool IsStarted => ReadCount > 0 || LastPage > 1 || MarkedDone;

    /// <summary>
    /// أجزاء الكتاب اللي بتظهر في الشجرة: الفصول (المستوى الأول) من الفهرس، ولو مفيش فهرس: كل 25 صفحة جزء.
    /// كل جزء من صفحته لحد قبل الجزء اللي بعده.
    /// </summary>
    public List<(string Title, int From, int To, List<(string Title, int From, int To)> Parts)> Sections()
    {
        var list = new List<(string, int, int, List<(string, int, int)>)>();
        if (PageCount <= 0) return list;
        var top = Chapters.Where(c => c.Level == 0 && c.Page >= 1 && c.Page <= PageCount).OrderBy(c => c.Page).ToList();
        if (top.Count == 0)
        {
            const int size = 25;
            if (PageCount <= size) return list;
            for (int p = 1; p <= PageCount; p += size)
                list.Add(($"صفحات {p}–{Math.Min(PageCount, p + size - 1)}", p, Math.Min(PageCount, p + size - 1), new()));
            return list;
        }
        // صفحات قبل أول فصل (المقدمة والغلاف)
        if (top[0].Page > 1) list.Add(("البداية", 1, top[0].Page - 1, new()));
        for (int i = 0; i < top.Count; i++)
        {
            int from = top[i].Page;
            int to = i + 1 < top.Count ? Math.Max(from, top[i + 1].Page - 1) : PageCount;
            var parts = new List<(string, int, int)>();
            int ci = Chapters.IndexOf(top[i]);
            var subs = Chapters.Skip(ci + 1).TakeWhile(c => c.Level > 0).Where(c => c.Page >= from && c.Page <= to).OrderBy(c => c.Page).ToList();
            for (int j = 0; j < subs.Count; j++)
            {
                int sf = subs[j].Page;
                int st = j + 1 < subs.Count ? Math.Max(sf, subs[j + 1].Page - 1) : to;
                parts.Add((subs[j].Title, sf, st));
            }
            list.Add((top[i].Title, from, to, parts));
        }
        return list;
    }

    /// <summary>نسبة المقروء من مدى صفحات (للفصل).</summary>
    public double FractionOf(int from, int to)
    {
        if (MarkedDone) return 1;
        if (to < from) return 0;
        var set = Read;
        int n = 0;
        for (int p = from; p <= to; p++) if (set.Contains(p)) n++;
        return n / (double)(to - from + 1);
    }
}

/// <summary>مجموعة صفحات متخزنة كمدى ("1-12,15") عشان الكتب الكبيرة ماتكبّرش الملف.</summary>
public sealed class PageSet
{
    private readonly SortedSet<int> _pages = new();

    public static PageSet Parse(string? s)
    {
        var set = new PageSet();
        if (string.IsNullOrWhiteSpace(s)) return set;
        foreach (string part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int dash = part.IndexOf('-');
            if (dash > 0 && int.TryParse(part[..dash], out int a) && int.TryParse(part[(dash + 1)..], out int b))
            {
                if (b < a) (a, b) = (b, a);
                if (b - a > 100_000) continue;
                for (int p = Math.Max(1, a); p <= b; p++) set._pages.Add(p);
            }
            else if (int.TryParse(part, out int p) && p >= 1) set._pages.Add(p);
        }
        return set;
    }

    public bool Contains(int p) => _pages.Contains(p);
    public bool Add(int p) => p >= 1 && _pages.Add(p);
    public void UnionWith(PageSet other) => _pages.UnionWith(other._pages);
    public IEnumerable<int> Pages => _pages;
    /// <summary>العدد (من غير الصفحات اللي بعد آخر صفحة لو الكتاب اتغيّر).</summary>
    public int Count(int pageCount) => pageCount <= 0 ? _pages.Count : _pages.Count(p => p <= pageCount);

    public override string ToString()
    {
        var sb = new StringBuilder();
        int? start = null, prev = null;
        foreach (int p in _pages)
        {
            if (start == null) { start = prev = p; continue; }
            if (p == prev + 1) { prev = p; continue; }
            Append(sb, start.Value, prev!.Value);
            start = prev = p;
        }
        if (start != null) Append(sb, start.Value, prev!.Value);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, int a, int b)
    {
        if (sb.Length > 0) sb.Append(',');
        sb.Append(a);
        if (b > a) sb.Append('-').Append(b);
    }
}

/// <summary>ملفات الكتب: الأنواع المدعومة والبحث في الفولدرات.</summary>
public static class BookFiles
{
    public static readonly string[] Extensions = { ".pdf" };

    public static string FormatOf(string path)
    {
        string p = path;
        int q = p.IndexOfAny(new[] { '?', '#' });
        if (MediaFiles.IsUrl(p) && q > 0) p = p[..q];
        string ext = System.IO.Path.GetExtension(p).ToLowerInvariant();
        return ext == ".epub" ? "epub" : "pdf";
    }

    public static bool IsBookFile(string path) =>
        Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>الكتب اللي في فولدر (من غير الفولدرات الفرعية)، بالترتيب الطبيعي للأسماء.</summary>
    public static List<string> FilesIn(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir).Where(IsBookFile)
                .Where(f => !System.IO.Path.GetFileName(f).StartsWith("~$"))
                .OrderBy(f => System.IO.Path.GetFileName(f), NaturalComparer.Instance).ToList();
        }
        catch (Exception) { return new List<string>(); }
    }

    /// <summary>الفولدرات الفرعية اللي فيها كتب (في أي مستوى تحتها).</summary>
    public static List<string> SubfoldersWithBooks(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir)
                .Where(d => !System.IO.Path.GetFileName(d).StartsWith('.'))
                .Where(HasBooks)
                .OrderBy(d => System.IO.Path.GetFileName(d), NaturalComparer.Instance).ToList();
        }
        catch (Exception) { return new List<string>(); }
    }

    public static bool HasBooks(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Any(IsBookFile); }
        catch (Exception) { return false; }
    }

    /// <summary>اسم الكتاب من اسم الملف (من غير الامتداد والشرَط).</summary>
    public static string TitleFromFile(string path)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(MediaFiles.IsUrl(path) ? Uri.UnescapeDataString(path.Split('?', '#')[0]) : path);
        name = name.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(name) ? "كتاب" : name;
    }
}
