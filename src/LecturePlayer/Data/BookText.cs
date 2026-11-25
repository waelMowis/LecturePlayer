using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LecturePlayer.Data;

/// <summary>
/// نص صفحات الكتاب (من الـ PDF نفسه، أو من الـ OCR للصفحات المصوّرة) — للبحث في كل الكتب وللذكاء الاصطناعي.
/// بيتحفظ على الجهاز ده بس (Data\book-text) ومش بيدخل ملف المكتبة.
/// </summary>
public sealed class BookText
{
    public int PageCount { get; set; }
    /// <summary>رقم الصفحة ← النص.</summary>
    public Dictionary<int, string> Pages { get; set; } = new();
    /// <summary>الصفحات اللي نصها جه من OCR.</summary>
    public List<int> OcrPages { get; set; } = new();
    /// <summary>الكتاب كله اتقرا (EPUB بيتقري كله مرة واحدة: كل فصل بأول صفحة فيه).</summary>
    public bool Complete { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public static string Dir => Path.Combine(Storage.AppDataDir, "book-text");

    /// <summary>بالبصمة لو معروفة (نفس الكتاب في أي مكان)، وإلا بالمسار.</summary>
    public static string PathFor(Book b)
    {
        string key = !string.IsNullOrEmpty(b.Fingerprint) ? b.Fingerprint! : b.Path.ToUpperInvariant();
        return Path.Combine(Dir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..24].ToLowerInvariant() + ".json");
    }

    public static BookText Load(Book b)
    {
        try
        {
            string p = PathFor(b);
            if (File.Exists(p)) return JsonSerializer.Deserialize<BookText>(File.ReadAllText(p)) ?? new BookText();
        }
        catch (Exception) { }
        return new BookText();
    }

    public void Save(Book b)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            UpdatedUtc = DateTime.UtcNow;
            File.WriteAllText(PathFor(b), JsonSerializer.Serialize(this));
        }
        catch (Exception ex) { Log.Info("book text save: " + ex.Message); }
    }

    public bool HasAll(int from, int to)
    {
        for (int p = from; p <= to; p++) if (!Pages.ContainsKey(p)) return false;
        return true;
    }

    /// <summary>صفحات فاضية من النص (غالبًا متصوّرة) في المدى.</summary>
    public List<int> EmptyPages(int from, int to) =>
        Enumerable.Range(from, Math.Max(0, to - from + 1)).Where(p => Pages.TryGetValue(p, out var t) && t.Trim().Length < 15 && !OcrPages.Contains(p)).ToList();

    /// <summary>النص بعلامة الصفحة قبل كل صفحة ([ص 12]) — للذكاء، ومقصوص عند الحد.</summary>
    public string Joined(int from, int to, int maxChars = 60_000)
    {
        var sb = new StringBuilder();
        for (int p = from; p <= to; p++)
        {
            if (!Pages.TryGetValue(p, out var t) || string.IsNullOrWhiteSpace(t)) continue;
            string block = $"[ص {p}]\n{t.Trim()}\n\n";
            if (sb.Length + block.Length > maxChars) { sb.Append("[… حُذف الباقي بسبب الطول]"); break; }
            sb.Append(block);
        }
        return sb.ToString();
    }

    public int CharCount => Pages.Values.Sum(v => v.Length);

    // ---------------- البحث ----------------

    public sealed record Hit(Book Book, int Page, string Snippet, int Score);

    /// <summary>
    /// الصفحات اللي فيها كل الكلمات (من غير التشكيل، والأ/إ/آ = ا، والى = ي، والة = ه، وحروف كبيرة = صغيرة).
    /// </summary>
    public IEnumerable<Hit> Search(Book b, string query)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 0).Distinct().ToList();
        if (words.Count == 0) yield break;
        foreach (var (page, text) in Pages.OrderBy(x => x.Key))
        {
            string norm = Normalize(text);
            if (!words.All(w => norm.Contains(w, StringComparison.Ordinal))) continue;
            int score = words.Sum(w => Count(norm, w));
            yield return new Hit(b, page, Snippet(text, norm, words[0]), score);
        }
    }

    private static int Count(string s, string w)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(w, i, StringComparison.Ordinal)) >= 0) { n++; i += w.Length; }
        return n;
    }

    /// <summary>حوالي 140 حرف حوالين أول كلمة (من نسخة من غير تشكيل، فمكان الكلمة مضبوط).</summary>
    private static string Snippet(string text, string norm, string word)
    {
        string flat = string.Join(" ", StripMarks(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string flatNorm = Normalize(flat);
        int i = flatNorm.IndexOf(word, StringComparison.Ordinal);
        if (i < 0 || flatNorm.Length != flat.Length) return flat.Length > 140 ? flat[..140] + "…" : flat;
        int start = Math.Max(0, i - 60), end = Math.Min(flat.Length, i + word.Length + 80);
        return (start > 0 ? "…" : "") + flat[start..end] + (end < flat.Length ? "…" : "");
    }

    private static string StripMarks(string s) =>
        new(s.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c != '\u0640').ToArray());

    /// <summary>تطبيع للبحث: من غير تشكيل ولا تطويل، والأ/إ/آ = ا، والى = ي، والة = ه، وحروف صغيرة.</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c0 in s)
        {
            char c = char.ToLowerInvariant(c0);
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark || c == 'ـ') continue; // تشكيل وتطويل
            c = c switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => c
            };
            if (char.IsWhiteSpace(c)) c = ' ';
            sb.Append(c);
        }
        return sb.ToString();
    }
}
