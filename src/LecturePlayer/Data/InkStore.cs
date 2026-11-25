using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LecturePlayer.Data;

/// <summary>
/// الشرح (أقلام/أشكال/نصوص/ملصقات) على الكتب: ملف JSON لكل كتاب في فولدر «‹اسم المكتبة›.annotations» جنب ملف المكتبة
/// (فبيتزامن مع Google Drive/OneDrive زيها). ملف الكتاب الأصلي مابيتلمسش أبدًا.
/// الشكل: {"v":1,"pages":{"‹رقم الصفحة›":[عناصر]},"del":{"‹رقم العنصر›":وقت المسح}}
/// كل عنصر ليه id و u (وقت آخر تعديل بالملّي ثانية): الدمج بين جهازين = الأحدث يكسب، والممسوح يفضل ممسوح.
/// </summary>
public static class InkStore
{
    public const string FolderSuffix = ".annotations";

    public static string DirFor(string libraryPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(libraryPath)) ?? ".", Path.GetFileNameWithoutExtension(libraryPath) + FolderSuffix);

    public static string FileFor(string libraryPath, string bookId) => Path.Combine(DirFor(libraryPath), SafeName(bookId) + ".json");

    private static string SafeName(string id)
    {
        var sb = new StringBuilder(id.Length);
        foreach (char c in id) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
        return sb.Length == 0 ? "book" : sb.ToString();
    }

    public static JsonObject Empty() => new() { ["v"] = 1, ["pages"] = new JsonObject(), ["del"] = new JsonObject() };

    /// <summary>بيقرا الملف (أو فاضي لو مش موجود/بايظ — ونسخة من البايظ بتتحفظ جنبه).</summary>
    public static JsonObject Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return Empty();
            string text = File.ReadAllText(file, Encoding.UTF8);
            return Normalize(JsonNode.Parse(text) as JsonObject);
        }
        catch (Exception ex)
        {
            Log.Info("ink load: " + ex.Message);
            try { File.Copy(file, file + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: false); } catch { }
            return Empty();
        }
    }

    public static JsonObject Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty();
        try { return Normalize(JsonNode.Parse(json) as JsonObject); } catch { return Empty(); }
    }

    private static JsonObject Normalize(JsonObject? o)
    {
        o ??= Empty();
        if (o["pages"] is not JsonObject) o["pages"] = new JsonObject();
        if (o["del"] is not JsonObject) o["del"] = o["tomb"] is JsonObject t ? (JsonObject)t.DeepClone() : new JsonObject();
        o.Remove("tomb");
        o["v"] = 1;
        return o;
    }

    public static JsonObject Pages(JsonObject doc) => (JsonObject)doc["pages"]!;
    public static JsonObject Tombs(JsonObject doc) => (JsonObject)doc["del"]!;

    private static long U(JsonNode? item) => Num(item?["u"]);
    private static string? Id(JsonNode? item) => item?["id"] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
    /// <summary>رقم من JSON مهما كان نوعه جوه (long من عندنا، أو double/int من ملف).</summary>
    private static long Num(JsonNode? n)
    {
        if (n is not JsonValue v) return 0;
        if (v.TryGetValue(out long l)) return l;
        if (v.TryGetValue(out double d)) return (long)d;
        if (v.TryGetValue(out int i)) return i;
        if (v.TryGetValue(out decimal m)) return (long)m;
        return 0;
    }

    /// <summary>
    /// تغييرات من القارئ: الصفحات اللي اتغيرت (كاملة) + أوقات المسح. بيرجّع true لو اتغير حاجة.
    /// القارئ هو اللي شايف آخر حالة للكتاب المفتوح، فصفحاته بتستبدل اللي عندنا.
    /// </summary>
    public static bool Apply(JsonObject doc, JsonElement pages, JsonElement tombs)
    {
        bool changed = false;
        var mine = Pages(doc);
        if (pages.ValueKind == JsonValueKind.Object)
            foreach (var p in pages.EnumerateObject())
            {
                var arr = JsonNode.Parse(p.Value.GetRawText()) as JsonArray ?? new JsonArray();
                if (arr.Count == 0) { if (mine.Remove(p.Name)) changed = true; continue; }
                string before = mine[p.Name]?.ToJsonString() ?? "";
                if (before != arr.ToJsonString()) { mine[p.Name] = arr; changed = true; }
            }
        if (tombs.ValueKind == JsonValueKind.Object)
        {
            var del = Tombs(doc);
            foreach (var t in tombs.EnumerateObject())
            {
                long v = t.Value.ValueKind == JsonValueKind.Number ? (long)t.Value.GetDouble() : 0;
                if (v > Num(del[t.Name])) { del[t.Name] = v; changed = true; }
            }
        }
        return changed;
    }

    /// <summary>
    /// بيدمج نسخة تانية (من جهاز تاني) جوه doc. بيرجّع الصفحات اللي اتغيرت في doc.
    /// القواعد زي القارئ بالظبط: العنصر الجديد بيتضاف، الأحدث يكسب، والممسوح بعد آخر تعديل بيتشال.
    /// </summary>
    public static HashSet<string> Merge(JsonObject doc, JsonObject other)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var del = Tombs(doc);
        foreach (var (id, v) in Tombs(other))
            if (Num(v) > Num(del[id])) del[id] = Num(v);

        var mine = Pages(doc);
        var theirs = Pages(other);
        var keys = mine.Select(k => k.Key).Concat(theirs.Select(k => k.Key)).Distinct(StringComparer.Ordinal).ToList();
        foreach (string key in keys)
        {
            var a = mine[key] as JsonArray ?? new JsonArray();
            var b = theirs[key] as JsonArray ?? new JsonArray();
            var list = a.Select(x => x?.DeepClone()).ToList();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++) if (Id(list[i]) is { } id) index[id] = i;
            bool dirty = false;
            foreach (var t in b)
            {
                if (Id(t) is not { } id) continue;
                if (!index.TryGetValue(id, out int i)) { index[id] = list.Count; list.Add(t!.DeepClone()); dirty = true; }
                else if (U(t) > U(list[i])) { list[i] = t!.DeepClone(); dirty = true; }
            }
            var kept = list.Where(x => !(Id(x) is { } id && del[id] is { } d && Num(d) >= U(x))).ToList();
            if (kept.Count != list.Count) dirty = true;
            if (!dirty) continue;
            if (kept.Count == 0) mine.Remove(key);
            else mine[key] = new JsonArray(kept.ToArray());
            changed.Add(key);
        }
        return changed;
    }

    /// <summary>أوقات المسح الأقدم من 90 يوم بتتشال (الأجهزة التانية أكيد اتزامنت من زمان).</summary>
    public static void Prune(JsonObject doc)
    {
        long cutoff = DateTimeOffset.UtcNow.AddDays(-90).ToUnixTimeMilliseconds();
        var del = Tombs(doc);
        foreach (var k in del.Where(x => Num(x.Value) < cutoff).Select(x => x.Key).ToList()) del.Remove(k);
    }

    /// <summary>عدد العناصر الدائمة (من غير الطبقة المؤقتة).</summary>
    public static int Count(JsonObject doc) =>
        Pages(doc).Sum(p => p.Value is JsonArray a ? a.Count(i => i?["ly"]?.GetValue<string>() != "t") : 0);

    /// <summary>الصفحات اللي عليها شرح (للقائمة والنسخة المشروحة).</summary>
    public static int[] PagesWithInk(JsonObject doc) =>
        Pages(doc).Where(p => p.Value is JsonArray { Count: > 0 } a && a.Any(i => i?["ly"]?.GetValue<string>() != "t"))
            .Select(p => int.TryParse(p.Key, out int n) ? n : 0).Where(n => n > 0).OrderBy(n => n).ToArray();

    /// <summary>حفظ آمن: ملف مؤقت وبعدين استبدال (عشان قطع الكهربا/المزامنة مايبوظوش الملف).</summary>
    public static DateTime Save(string file, JsonObject doc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        Prune(doc);
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, doc.ToJsonString(), new UTF8Encoding(false));
        if (File.Exists(file)) File.Replace(tmp, file, null, ignoreMetadataErrors: true);
        else File.Move(tmp, file);
        return File.GetLastWriteTimeUtc(file);
    }

    public static DateTime Stamp(string file)
    {
        try { return File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue; } catch { return DateTime.MinValue; }
    }
}
