namespace LecturePlayer.Data;

/// <summary>
/// «فولدر مربوط»: فولدر كورس على الجهاز (غالبًا Google Drive) البرنامج بيتابعه لوحده.
///
///   الفولدر الرئيسي            ← موضوع
///     كورس صغير (فولدر)        ← كورس
///       Session 01 (فولدر)    ← محاضرة: كل الفيديوهات اللي جواها (حتى لو في فولدر Videos)،
///                               وباقي الملفات (Presentation، Demo Link…) مرفقات
///
/// الفيديوهات الجديدة اللي بتنزل على الدرايف بتظهر لوحدها في مكانها، والتقدّم والعلامات مابيضيعوش.
/// القراية من الديسك (Scan) منفصلة عن التعديل في المكتبة (Apply) عشان القراية تتعمل في الخلفية.
/// </summary>
public static class FolderLink
{
    // ---------------- صورة الفولدر (بتتقري في الخلفية) ----------------

    public sealed record SessionSnap(string Path, string Name, List<string> Videos, List<string> Files);
    public sealed record CourseSnap(string Path, string Name, List<SessionSnap> Sessions);
    public sealed record Snapshot(string Root, List<CourseSnap> Courses);

    private static readonly HashSet<string> IgnoredFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini", "thumbs.db", ".ds_store", "icon\r"
    };

    /// <summary>فولدر اسمه زي «Videos» أو «فيديو»: الفيديوهات اللي فيه تبع الفولدر اللي فوقه مش محاضرة لوحدها.</summary>
    public static bool IsVideosFolderName(string name)
    {
        string n = name.Trim().ToLowerInvariant();
        return n is "videos" or "video" or "recordings" or "recording" or "record" or "records"
            or "فيديو" or "فيديوهات" or "الفيديوهات" or "الفيديو" or "تسجيلات" or "التسجيلات";
    }

    public static string Norm(string path)
    {
        try { return System.IO.Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant(); }
        catch { return path.Trim().TrimEnd('\\', '/').ToLowerInvariant(); }
    }

    private static IEnumerable<string> Dirs(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            })
            .Where(d => !System.IO.Path.GetFileName(d).StartsWith('.'))
            .OrderBy(System.IO.Path.GetFileName, NaturalComparer.Instance)
            .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> FilesUnder(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            })
            .Where(f =>
            {
                string name = System.IO.Path.GetFileName(f);
                return !IgnoredFiles.Contains(name) && !name.StartsWith("~$") && !name.StartsWith('.')
                       && !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(f => System.IO.Path.GetRelativePath(dir, f), NaturalComparer.Instance)
            .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private static SessionSnap SessionOf(string dir, string name, bool recursive)
    {
        var files = recursive ? FilesUnder(dir).ToList() : FilesDirect(dir);
        return new SessionSnap(dir, name,
            files.Where(MediaFiles.IsMedia).ToList(),
            files.Where(f => !MediaFiles.IsMedia(f)).ToList());
    }

    private static List<string> FilesDirect(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir)
                .Where(f => !IgnoredFiles.Contains(System.IO.Path.GetFileName(f)) && !System.IO.Path.GetFileName(f).StartsWith("~$"))
                .OrderBy(System.IO.Path.GetFileName, NaturalComparer.Instance).ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>بيقرا شكل الفولدر من الديسك. null لو الفولدر مش موجود (الدرايف لسه مافتحش مثلًا).</summary>
    public static Snapshot? Scan(string root)
    {
        if (!Directory.Exists(root)) return null;
        var courses = new List<CourseSnap>();

        // فيديوهات على طول في الفولدر الرئيسي ← كورس «عام»
        var rootDirect = SessionOf(root, new DirectoryInfo(root).Name, recursive: false);
        if (rootDirect.Videos.Count > 0)
            courses.Add(new CourseSnap(root, "عام", new List<SessionSnap> { rootDirect }));

        foreach (string courseDir in Dirs(root))
        {
            string courseName = System.IO.Path.GetFileName(courseDir);
            var sessions = new List<SessionSnap>();

            // فيديوهات على طول في فولدر الكورس، أو في فولدر «Videos» جوه الكورس ← محاضرة باسم الكورس
            var direct = SessionOf(courseDir, courseName, recursive: false);
            var videosDirs = Dirs(courseDir).Where(d => IsVideosFolderName(System.IO.Path.GetFileName(d))).ToList();
            var directVideos = direct.Videos.Concat(videosDirs.SelectMany(d => FilesUnder(d).Where(MediaFiles.IsMedia))).ToList();
            if (directVideos.Count > 0)
                sessions.Add(new SessionSnap(courseDir, courseName, directVideos, direct.Files));

            foreach (string sessionDir in Dirs(courseDir))
            {
                if (IsVideosFolderName(System.IO.Path.GetFileName(sessionDir))) continue;
                var s = SessionOf(sessionDir, System.IO.Path.GetFileName(sessionDir), recursive: true);
                if (s.Videos.Count > 0 || s.Files.Count > 0) sessions.Add(s);
            }
            if (sessions.Count > 0) courses.Add(new CourseSnap(courseDir, courseName, sessions));
        }
        return new Snapshot(root, courses);
    }

    // ---------------- تطبيق الصورة على المكتبة (على ثريد الواجهة) ----------------

    public sealed class Result
    {
        public int NewCourses, NewGroups, Moved, NewAttachments;
        public List<VideoItem> NewVideos { get; } = new();
        public List<(Course Course, LectureGroup Group)> GroupsWithNew { get; } = new();
        public bool Changed => NewCourses + NewGroups + Moved + NewAttachments + NewVideos.Count > 0;
    }

    /// <param name="initial">أول ربط: الفيديوهات مش بتتعلّم «جديد» (كلها جديدة أصلًا).</param>
    public static Result Apply(CourseLibrary lib, Topic topic, Snapshot snap, bool initial)
    {
        var r = new Result();
        // كل فيديو في المكتبة بمساره، عشان لو كان متضاف قبل كده بإيدك ننقله بتقدّمه بدل ما نكرره
        var byPath = new Dictionary<string, (VideoItem V, LectureGroup G)>();
        foreach (var c in lib.Courses)
            foreach (var g in c.Groups)
                foreach (var v in g.Videos)
                    byPath.TryAdd(Norm(v.Path), (v, g));

        var touchedOld = new HashSet<LectureGroup>();

        foreach (var cs in snap.Courses)
        {
            string cKey = Norm(cs.Path);
            var course = lib.Courses.FirstOrDefault(c => c.TopicId == topic.Id && c.SourceFolder != null && Norm(c.SourceFolder) == cKey);
            if (course == null)
            {
                course = new Course { Name = cs.Name, TopicId = topic.Id, SourceFolder = cs.Path, Expanded = initial && r.NewCourses == 0 };
                lib.Courses.Add(course);
                r.NewCourses++;
            }

            foreach (var ss in cs.Sessions)
            {
                string gKey = Norm(ss.Path);
                var group = course.Groups.FirstOrDefault(g => g.SourceFolder != null && Norm(g.SourceFolder) == gKey);
                if (group == null)
                {
                    group = new LectureGroup { Name = ss.Name, SourceFolder = ss.Path, Expanded = false };
                    course.Groups.Add(group);
                    r.NewGroups++;
                }

                bool addedHere = false;
                foreach (string file in ss.Videos)
                {
                    string key = Norm(file);
                    if (group.Videos.Any(v => Norm(v.Path) == key)) continue;
                    if (byPath.TryGetValue(key, out var existing) && existing.G != group)
                    {
                        // كان متضاف بإيدك في مكان تاني: ننقله هو نفسه (بمكانه وعلاماته وملاحظاته)
                        existing.G.Videos.Remove(existing.V);
                        touchedOld.Add(existing.G);
                        group.Videos.Add(existing.V);
                        byPath[key] = (existing.V, group);
                        r.Moved++;
                        continue;
                    }
                    var nv = VideoItem.FromFile(file);
                    nv.IsNew = !initial;
                    group.Videos.Add(nv);
                    byPath[key] = (nv, group);
                    r.NewVideos.Add(nv);
                    addedHere = true;
                }
                if (addedHere && !initial) r.GroupsWithNew.Add((course, group));

                // ترتيب الفيديوهات زي ترتيب الملفات (Part 01, Part 02…)
                var order = ss.Videos.Select((f, i) => (Norm(f), i)).ToDictionary(x => x.Item1, x => x.i);
                group.Videos = group.Videos
                    .OrderBy(v => order.TryGetValue(Norm(v.Path), out int i) ? i : int.MaxValue)
                    .ToList();

                foreach (string file in ss.Files)
                {
                    if (group.Attachments.Any(a => Norm(a.Target) == Norm(file))) continue;
                    string rel = System.IO.Path.GetRelativePath(ss.Path, file);
                    string name = System.IO.Path.GetFileNameWithoutExtension(rel);
                    string? folder = System.IO.Path.GetDirectoryName(rel);
                    group.Attachments.Add(new Attachment
                    {
                        Name = string.IsNullOrEmpty(folder) ? name : $"{folder} › {name}",
                        Target = file
                    });
                    r.NewAttachments++;
                }
            }

            // نفس الفولدر اتعمل محاضرتين (مثلًا على جهازين قبل الدمج): نجمعهم في واحدة
            foreach (var dup in course.Groups.Where(g => g.SourceFolder != null)
                         .GroupBy(g => Norm(g.SourceFolder!)).Where(x => x.Count() > 1).ToList())
            {
                var keep = dup.First();
                foreach (var extra in dup.Skip(1).ToList())
                {
                    foreach (var v in extra.Videos)
                        if (!keep.Videos.Any(k => Norm(k.Path) == Norm(v.Path))) keep.Videos.Add(v);
                    course.Groups.Remove(extra);
                }
            }
        }

        // محاضرات قديمة (اتضافت بإيدك) فضيت بعد ما فيديوهاتها اتنقلت ← تتشال، وكورساتها لو فضيت
        foreach (var old in touchedOld.Where(g => g.Videos.Count == 0 && g.SourceFolder == null))
        {
            var owner = lib.FindCourseOf(old);
            owner?.Groups.Remove(old);
            if (owner != null && owner.Groups.Count == 0 && owner.SourceFolder == null) lib.Courses.Remove(owner);
        }
        return r;
    }
}
