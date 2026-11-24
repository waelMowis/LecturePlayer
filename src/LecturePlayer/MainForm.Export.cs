using System.Text.Json;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// «📤 حفظ في مكتبة جديدة»: نسخة من موضوع (أو مصدر أو كورس) في ملف مكتبة لوحده — تبعته لحد يفتحه في «مسار».
/// الأصل بيفضل زي ما هو. الافتراضي نسخة «للمشاركة»: من غير تقدّمك وملاحظاتك وحاجات جهازك.
/// </summary>
internal sealed partial class MainForm
{
    /// <summary>اختيارات النسخة.</summary>
    /// <param name="WithProgress">تقدّمي وملاحظاتي وعلاماتي وخطتي (نسخة ليا، مش للمشاركة).</param>
    /// <param name="OnlineOnly">روابط الإنترنت بس (ملفات جهازي مش هتشتغل عند حد تاني).</param>
    internal sealed record ExportOptions(bool WithProgress, bool OnlineOnly);

    private ToolStripMenuItem ExportMenuItem(object item) => MenuItem("📤  حفظ في مكتبة جديدة (للمشاركة)…", () => ExportToNewLibrary(item));

    /// <summary>للفحص التلقائي: الشاشة بتختار دول وتحفظ في المسار ده لوحدها.</summary>
    internal static (ExportOptions Options, string Path)? SmokeExport;

    private void ExportToNewLibrary(object item)
    {
        if (!EnsureLibrary()) return;
        string name = item switch { Topic t => t.Name, Place p => p.Name, Course c => c.Name, _ => "" };
        if (name.Length == 0) return;
        var all = ExportCourses(_library, item).SelectMany(c => c.AllVideos).ToList();
        int online = all.Count(v => v.IsOnline), local = all.Count - online;
        if (all.Count == 0 && ExportCourses(_library, item).Count == 0) { ShowStatus("مفيش كورسات هنا تتحفظ"); return; }

        ExportOptions options;
        string? path;
        if (SmokeExport is { } smoke) { options = smoke.Options; path = smoke.Path; }
        else
        {
            using var kit = new FormKit($"حفظ «{name}» في مكتبة جديدة", 540);
            kit.Header("📤  نسخة لوحدها تبعتها لحد");
            kit.Note($"{ExportCourses(_library, item).Count} كورس • {all.Count} فيديو" +
                     (online > 0 ? $" — يوتيوب/إنترنت {online}" : "") + (local > 0 ? $" — ملفات على جهازك {local}" : "") +
                     "\nالأصل بيفضل عندك زي ما هو.", 44);
            var onlyOnline = local > 0 && online > 0
                ? kit.Full(new ToggleSwitch { Text = $"روابط الإنترنت بس (الـ {local} ملف اللي على جهازك مش هيشتغلوا عند حد تاني)", Checked = true }, 32)
                : null;
            if (local > 0 && online == 0)
                kit.Note("⚠ كل الفيديوهات ملفات على جهازك — اللي هتبعتله محتاج نفس الملفات عنده.", 24, Theme.Folder);
            var progress = kit.Full(new ToggleSwitch { Text = "معاها تقدّمي وملاحظاتي وعلاماتي (نسخة ليا، مش للمشاركة)", Checked = false }, 32);
            kit.Note("الملف صغير: ابعته واتساب أو إيميل، واللي يستلمه يفتحه دبل كليك أو من «ملف ← فتح مكتبة».", 34, Theme.Muted);
            kit.Buttons("حفظ…", "إلغاء");
            if (kit.Show(this) != DialogResult.OK) return;
            options = new ExportOptions(progress.Checked, onlyOnline?.Checked == true);

            using var dlg = new SaveFileDialog
            {
                Title = "حفظ المكتبة الجديدة",
                Filter = $"مكتبة مسار (*{Storage.LibraryExtension})|*{Storage.LibraryExtension}",
                FileName = NotesExporter.SafeFileName(name) + Storage.LibraryExtension,
                InitialDirectory = Directory.Exists(_settings.ExportFolder) ? _settings.ExportFolder : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                OverwritePrompt = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
            _settings.ExportFolder = Path.GetDirectoryName(path) ?? "";
            SaveSettings();
        }

        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_libraryPath), StringComparison.OrdinalIgnoreCase))
        {
            Dialogs.Error(this, "ده ملف المكتبة المفتوحة نفسها — اختار اسم تاني.");
            return;
        }
        try
        {
            var export = BuildExport(_library, item, options);
            Storage.SaveLibrary(path, export);
            // نتأكد إن اللي اتحفظ بيتقري وكامل
            var check = Storage.LoadLibrary(path);
            if (check.Courses.Count != export.Courses.Count || check.VideoCount != export.VideoCount)
                throw new InvalidOperationException("الملف اتحفظ ناقص.");
            Log.Info($"exported «{name}» → {path}: {export.Courses.Count} courses, {export.VideoCount} videos, progress {options.WithProgress}, online-only {options.OnlineOnly}");
            LastExport = path;
            ShowBanner($"اتحفظت «{Path.GetFileName(path)}» ✓ — {export.Courses.Count} كورس، {export.VideoCount} فيديو", BannerKind.Success,
                "افتح الفولدر", () => RevealFile(path), seconds: 15, key: "export");
        }
        catch (Exception ex)
        {
            Log.Error("export", ex);
            Dialogs.Error(this, "مقدرتش أحفظ المكتبة الجديدة:\n" + ex.Message);
        }
    }

    internal string? LastExport { get; private set; }

    private static List<Course> ExportCourses(CourseLibrary lib, object item) => item switch
    {
        Topic t => lib.CoursesOf(t).ToList(),
        Place p => lib.AllCoursesUnder(p).ToList(),
        Course c => new List<Course> { c },
        _ => new List<Course>()
    };

    /// <summary>
    /// المكتبة الجديدة: نسخة عميقة من الجزء ده بس (الأصل مابيتلمسش).
    /// المصدر أو الكورس لوحده بيبقى في أول المكتبة (من غير الموضوع اللي كان فيه).
    /// </summary>
    internal static CourseLibrary BuildExport(CourseLibrary lib, object item, ExportOptions o)
    {
        // نسخة عميقة بنفس شكل الحفظ: أي حقل جديد بيتنسخ لوحده
        T Clone<T>(T x) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(x, Storage.JsonOptions), Storage.JsonOptions)!;

        var result = new CourseLibrary();
        var places = new List<Place>();
        switch (item)
        {
            case Topic t:
                result.Name = t.Name;
                result.Topics.Add(Clone(t));
                places.AddRange(lib.PlacesOf(t).Select(Clone));
                break;
            case Place p:
                result.Name = p.Name;
                var root = Clone(p);
                root.TopicId = null;
                root.ParentId = null;
                places.Add(root);
                foreach (var child in lib.ChildrenOf(p)) { var c = Clone(child); c.TopicId = null; places.Add(c); }
                break;
            case Course c:
                result.Name = c.Name;
                break;
        }
        result.Places.AddRange(places);
        foreach (var src in ExportCourses(lib, item))
        {
            var c = Clone(src);
            if (item is Place) c.TopicId = null;
            if (item is Course) { c.TopicId = null; c.PlaceId = null; }
            result.Courses.Add(c);
        }

        foreach (var c in result.Courses)
        {
            if (o.OnlineOnly)
            {
                foreach (var g in c.Groups) g.Videos.RemoveAll(v => !v.IsOnline);
                c.Groups.RemoveAll(g => g.Videos.Count == 0);
            }
            if (o.WithProgress) continue;
            // نسخة للمشاركة: تقدّمي وخطتي وحاجات جهازي بتفضل عندي
            c.Status = CourseStatus.Active;
            c.Priority = 0;
            c.Goal = null;
            c.Deadline = null;
            c.StudyDays.Clear();
            c.Rate = null;
            c.SourceFolder = null;
            foreach (var g in c.Groups)
            {
                g.SourceFolder = null;
                g.Attachments.RemoveAll(a => !MediaFiles.IsUrl(a.Target));
                foreach (var v in g.Videos)
                {
                    v.Watched = false;
                    v.LastPositionMs = 0;
                    v.Notes = null;
                    v.Bookmarks.Clear();
                    v.IsNew = false;
                    v.UpdatedUtc = default;
                }
            }
        }
        if (o.OnlineOnly) result.Courses.RemoveAll(c => c.Groups.Count == 0);

        if (o.WithProgress)
        {
            // الفولدرات المتابَعة اللي جوه الجزء ده تتنقل معاه (عشان المزامنة تكمّل في المكتبة الجديدة)
            var placeIds = places.Select(p => p.Id).ToHashSet();
            foreach (var link in lib.Links)
            {
                bool inside = item switch
                {
                    Topic t => link.TopicId == t.Id,
                    Place => link.PlaceId != null && placeIds.Contains(link.PlaceId),
                    _ => false
                };
                if (!inside) continue;
                var l = Clone(link);
                if (item is Place) l.TopicId = null;
                result.Links.Add(l);
            }
        }
        else
        {
            foreach (var t in result.Topics) t.SourceFolder = null;
            foreach (var p in result.Places) p.SourceFolders = null;
        }
        return result;
    }
}

internal sealed partial class MainForm
{
    internal void RunExportChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("export: " + what); }
        string dir = Path.Combine(Path.GetTempPath(), "masar-smoke-export");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        string localFile = Path.Combine(dir, "local lecture.mp4");
        File.WriteAllBytes(localFile, new byte[16]);

        // مكتبة: «YouTube» (مصدر ← مصدر فرعي ← كورس، وكورس مباشر فيه ملف محلي)، و«Other» جنبه
        var lib = new CourseLibrary { Name = "Main" };
        var yt = new Topic { Name = "YouTube", IconKey = "video_clip", ColorHex = "#E86870", SourceFolder = @"D:\Courses" };
        var other = new Topic { Name = "Other" };
        lib.Topics.AddRange(new[] { yt, other });
        var chan = new Place { Name = "Channel A", TopicId = yt.Id, SourceFolders = new() { @"D:\Courses\A" } };
        var pl = new Place { Name = "Playlist 1", TopicId = yt.Id, ParentId = chan.Id };
        lib.Places.AddRange(new[] { chan, pl });
        var g1 = new LectureGroup { Name = "Part 1", SourceFolder = @"D:\x" };
        g1.Videos.Add(new VideoItem { Path = "https://www.youtube.com/watch?v=aaaaaaaaaaa", Title = "V1", DurationMs = 600_000, Watched = true, LastPositionMs = 590_000, Notes = "my note" });
        g1.Videos[0].Bookmarks.Add(new Bookmark { TimeMs = 1000, Note = "bm" });
        g1.Videos.Add(new VideoItem { Path = "https://www.youtube.com/watch?v=bbbbbbbbbbb", Title = "V2", DurationMs = 300_000, LastPositionMs = 120_000 });
        g1.Attachments.Add(new Attachment { Name = "slides", Target = Path.Combine(dir, "slides.pdf") });
        g1.Attachments.Add(new Attachment { Name = "repo", Target = "https://github.com/x/y" });
        var c1 = new Course { Name = "C1 in playlist", TopicId = yt.Id, PlaceId = pl.Id, Goal = "finish", Deadline = DateTime.Today.AddDays(9), Priority = 3, Rate = 1.5f, IconKey = "code" };
        c1.StudyDays.Add(DayOfWeek.Sunday);
        c1.Groups.Add(g1);
        var g2 = new LectureGroup { Name = "Mixed" };
        g2.Videos.Add(new VideoItem { Path = "https://www.youtube.com/watch?v=ccccccccccc", Title = "V3", DurationMs = 100_000 });
        g2.Videos.Add(new VideoItem { Path = localFile, Title = "Local", DurationMs = 100_000, Watched = true });
        var c2 = new Course { Name = "C2 direct", TopicId = yt.Id };
        c2.Groups.Add(g2);
        var g3 = new LectureGroup { Name = "Only local" };
        g3.Videos.Add(new VideoItem { Path = localFile, Title = "Local 2" });
        var c2b = new Course { Name = "C2b local only", TopicId = yt.Id };
        c2b.Groups.Add(g3);
        var g4 = new LectureGroup { Name = "O" };
        g4.Videos.Add(new VideoItem { Path = "https://www.youtube.com/watch?v=ddddddddddd", Title = "V4" });
        var c3 = new Course { Name = "C3 other", TopicId = other.Id };
        c3.Groups.Add(g4);
        lib.Courses.AddRange(new[] { c1, c2, c2b, c3 });
        lib.Links.Add(new LinkedFolder { Path = @"D:\Courses\A", TopicId = yt.Id, PlaceId = chan.Id });
        lib.Links.Add(new LinkedFolder { Path = @"D:\Other", TopicId = other.Id });
        string mainPath = Path.Combine(dir, "Main.lpl");
        Storage.SaveLibrary(mainPath, lib);
        LoadLibrary(mainPath, remember: false);
        // الفتح بيحوّل فولدر الموضوع لفولدر متابَع (MigrateLinks): نقارن بعد الفتح
        int linksAtLoad = _library.Links.Count;
        var ytLinks = _library.Links.Where(l => l.TopicId == _library.Topics.Single(x => x.Name == "YouTube").Id).Select(l => l.Path).OrderBy(x => x).ToList();
        Topic T(string n) => _library.Topics.Single(x => x.Name == n);
        Place P(string n) => _library.Places.Single(x => x.Name == n);
        Course C(string n) => _library.Courses.Single(x => x.Name == n);

        CourseLibrary Export(object item, ExportOptions o, string file)
        {
            string path = Path.Combine(dir, file);
            SmokeExport = (o, path);
            try { ExportToNewLibrary(item); } finally { SmokeExport = null; }
            Check(File.Exists(path) && LastExport == path, "file saved: " + file);
            return Storage.LoadLibrary(path);
        }

        // 1) للمشاركة (الافتراضي): روابط بس، من غير تقدّمي
        var share = Export(T("YouTube"), new ExportOptions(false, true), "YouTube.lpl");
        Check(share.Name == "YouTube" && share.Topics.Count == 1 && share.Topics[0].IconKey == "video_clip" && share.Topics[0].ColorHex == "#E86870", "topic + look");
        Check(share.Topics[0].SourceFolder == null && share.Places.All(p => p.SourceFolders == null), "no local folder paths");
        Check(share.Places.Count == 2 && share.ParentOf(share.Places.Single(p => p.Name == "Playlist 1"))?.Name == "Channel A", "nested sources kept");
        Check(share.Courses.Select(c => c.Name).OrderBy(n => n).SequenceEqual(new[] { "C1 in playlist", "C2 direct" }), "courses: " + string.Join(",", share.Courses.Select(c => c.Name)));
        var sc1 = share.Courses.Single(c => c.Name == "C1 in playlist");
        Check(share.PlaceOf(sc1)?.Name == "Playlist 1" && share.TopicOf(sc1)?.Name == "YouTube" && sc1.IconKey == "code", "course stays in its source");
        Check(share.AllVideos.All(v => !v.Watched && v.LastPositionMs == 0 && v.Notes == null && v.Bookmarks.Count == 0), "no progress/notes/bookmarks");
        Check(sc1.Goal == null && sc1.Deadline == null && sc1.StudyDays.Count == 0 && sc1.Priority == 0 && sc1.Rate == null, "no personal plan");
        Check(share.AllVideos.All(v => v.IsOnline) && share.VideoCount == 3, "online only: " + share.VideoCount);
        Check(share.AllVideos.Single(v => v.Title == "V1").DurationMs == 600_000, "durations kept");
        Check(sc1.Groups[0].Attachments.Count == 1 && sc1.Groups[0].Attachments[0].Target.StartsWith("https"), "only online attachments");
        Check(share.Links.Count == 0 && share.Live.Count == 0, "no followed folders");
        Check(!share.Topics.Any(t => t.Name == "Other") && !share.Courses.Any(c => c.Name == "C3 other"), "other topic not included");

        // الأصل ماتلمسش
        SaveLibrary();
        Check(C("C1 in playlist").AllVideos.First().Watched && C("C2 direct").VideoCount == 2 && _library.Topics.Count == 2 && _library.Links.Count == linksAtLoad,
            $"original unchanged: watched {C("C1 in playlist").AllVideos.First().Watched}, C2 videos {C("C2 direct").VideoCount}, topics {_library.Topics.Count}, links {_library.Links.Count} ({string.Join(" | ", _library.Links.Select(l => l.Path))})");
        Check(C("C1 in playlist").Goal == "finish" && C("C1 in playlist").Deadline != null && C("C1 in playlist").AllVideos.First().Notes == "my note", "original plan/notes kept");
        Check(_library.Links.Select(l => l.Path).OrderBy(x => x).SequenceEqual(ytLinks.Append(@"D:\Other").OrderBy(x => x)), "original followed folders kept");

        // 2) نسخة ليا: بكل حاجة (التقدم والملفات المحلية والفولدر المتابَع بتاع الموضوع بس)
        var mine = Export(T("YouTube"), new ExportOptions(true, false), "YouTube (mine).lpl");
        Check(mine.VideoCount == 5 && mine.AllVideos.Single(v => v.Title == "V1").Watched && mine.AllVideos.Single(v => v.Title == "V1").Notes == "my note", "progress kept");
        Check(mine.Courses.Single(c => c.Name == "C1 in playlist").Goal == "finish", "plan kept");
        Check(mine.Links.Select(l => l.Path).OrderBy(x => x).SequenceEqual(ytLinks) && !mine.Links.Any(l => l.Path == @"D:\Other"),
            "only this topic's followed folders: " + string.Join(" | ", mine.Links.Select(l => l.Path)));

        // 3) مصدر لوحده: بيبقى في أول المكتبة بمصادره الفرعية
        var src = Export(P("Channel A"), new ExportOptions(false, true), "Channel A.lpl");
        var root = src.Places.Single(p => p.Name == "Channel A");
        Check(src.Topics.Count == 0 && root.TopicId == null && root.ParentId == null, "source at top");
        Check(src.ParentOf(src.Places.Single(p => p.Name == "Playlist 1")) == root, "sub-source nested");
        Check(src.Courses.Count == 1 && src.PlaceOf(src.Courses[0])?.Name == "Playlist 1", "course under sub-source");

        // 4) كورس لوحده
        var one = Export(C("C2 direct"), new ExportOptions(false, false), "C2.lpl");
        Check(one.Courses.Count == 1 && one.Courses[0].TopicId == null && one.Courses[0].PlaceId == null && one.VideoCount == 2, "single course");

        // 5) اللي استلمها بيفتحها: الموضوع ومصادره في الشجرة
        LoadLibrary(Path.Combine(dir, "YouTube.lpl"), remember: false);
        Check(_library.Name == "YouTube" && NodeOf(_library.Topics[0]) != null && NodeOf(_library.Courses.Single(c => c.Name == "C1 in playlist")) != null, "opens in tree");
        LoadLibrary(mainPath, remember: false);

        // 6) القايمة فيها الأمر للتلاتة
        foreach (var o in new object[] { T("YouTube"), P("Channel A"), C("C2 direct") })
        {
            ShowNodeMenu(NodeOf(o)!, Point.Empty);
            bool has = MenuHas(_nodeMenu.Items, "حفظ في مكتبة جديدة");
            _nodeMenu.Close();
            Check(has, "menu item for " + o.GetType().Name);
        }
        _dirty = false;
        Console.WriteLine("SMOKE export OK");
    }
}
