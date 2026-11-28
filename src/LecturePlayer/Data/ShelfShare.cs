using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LecturePlayer.Data;

/// <summary>
/// «شارك الرف»: ملف ZIP فيه مكتبة صغيرة بالرفوف اللي اخترتها + نسخة من ملفات الكتب (+ الشرح والنسخ المشروحة لو عايز).
/// اللي بيستقبله: «استقبل رف متشارك» بيفكّه في فولدر ويضيف الرفوف لمكتبته بأرقام جديدة (مفيش تعارض مع اللي عنده).
/// الملف نفسه مكتبة عادية كمان: ممكن يتفك ويتفتح من «فتح مكتبة».
/// </summary>
public static class ShelfShare
{
    public const string Manifest = "masar-share.json";
    public const string LibName = "مكتبة متشاركة" + Storage.LibraryExtension;

    public sealed record Options(bool Progress, bool Annotations, bool AnnotatedCopies);
    public sealed record Plan(string Name, List<Shelf> Shelves, List<Book> Books, List<Book> PrivateLinks, List<Book> Missing, long Bytes);
    public sealed record ExportResult(int Books, int Files, long Bytes);
    public sealed record ImportResult(string Name, List<Shelf> Shelves, List<Book> Books, string Folder, int Annotations)
    {
        /// <summary>رقم الكتاب في الملف ← رقمه الجديد في المكتبة.</summary>
        public Dictionary<string, string> IdMap { get; init; } = new();
    }

    /// <summary>رابط فيه توكن/توقيع أو رابط Drive خاص — اللي هيستقبله ممكن مايقدرش يفتحه (أو يشوف حاجة مش المفروض يشوفها).</summary>
    public static bool LooksPrivate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        string q = u.Query.ToLowerInvariant();
        if (q.Contains("token") || q.Contains("signature") || q.Contains("sig=") || q.Contains("key=") || q.Contains("expires") || q.Contains("x-amz-") || q.Contains("access_token") || q.Contains("auth"))
            return true;
        string host = u.Host.ToLowerInvariant();
        return host.EndsWith("sharepoint.com") || host.Contains("1drv.ms") || (host.Contains("dropbox") && q.Contains("rlkey"));
    }

    /// <summary>إيه اللي هيتشارك (عشان الشباك يعرض الحجم والتحذيرات قبل ما نكتب).</summary>
    public static Plan MakePlan(CourseLibrary lib, IEnumerable<Shelf> roots, Func<Book, string> fileOf, Options o)
    {
        var shelves = new List<Shelf>();
        var stack = new Stack<Shelf>(roots.Reverse());
        while (stack.Count > 0)
        {
            var s = stack.Pop();
            if (shelves.Contains(s)) continue;
            shelves.Add(s);
            foreach (var c in lib.ShelvesIn(s).Reverse()) stack.Push(c);
        }
        var ids = shelves.Select(s => s.Id).ToHashSet();
        var books = lib.Books.Where(b => b.ShelfId != null && ids.Contains(b.ShelfId)).ToList();
        if (o.AnnotatedCopies)
        {
            var bookIds = books.Select(b => b.Id).ToHashSet();
            books.AddRange(lib.Books.Where(b => b.AnnotatedOf != null && bookIds.Contains(b.AnnotatedOf) && !books.Contains(b)));
        }
        var priv = new List<Book>();
        var missing = new List<Book>();
        long bytes = 0;
        foreach (var b in books)
        {
            string f = fileOf(b);
            bool have = !string.IsNullOrEmpty(f) && File.Exists(f);
            if (have) bytes += new FileInfo(f).Length;
            else if (b.IsOnline) { if (LooksPrivate(b.Path)) priv.Add(b); }
            else missing.Add(b);
        }
        return new Plan(roots.FirstOrDefault()?.Name ?? "رف", shelves, books, priv, missing, bytes);
    }

    /// <summary>ملفات زيادة جوه الحزمة (فيديوهات الحصة مثلًا) — بتتكتب في نفس المرور (من غير فتح الملف تاني).</summary>
    public sealed record Extra(IReadOnlyList<(string Src, string Entry)>? Files = null, IReadOnlyList<(string Entry, string Text)>? Texts = null,
        Func<Book, string?>? InkOf = null, string? Readme = null);

    public static ExportResult Export(Plan plan, string libraryPath, Func<Book, string> fileOf, string zipPath, Options o, IProgress<double>? progress, CancellationToken ct,
        Extra? extra = null)
    {
        string annDir = Path.GetFileNameWithoutExtension(LibName) + InkStore.FolderSuffix + "/";
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copies = new List<Book>();
        var files = new List<(string Src, string Entry)>();
        var inPlan = plan.Books.Select(b => b.Id).ToHashSet();
        foreach (var b in plan.Books)
        {
            var c = JsonSerializer.Deserialize<Book>(JsonSerializer.Serialize(b))!;
            string f = fileOf(b);
            if (!string.IsNullOrEmpty(f) && File.Exists(f))
            {
                string name = Path.GetFileName(f), stem = Path.GetFileNameWithoutExtension(f), ext = Path.GetExtension(f);
                for (int i = 2; !used.Add(name); i++) name = $"{stem} ({i}){ext}";
                string entry = "books/" + name;
                files.Add((f, entry));
                c.Path = entry.Replace('/', Path.DirectorySeparatorChar);
                c.RelativePath = c.Path;
            }
            else if (!b.IsOnline) { c.RelativePath = null; } // ملف مش موجود: بيتسجّل بس (هيبان «ناقص» عند اللي استقبله)
            if (!o.Progress)
            {
                c.ReadPages = ""; c.LastPage = 0; c.LastTop = null; c.LastLeft = null; c.LastCfi = null; c.LastOpenedUtc = null;
                c.MarkedDone = false; c.Zoom = null; c.ProgressUtc = default;
            }
            if (!o.Annotations) { c.Marks = new(); c.Highlights = new(); c.Notes = null; c.InkCount = 0; }
            if (c.AnnotatedOf != null && !inPlan.Contains(c.AnnotatedOf)) c.AnnotatedOf = null;
            if (c.WorkCopyOf != null && !inPlan.Contains(c.WorkCopyOf)) c.WorkCopyOf = null;
            if (c.AnnotatedOf != null && plan.Books.FirstOrDefault(x => x.Id == c.AnnotatedOf) is { } src) c.ShelfId = src.ShelfId; // النسخة المشروحة جنب أصلها
            copies.Add(c);
        }
        var shelves = plan.Shelves.Select(s => new Shelf
        {
            Id = s.Id, Name = s.Name, IconKey = s.IconKey, ColorHex = s.ColorHex, Expanded = true,
            ParentId = s.ParentId != null && plan.Shelves.Any(x => x.Id == s.ParentId) ? s.ParentId : null, InfoUpdatedUtc = DateTime.UtcNow
        }).ToList();
        var lib = new CourseLibrary { Name = plan.Name, Shelves = shelves, Books = copies };

        var extraFiles = (extra?.Files ?? Array.Empty<(string, string)>()).Where(x => File.Exists(x.Src)).ToList();
        string tmp = zipPath + ".part";
        long total = Math.Max(1, files.Concat(extraFiles).Sum(x => new FileInfo(x.Src).Length)), done = 0;
        int annotations = 0;
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                void Text(string name, string text)
                {
                    var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                    w.Write(text);
                }
                Text(LibName, Storage.Serialize(lib));
                Text(Manifest, new JsonObject
                {
                    ["v"] = 1, ["app"] = "Masar", ["name"] = plan.Name, ["created"] = DateTime.UtcNow.ToString("o"),
                    ["books"] = copies.Count, ["progress"] = o.Progress, ["annotations"] = o.Annotations, ["annotatedCopies"] = o.AnnotatedCopies
                }.ToJsonString());
                Text("اقرأني.txt", extra?.Readme ??
                    $"هذا رف مشترك من «مسار»: {plan.Name}\r\n\r\n" +
                    "الطريقة الأسهل: من «مسار» ← الكتب ← إضافة ← «رف مشترك (ZIP)» واختر هذا الملف — ستُضاف الرفوف إلى مكتبتك.\r\n" +
                    $"أو فك الضغط وافتح «{LibName}» من «فتح مكتبة».\r\n\r\n" +
                    "الكتب نفسها في مجلد books.\r\n");
                if (o.Annotations)
                    foreach (var b in plan.Books)
                    {
                        string slides = InkStore.FileFor(libraryPath, b.Id + "-slides");   // رسم «أقلام وأشكال» على شرائح العرض
                        if (File.Exists(slides)) zip.CreateEntryFromFile(slides, annDir + Path.GetFileName(slides), CompressionLevel.Optimal);
                        string ink = extra?.InkOf?.Invoke(b) ?? InkStore.FileFor(libraryPath, b.Id);
                        if (!File.Exists(ink)) continue;
                        zip.CreateEntryFromFile(ink, annDir + b.Id + ".json", CompressionLevel.Optimal);
                        annotations++;
                    }
                foreach (var (entry, text) in extra?.Texts ?? Array.Empty<(string, string)>()) Text(entry, text);
                foreach (var (src, entry) in files.Concat(extraFiles))
                {
                    ct.ThrowIfCancellationRequested();
                    string ext = Path.GetExtension(src).ToLowerInvariant();
                    bool packed = ext is ".pdf" or ".epub" or ".zip" || MediaFiles.IsMedia(src);   // مضغوطة أصلًا
                    var e = zip.CreateEntry(entry, packed ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    e.LastWriteTime = File.GetLastWriteTime(src);
                    using var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var output = e.Open();
                    var buf = new byte[1 << 20];
                    int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        output.Write(buf, 0, n);
                        done += n;
                        progress?.Report((double)done / total);
                    }
                }
            }
            File.Move(tmp, zipPath, overwrite: true);
        }
        catch { try { File.Delete(tmp); } catch { } throw; }
        return new ExportResult(copies.Count, files.Count, new FileInfo(zipPath).Length);
    }

    /// <summary>اسم الرف من الملف (من غير فك).</summary>
    public static string? PeekName(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var m = zip.GetEntry(Manifest);
            if (m == null) return zip.Entries.Any(e => e.FullName.EndsWith(Storage.LibraryExtension, StringComparison.OrdinalIgnoreCase)) ? Path.GetFileNameWithoutExtension(zipPath) : null;
            using var r = new StreamReader(m.Open());
            return JsonNode.Parse(r.ReadToEnd())?["name"]?.GetValue<string>();
        }
        catch { return null; }
    }

    /// <summary>
    /// بيفك الملف في dest ويرجّع الرفوف والكتب بأرقام جديدة (لسه مااتضافتش للمكتبة).
    /// الشرح بيتنسخ لفولدر شرح المكتبة باسم الأرقام الجديدة.
    /// </summary>
    public static ImportResult Import(string zipPath, string dest, CourseLibrary into, Shelf? parent, string libraryPath)
    {
        Directory.CreateDirectory(dest);
        string root = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string? lplEntry = null;
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var e in zip.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;
                // أي مسار بيحاول يطلع برّه الفولدر (../) بيتجاهل
                string target = Path.GetFullPath(Path.Combine(root, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, overwrite: true);
                if (lplEntry == null && e.FullName.EndsWith(Storage.LibraryExtension, StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/')) lplEntry = target;
            }
        }
        if (lplEntry == null) throw new InvalidDataException("هذا الملف ليس رفًا مشتركًا من «مسار» (لا توجد مكتبة بداخله)");
        var shared = Storage.FromJson(File.ReadAllText(lplEntry, Encoding.UTF8), lplEntry);
        string name = shared.Name is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(zipPath);

        var shelfMap = shared.Shelves.ToDictionary(s => s.Id, _ => Guid.NewGuid().ToString("N"));
        var bookMap = shared.Books.ToDictionary(b => b.Id, _ => Guid.NewGuid().ToString("N"));
        var shelves = new List<Shelf>();
        foreach (var s in shared.Shelves)
        {
            bool top = s.ParentId == null || !shelfMap.ContainsKey(s.ParentId);
            string sname = s.Name;
            if (top && into.ShelvesIn(parent).Any(x => string.Equals(x.Name, sname, StringComparison.CurrentCultureIgnoreCase))) sname += " (مشترك)";
            shelves.Add(new Shelf
            {
                Id = shelfMap[s.Id], Name = sname, IconKey = s.IconKey, ColorHex = s.ColorHex, Expanded = true,
                ParentId = top ? parent?.Id : shelfMap[s.ParentId!], InfoUpdatedUtc = DateTime.UtcNow
            });
        }
        var books = new List<Book>();
        int ann = 0;
        string annSrc = Path.Combine(Path.GetDirectoryName(lplEntry)!, Path.GetFileNameWithoutExtension(lplEntry) + InkStore.FolderSuffix);
        foreach (var b in shared.Books)
        {
            string oldId = b.Id;
            b.Id = bookMap[oldId];
            b.ShelfId = b.ShelfId != null && shelfMap.TryGetValue(b.ShelfId, out var sid) ? sid : shelves.FirstOrDefault()?.Id;
            if (b.AnnotatedOf != null) b.AnnotatedOf = bookMap.GetValueOrDefault(b.AnnotatedOf);
            if (b.WorkCopyOf != null) b.WorkCopyOf = bookMap.GetValueOrDefault(b.WorkCopyOf);
            if (!b.IsOnline) { b.Path = b.LocalPath; b.RelativePath = null; }
            b.TouchInfo();
            string slidesFile = Path.Combine(annSrc, oldId + "-slides.json");
            if (File.Exists(slidesFile) && !string.IsNullOrEmpty(libraryPath))
            {
                Directory.CreateDirectory(InkStore.DirFor(libraryPath));
                File.Copy(slidesFile, InkStore.FileFor(libraryPath, b.Id + "-slides"), overwrite: true);
            }
            string inkFile = Path.Combine(annSrc, oldId + ".json");
            if (File.Exists(inkFile) && !string.IsNullOrEmpty(libraryPath))
            {
                var doc = InkStore.Load(inkFile);
                InkStore.Save(InkStore.FileFor(libraryPath, b.Id), doc);
                b.InkCount = InkStore.Count(doc);
                ann++;
            }
            else b.InkCount = 0;
            books.Add(b);
        }
        // ملفات المكتبة الصغيرة ملهاش لازمة بعد الاستيراد (الكتب بس اللي بتفضل)
        try { File.Delete(lplEntry); } catch { }
        try { if (Directory.Exists(annSrc)) Directory.Delete(annSrc, true); } catch { }
        try { File.Delete(Path.Combine(root, Manifest)); } catch { }
        return new ImportResult(name, shelves, books, dest, ann) { IdMap = bookMap };
    }
}
