using System.Globalization;
using System.Text.Json.Nodes;
using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>
/// رسم «🖊 أقلام وأشكال» فوق شرائح العرض: يُحفظ لكل عرض في مجلد الشرح بجانب المكتبة
/// («‹رقم العرض›-slides.json») بإحداثيات نسبية، فيعود عند فتح العرض ويدخل في التصدير.
/// </summary>
internal static class SlideInk
{
    public static string FileFor(string libraryPath, string bookId) => InkStore.FileFor(libraryPath, bookId + "-slides");

    public static Dictionary<int, List<ScreenInk.Item>> Load(string libraryPath, string bookId)
    {
        var r = new Dictionary<int, List<ScreenInk.Item>>();
        try
        {
            string f = FileFor(libraryPath, bookId);
            if (!File.Exists(f)) return r;
            var root = JsonNode.Parse(File.ReadAllText(f)) as JsonObject;
            if (root?["pages"] is not JsonObject pages) return r;
            foreach (var (k, v) in pages)
            {
                if (!int.TryParse(k, out int n) || v is not JsonArray arr) continue;
                var list = new List<ScreenInk.Item>();
                foreach (var it in arr.OfType<JsonObject>())
                {
                    if (!Enum.TryParse<ScreenInk.Tool>((string?)it["t"], out var tool)) continue;
                    var pts = (it["p"] as JsonArray)?.Select(x => (float)(double)x!).ToList() ?? new();
                    var item = new ScreenInk.Item
                    {
                        Tool = tool, Color = Color.FromArgb(unchecked((int)uint.Parse(((string?)it["c"] ?? "FFE03131").TrimStart('#'), NumberStyles.HexNumber))),
                        Width = (float)((double?)it["w"] ?? 0.005)
                    };
                    for (int i = 0; i + 1 < pts.Count; i += 2) item.Pts.Add(new PointF(pts[i], pts[i + 1]));
                    if (item.Pts.Count > 0) list.Add(item);
                }
                if (list.Count > 0) r[n] = list;
            }
        }
        catch (Exception ex) { Log.Info("slide ink load: " + ex.Message); }
        return r;
    }

    /// <summary>نسبة عرض مساحة الرسم إلى ارتفاعها وقت الرسم (الشريحة في وسطها بنسبتها) — 0 = غير معروفة.</summary>
    public static float AspectOf(string libraryPath, string bookId)
    {
        try
        {
            string f = FileFor(libraryPath, bookId);
            return File.Exists(f) && JsonNode.Parse(File.ReadAllText(f))?["a"] is JsonNode a ? (float)(double)a : 0;
        }
        catch { return 0; }
    }

    public static void Save(string libraryPath, string bookId, Dictionary<int, List<ScreenInk.Item>> pages, float aspect = 0)
    {
        try
        {
            string f = FileFor(libraryPath, bookId);
            var obj = new JsonObject();
            foreach (var (n, items) in pages.Where(p => p.Value.Count > 0).OrderBy(p => p.Key))
            {
                var arr = new JsonArray();
                foreach (var i in items)
                {
                    var pts = new JsonArray();
                    foreach (var p in i.Pts) { pts.Add(Math.Round(p.X, 5)); pts.Add(Math.Round(p.Y, 5)); }
                    arr.Add(new JsonObject { ["t"] = i.Tool.ToString(), ["c"] = ((uint)i.Color.ToArgb()).ToString("X8"), ["w"] = Math.Round(i.Width, 6), ["p"] = pts });
                }
                obj[n.ToString(CultureInfo.InvariantCulture)] = arr;
            }
            if (obj.Count == 0) { if (File.Exists(f)) File.Delete(f); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, new JsonObject { ["v"] = 1, ["a"] = Math.Round(aspect, 4), ["pages"] = obj }.ToJsonString());
        }
        catch (Exception ex) { Log.Error("slide ink save", ex); }
    }
}
