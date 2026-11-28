using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// «📂 افتح حصة» للطالب: يختار ملف الحصة المضغوط (من الشريط أو القائمة أو الرئيسية، أو يسحبه على البرنامج)،
/// فيرى ما فيها، ثم تنزل محتوياتها: الكتب والسبورات والدفتر والعروض في رف باسمها داخل «حصصي»،
/// والفيديوهات والروابط والتسجيلات في محاضرة باسمها في دورة «حصصي» — وتتفتح الحصة.
/// ملف الحصة نفسه مرة ثانية (تحديث من المعلم): تحديث الموجودة أو نسخة جديدة. والحزم القديمة (جلسة v1) تُفتح كما هي.
/// </summary>
internal partial class MainForm
{
    internal const string LessonsInbox = "حصص";
    /// <summary>للفحص: «حدّث الموجودة» (true) أو «نسخة جديدة» (false) من غير شباك.</summary>
    internal bool? SmokeLessonUpdate;

    /// <summary>manifest الحصة أو الجلسة من ملف ZIP (من غير فك) — null لو مش حصة.</summary>
    internal static JsonObject? PeekSessionManifest(string zip)
    {
        try
        {
            using var z = ZipFile.OpenRead(zip);
            var e = z.GetEntry(SessionManifest);
            if (e == null) return null;
            using var r = new StreamReader(e.Open(), Encoding.UTF8);
            return JsonNode.Parse(r.ReadToEnd()) as JsonObject;
        }
        catch { return null; }
    }

    internal static bool IsLessonZip(string path) =>
        File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && PeekSessionManifest(path) != null;

    internal void OpenLessonFileDialog()
    {
        if (!EnsureLibrary()) return;
        string? zip = SmokeOpenPath;
        if (zip == null)
        {
            using var dlg = new OpenFileDialog { Title = "اختر ملف الحصة (ملف مضغوط من «مسار» أرسله معلمك)", Filter = "حصة من «مسار» (ZIP)|*.zip", RestoreDirectory = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            zip = dlg.FileName;
        }
        _ = OpenLessonFileAsync(zip);
    }

    /// <summary>يعرض ما في الحصة، ثم ينزّلها ويفتحها. يرجّع الحصة (أو null لو اتلغت أو فشلت).</summary>
    internal async Task<StudySession?> OpenLessonFileAsync(string zip)
    {
        if (!EnsureLibrary()) return null;
        var m = PeekSessionManifest(zip);
        if (m == null)
        {
            // رف متشارك عادي (من غير حصة)
            if (ShelfShare.PeekName(zip) != null) { await ReceiveSharedShelfAsync(zip, null); return null; }
            Dialogs.Error(this, "هذا الملف ليس حصة من «مسار».\nاطلب من معلمك ملف «… — مسار.zip» الذي يصدّره من «📤 تصدير الحصة».");
            return null;
        }
        string name = (string?)m["name"] ?? Path.GetFileNameWithoutExtension(zip);
        string? lessonId = (string?)m["lessonId"];
        var existing = lessonId == null ? null : _library.Sessions.Where(s => s.LessonId == lessonId && s.ImportedFrom != null).OrderByDescending(s => s.CreatedUtc).FirstOrDefault();
        bool update = SmokeLessonUpdate ?? false;
        if (SmokeLessonUpdate == null)
        {
            var items = (m["items"] as JsonArray ?? m["tabs"] as JsonArray ?? new()).OfType<JsonObject>().ToList();
            var videos = (m["videos"] as JsonArray ?? new()).OfType<JsonObject>().ToList();
            using var kit = new FormKit("📂 افتح حصة", 560);
            kit.Header(name);
            string? cls = (string?)m["class"];
            var info = new List<string>();
            if (cls != null) info.Add("المجموعة: " + cls);
            info.Add($"{items.Count} ملف (كتب وسبورات ودفتر وعروض)");
            int files = videos.Count(v => v["file"] != null), links = videos.Count(v => v["url"] != null);
            if (files > 0) info.Add($"{files} فيديو");
            if (links > 0) info.Add($"{links} رابط يوتيوب");
            kit.Note(string.Join("  ·  ", info) + $"\nحجم الملف {YtDlp.FormatSize(new FileInfo(zip).Length)}", 40, Theme.Text);
            if ((string?)m["homework"] is { Length: > 0 } hw) kit.Note("الواجب: " + FirstLine(hw), 22, Theme.Text);
            ComboBox? choice = null;
            if (existing != null)
            {
                kit.Header("الحصة موجودة عندك");
                choice = kit.Row("افتحها", FormKit.Style(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }));
                choice.Items.AddRange(new object[] { "حدّث الموجودة (تحل النسخة الجديدة محلها)", "نسخة جديدة بجانبها" });
                choice.SelectedIndex = 0;
                kit.Note("التحديث يزيل النسخة القديمة من المكتبة فقط — ملفاتها وشرحك عليها تبقى في مجلدها.");
            }
            kit.Note("تنزل الكتب والسبورات والدفتر في رف باسم الحصة داخل «حصصي»، والفيديوهات والروابط في دورة «حصصي».");
            kit.Buttons("📂  افتح الحصة");
            if (SmokeShowDialog(kit, "lesson-open")) return null;
            if (kit.Show(this) != DialogResult.OK) return null;
            update = choice?.SelectedIndex == 0;
        }
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "مسار", LessonsInbox);
        var r = await ReceiveSharedShelfAsync(zip, (string?)m["kind"] == "lesson" ? LessonsShelf() : null, SmokeLessonRoot ?? root, openSession: true);
        var s = r != null ? LastImportedSession : null;
        if (s != null && update && existing != null && existing != s) RemoveImportedLesson(existing);
        return s;
    }

    /// <summary>للفحص: مكان فك الحصص بدل «المستندات».</summary>
    internal string? SmokeLessonRoot;

    /// <summary>يشيل نسخة مستلَمة قديمة من المكتبة (الحصة ورفها وكتبها ومحاضرتها) — الملفات تبقى على الجهاز.</summary>
    private void RemoveImportedLesson(StudySession old)
    {
        if (_sessionId == old.Id) EndSession(markDone: false);
        var bookIds = (old.Items ?? old.Tabs).Where(t => t.Kind == WorkTab.BookKind).Select(t => t.Ref).ToHashSet();
        var books = _library.Books.Where(b => bookIds.Contains(b.Id)).ToList();
        var shelves = books.Select(b => _library.ShelfOf(b)).OfType<Shelf>().Distinct()
            .Where(sh => sh.Kind == null && _library.Books.Where(b => b.ShelfId == sh.Id).All(b => bookIds.Contains(b.Id))).ToList();
        foreach (var b in books) { _library.Books.Remove(b); _deletedKeys.Add(LibrarySync.BookKey(b)); }
        foreach (var sh in shelves) { _library.Shelves.Remove(sh); _deletedKeys.Add(LibrarySync.ShelfKey(sh)); }
        if (LessonGroup(old, create: false) is { } g && _library.FindCourseOf(g) is { } c)
        {
            c.Groups.Remove(g);
            _deletedKeys.Add(LibrarySync.GroupKey(g));
            if (c.Groups.Count == 0) { _library.Courses.Remove(c); _deletedKeys.Add(LibrarySync.CourseKey(c)); }
        }
        _tabs.RemoveAll(t => t.Kind == WorkTab.BookKind && bookIds.Contains(t.Ref));
        _library.Sessions.Remove(old);
        _deletedKeys.Add(LibrarySync.SessionKey(old));
        MarkDirty();
        RebuildShelf();
        RebuildTree();
        TabsChanged();
    }

    /// <summary>
    /// بعد فك الحزمة: الحصة (v2) بمحتوياتها وسبورتها ودفترها وفيديوهاتها وروابطها، أو الجلسة القديمة (v1) بتبويبات كتبها.
    /// </summary>
    private StudySession? ImportSessionFromZip(string zipPath, ShelfShare.ImportResult r)
    {
        try
        {
            var m = PeekSessionManifest(zipPath);
            if (m == null) return null;
            bool lesson = (string?)m["kind"] == "lesson";
            var s = new StudySession
            {
                Name = (string?)m["name"] ?? r.Name,
                Plan = (string?)m["plan"],
                Homework = (string?)m["homework"]
            };
            string? Map(string? old) => old != null && r.IdMap.TryGetValue(old, out var id) && r.Books.Any(b => b.Id == id) ? id : null;
            foreach (var t in (m["items"] as JsonArray ?? m["tabs"] as JsonArray ?? new()).OfType<JsonObject>())
            {
                if ((string?)t["kind"] != WorkTab.BookKind || Map((string?)t["ref"]) is not { } id) continue;
                var tab = new WorkTab { Kind = WorkTab.BookKind, Ref = id, Title = (string?)t["title"], Page = (int?)t["page"] };
                if (s.Tabs.All(x => x.Ref != id)) s.Tabs.Add(tab);
            }
            if (lesson)
            {
                s.Lesson = true;
                s.LessonId = (string?)m["lessonId"];
                s.ImportedFrom = Path.GetFileName(zipPath);
                s.Items = s.Tabs.Select(t => t.Copy()).ToList();
                s.BoardId = Map((string?)m["board"]);
                s.NotebookId = Map((string?)m["notebook"]);
                if (s.NotebookId != null && r.Books.FirstOrDefault(b => b.Id == s.NotebookId) is { } nb) s.NotebookSection = NotebookFirstSection(nb);
                // الفيديوهات (فُكّت جنب الكتب في «videos/») والروابط والتسجيلات بعلاماتها
                string root = Path.GetFullPath(r.Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var added = new List<VideoItem>();
                foreach (var v in (m["videos"] as JsonArray ?? new()).OfType<JsonObject>())
                {
                    string? path = null;
                    if ((string?)v["file"] is { Length: > 0 } f)
                    {
                        string full = Path.GetFullPath(Path.Combine(root, f.Replace('/', Path.DirectorySeparatorChar)));
                        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) path = full;
                    }
                    else if ((string?)v["url"] is { } u && MediaFiles.IsUrl(u) && (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))) path = u;
                    if (path == null) continue;
                    var item = new VideoItem { Path = path, Title = (string?)v["title"] ?? Path.GetFileNameWithoutExtension(path), DurationMs = (long?)v["durationMs"] ?? 0 };
                    foreach (var bm in (v["bookmarks"] as JsonArray ?? new()).OfType<JsonObject>())
                        item.Bookmarks.Add(new Bookmark { TimeMs = (long?)bm["ms"] ?? 0, Note = (string?)bm["note"] ?? "" });
                    LessonGroup(s, create: true)!.Videos.Add(item);
                    if ((bool?)v["recording"] == true) s.Recordings.Add(path);
                    if (!item.IsOnline) added.Add(item);
                }
                if (added.Count > 0) QueueProbe(added);
            }
            try { File.Delete(Path.Combine(r.Folder, SessionManifest)); } catch { }
            _library.Sessions.Add(s);
            MarkDirty();
            return s;
        }
        catch (Exception ex) { Log.Error("import session", ex); return null; }
    }
}
