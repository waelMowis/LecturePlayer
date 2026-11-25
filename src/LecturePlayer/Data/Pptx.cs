using System.IO.Compression;
using System.Xml.Linq;

namespace LecturePlayer.Data;

/// <summary>
/// قراية ملف عرض PowerPoint (pptx/ppsx/pptm) من غير Office: عدد الشرائح وعناوينها ونصها والملاحظات والمخفي منها.
/// (العرض نفسه بالأنيميشن بيحتاج PowerPoint متثبت — شوف PowerPointShow.)
/// </summary>
public static class Pptx
{
    public static readonly string[] Extensions = { ".pptx", ".ppsx", ".pptm", ".ppt", ".pps" };
    public static bool IsPresentation(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    /// <summary>الصيغ القديمة (ppt/pps) ماينفعش تتقري من غير PowerPoint.</summary>
    public static bool IsOpenXml(string path) => Path.GetExtension(path).ToLowerInvariant() is ".pptx" or ".ppsx" or ".pptm";

    public sealed record Slide(int Index, string Title, string Text, string Notes, bool Hidden);

    static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static List<Slide> Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XDocument Xml(string name)
        {
            var e = zip.GetEntry(name) ?? throw new InvalidDataException("الملف ناقص: " + name);
            using var s = e.Open();
            return XDocument.Load(s);
        }
        Dictionary<string, string> Rels(string part)
        {
            string dir = part.Contains('/') ? part[..part.LastIndexOf('/')] : "";
            string relName = (dir.Length > 0 ? dir + "/" : "") + "_rels/" + part[(part.LastIndexOf('/') + 1)..] + ".rels";
            var map = new Dictionary<string, string>();
            if (zip.GetEntry(relName) == null) return map;
            foreach (var r in Xml(relName).Root!.Elements(Rel + "Relationship"))
            {
                string target = (string?)r.Attribute("Target") ?? "";
                string full = target.StartsWith('/') ? target[1..] : Normalize(dir + "/" + target);
                map[(string?)r.Attribute("Id") ?? ""] = full;
            }
            return map;
        }

        var pres = Xml("ppt/presentation.xml");
        var presRels = Rels("ppt/presentation.xml");
        var ids = pres.Root!.Element(P + "sldIdLst")?.Elements(P + "sldId").Select(x => (string?)x.Attribute(R + "id") ?? "").ToList() ?? new();
        var slides = new List<Slide>();
        int n = 0;
        foreach (var rid in ids)
        {
            if (!presRels.TryGetValue(rid, out var part) || zip.GetEntry(part) == null) continue;
            n++;
            var doc = Xml(part);
            bool hidden = (string?)doc.Root!.Attribute("show") == "0";
            string title = "", body = "";
            var texts = new List<string>();
            foreach (var sp in doc.Descendants(P + "sp"))
            {
                string type = (string?)sp.Descendants(P + "ph").FirstOrDefault()?.Attribute("type") ?? "";
                string t = Paragraphs(sp);
                if (t.Length == 0) continue;
                if (title.Length == 0 && (type == "title" || type == "ctrTitle")) title = t;
                else texts.Add(t);
            }
            body = string.Join("\n", texts);
            if (title.Length == 0) title = texts.FirstOrDefault()?.Split('\n')[0] ?? "";
            if (title.Length > 120) title = title[..120] + "…";
            string notes = "";
            var rels = Rels(part);
            var notesPart = rels.Values.FirstOrDefault(v => v.Contains("notesSlide", StringComparison.OrdinalIgnoreCase));
            if (notesPart != null && zip.GetEntry(notesPart) != null)
            {
                var nd = Xml(notesPart);
                notes = string.Join("\n", nd.Descendants(P + "sp")
                    .Where(sp => (string?)sp.Descendants(P + "ph").FirstOrDefault()?.Attribute("type") == "body")
                    .Select(Paragraphs).Where(t => t.Length > 0));
            }
            slides.Add(new Slide(n, title.Replace('\n', ' ').Trim(), body, notes, hidden));
        }
        return slides;
    }

    static string Paragraphs(XElement sp) =>
        string.Join("\n", sp.Descendants(A + "p").Select(p => string.Concat(p.Descendants(A + "t").Select(t => t.Value)).Trim()).Where(t => t.Length > 0));

    static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var s in path.Split('/'))
        {
            if (s == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (s.Length > 0 && s != ".") parts.Add(s);
        }
        return string.Join("/", parts);
    }

    /// <summary>للفحص: ملف pptx صغير (شرائح بعناوين ونص وملاحظات، وواحدة مخفية).</summary>
    public static void WriteTest(string path, params (string Title, string Body, string Notes, bool Hidden)[] slides)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Add(string name, string xml) { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(xml); }
        const string ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";
        Add("[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/></Types>");
        Add("ppt/presentation.xml", $"<?xml version=\"1.0\"?><p:presentation {ns}><p:sldIdLst>" +
            string.Concat(slides.Select((_, i) => $"<p:sldId id=\"{256 + i}\" r:id=\"rId{i + 10}\"/>")) + "</p:sldIdLst></p:presentation>");
        Add("ppt/_rels/presentation.xml.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            string.Concat(slides.Select((_, i) => $"<Relationship Id=\"rId{i + 10}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide{i + 1}.xml\"/>")) + "</Relationships>");
        for (int i = 0; i < slides.Length; i++)
        {
            var s = slides[i];
            string Sp(string type, string text) => $"<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"x\"/><p:cNvSpPr/><p:nvPr>{(type.Length > 0 ? $"<p:ph type=\"{type}\"/>" : "")}</p:nvPr></p:nvSpPr><p:txBody>" +
                string.Concat(text.Split('\n').Select(l => $"<a:p><a:r><a:t>{System.Security.SecurityElement.Escape(l)}</a:t></a:r></a:p>")) + "</p:txBody></p:sp>";
            Add($"ppt/slides/slide{i + 1}.xml", $"<?xml version=\"1.0\"?><p:sld {ns}{(s.Hidden ? " show=\"0\"" : "")}><p:cSld><p:spTree>{(s.Title.Length > 0 ? Sp("title", s.Title) : "")}{Sp("", s.Body)}</p:spTree></p:cSld></p:sld>");
            if (s.Notes.Length > 0)
            {
                Add($"ppt/slides/_rels/slide{i + 1}.xml.rels", $"<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide\" Target=\"../notesSlides/notesSlide{i + 1}.xml\"/></Relationships>");
                Add($"ppt/notesSlides/notesSlide{i + 1}.xml", $"<?xml version=\"1.0\"?><p:notes {ns}><p:cSld><p:spTree>{Sp("body", s.Notes)}</p:spTree></p:cSld></p:notes>");
            }
        }
    }
}
