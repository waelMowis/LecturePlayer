namespace LecturePlayer.Data;

/// <summary>
/// «فولدر مربوط»: فولدر كورس على الجهاز (غالبًا Google Drive) البرنامج بيتابعه لوحده.
///
///   الفولدر الرئيسي            ← موضوع (أو يتحط جوه «مصدر»)
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
            if (IsVideosFolderName(System.IO.Path.GetFileName(courseDir))) continue;
            var cs = CourseOf(courseDir);
            if (cs.Sessions.Count > 0) courses.Add(cs);
        }
        return new Snapshot(root, courses);
    }

    /// <summary>فولدر كورس واحد: الفيديوهات اللي على طول فيه (أو في Videos) محاضرة باسمه، وكل فولدر جواه محاضرة.</summary>
    private static CourseSnap CourseOf(string courseDir)
    {
        string courseName = System.IO.Path.GetFileName(courseDir.TrimEnd('\\', '/'));
        var sessions = new List<SessionSnap>();
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
        return new CourseSnap(courseDir, courseName, sessions);
    }

    /// <summary>الفولدر كله كورس واحد (كل فولدر جواه محاضرة).</summary>
    public static Snapshot? ScanSingle(string root) =>
        Directory.Exists(root) ? new Snapshot(root, new List<CourseSnap> { CourseOf(root) }.Where(c => c.Sessions.Count > 0).ToList()) : null;

    public static Snapshot? Scan(string root, bool singleCourse) => singleCourse ? ScanSingle(root) : Scan(root);

    /// <summary>
    /// الفولدر شكله «مجموعة كورسات» (كل فولدر جواه جواه فولدرات فيها فيديوهات) ولا «كورس واحد» (كل فولدر جواه محاضرة)؟
    /// «Session 01» اللي جواها Videos وPresentation محاضرة مش كورس، لأن Videos بيتعد جزء من المحاضرة.
    /// </summary>
    public static bool LooksLikeManyCourses(string root)
    {
        int courseLike = 0, sessionLike = 0;
        foreach (string d in Dirs(root))
        {
            if (IsVideosFolderName(System.IO.Path.GetFileName(d))) continue;
            bool nestedSessions = Dirs(d).Any(sub => !IsVideosFolderName(System.IO.Path.GetFileName(sub)) && FilesUnder(sub).Any(MediaFiles.IsMedia));
            if (nestedSessions) courseLike++;
            else if (FilesUnder(d).Any(MediaFiles.IsMedia)) sessionLike++;
        }
        return courseLike > 0 && courseLike >= sessionLike;
    }

    /// <summary>
    /// آخر أجزاء المسار (مثلًا «01 c# basics\\session 01\\videos\\part 01.mp4»): نفس الملف على Google Drive
    /// ممكن يبان بمسارين (G:\\My Drive\\… و G:\\.shortcut-targets-by-id\\…)، فبنقارن بآخر المسار لما المسار كله مايتطابقش.
    /// </summary>
    /// <summary>
    /// الملف القديم هو نفس الملف الجديد؟ لو القديم مبقاش موجود (المسار اتغير) ← أيوه.
    /// لو الاتنين موجودين (نسختين؟) ← بس لو نفس الحجم ونفس وقت التعديل (نفس الملف من مسارين للدرايف).
    /// </summary>
    public static bool SameFile(string oldPath, string newPath)
    {
        try
        {
            if (!File.Exists(oldPath)) return true;
            var a = new FileInfo(oldPath);
            var b = new FileInfo(newPath);
            return b.Exists && a.Length == b.Length && a.LastWriteTimeUtc == b.LastWriteTimeUtc;
        }
        catch { return false; }
    }

    /// <summary>نفس الفولدر من مسار تاني؟ القديم مبقاش موجود، أو أول فيديو في الجديد موجود في القديم بنفس الملف.</summary>
    public static bool SameFolder(string oldDir, string newDir)
    {
        try
        {
            if (!Directory.Exists(oldDir)) return true;
            string? sample = FilesUnder(newDir).FirstOrDefault(MediaFiles.IsMedia);
            if (sample == null) return false;
            string twin = System.IO.Path.Combine(oldDir, System.IO.Path.GetRelativePath(newDir, sample));
            return File.Exists(twin) && SameFile(twin, sample);
        }
        catch { return false; }
    }

    public static string Tail(string path, int parts)
    {
        var segs = Norm(path).Split('\\', '/').Where(x => x.Length > 0).ToArray();
        return segs.Length < parts ? "" : string.Join("\\", segs[^parts..]);
    }

    // ---------------- تطبيق الصورة على المكتبة (على ثريد الواجهة) ----------------

    public sealed class Result
    {
        public int NewCourses, NewGroups, Moved, NewAttachments, PathsUpdated;
        public List<VideoItem> NewVideos { get; } = new();
        public List<(Course Course, LectureGroup Group)> GroupsWithNew { get; } = new();
        public bool Changed => NewCourses + NewGroups + Moved + NewAttachments + PathsUpdated + NewVideos.Count > 0;
    }

    /// <param name="initial">أول ربط: الفيديوهات مش بتتعلّم «جديد» (كلها جديدة أصلًا).</param>
    public static Result Apply(CourseLibrary lib, Topic topic, Snapshot snap, bool initial) =>
        Apply(lib, topic.Id, null, snap, initial);

    /// <summary>فولدر جوه مصدر: كورساته بتتحط في المصدر على طول.</summary>
    public static Result Apply(CourseLibrary lib, Place place, Snapshot snap, bool initial) =>
        Apply(lib, place.TopicId, place.Id, snap, initial);

    /// <summary>فولدر متابَع: الكورسات الجديدة بتتحط في موضوعه/مصدره الحالي، واللي المستخدم مسحه مش بيرجع.</summary>
    public static Result Apply(CourseLibrary lib, LinkedFolder link, Snapshot snap, bool initial)
    {
        var (t, p) = lib.TargetOf(link);
        return Apply(lib, p?.TopicId ?? t?.Id, p?.Id, snap, initial, link.Ignored);
    }

    /// <param name="ignored">مسارات (كورس/محاضرة/فيديو) المستخدم مسحها من المكتبة: مش بترجع تاني في المزامنة.</param>
    public static Result Apply(CourseLibrary lib, string? topicId, string? placeId, Snapshot snap, bool initial,
        IEnumerable<string>? ignored = null)
    {
        var r = new Result();
        var skip = new HashSet<string>((ignored ?? Enumerable.Empty<string>()).Select(Norm));

        // كل فيديو في المكتبة بمساره، عشان لو كان متضاف قبل كده بإيدك ننقله بتقدّمه بدل ما نكرره
        var byPath = new Dictionary<string, (VideoItem V, LectureGroup G)>();
        // وبآخر المسار (لو نفس الملف اتضاف قبل كده من مسار تاني للدرايف) — بس لو مفيش غير ملف واحد بنفس الآخر
        var byTail = new Dictionary<string, (VideoItem V, LectureGroup G)?>();
        // كل محاضرة جاية من فولدر بمسار الفولدر (في أي كورس: لو نقلتها بإيدك لكورس تاني تفضل هناك)
        var groupByFolder = new Dictionary<string, LectureGroup>();
        foreach (var c in lib.Courses)
            foreach (var g in c.Groups)
            {
                if (g.SourceFolder != null) groupByFolder.TryAdd(Norm(g.SourceFolder), g);
                foreach (var v in g.Videos)
                {
                    if (v.IsOnline) continue;
                    byPath.TryAdd(Norm(v.Path), (v, g));
                    string tail = Tail(v.Path, 4);
                    if (tail.Length > 0) byTail[tail] = byTail.ContainsKey(tail) ? null : (v, g);
                }
            }
        bool TryFind(string file, out (VideoItem V, LectureGroup G) hit)
        {
            if (byPath.TryGetValue(Norm(file), out hit)) return true;
            string tail = Tail(file, 4);
            if (tail.Length > 0 && byTail.TryGetValue(tail, out var t) && t is { } found && SameFile(found.V.Path, file))
            {
                hit = found;
                return true;
            }
            hit = default;
            return false;
        }

        var touchedOld = new HashSet<LectureGroup>();

        foreach (var cs in snap.Courses)
        {
            string cKey = Norm(cs.Path);
            if (skip.Contains(cKey)) continue; // المستخدم مسح الكورس ده
            // الكورس بفولدره، أيًا كان مكانه (لو نقلته بإيدك لمكان تاني يفضل هناك ومايتكررش)،
            // أو بآخر مسار الفولدر لو نفس الفولدر جه من مسار تاني للدرايف
            var course = lib.Courses.FirstOrDefault(c => c.SourceFolder != null && Norm(c.SourceFolder) == cKey);
            if (course == null)
            {
                string ct = Tail(cs.Path, 2);
                var byTailCourse = ct.Length == 0 ? new List<Course>()
                    : lib.Courses.Where(c => c.SourceFolder != null && Tail(c.SourceFolder, 2) == ct && SameFolder(c.SourceFolder, cs.Path)).ToList();
                if (byTailCourse.Count == 1) { course = byTailCourse[0]; course.SourceFolder = cs.Path; }
            }
            if (course == null)
            {
                course = new Course { Name = cs.Name, TopicId = topicId, PlaceId = placeId, SourceFolder = cs.Path, Expanded = initial && r.NewCourses == 0 };
                lib.Courses.Add(course);
                r.NewCourses++;
            }

            foreach (var ss in cs.Sessions)
            {
                string gKey = Norm(ss.Path);
                if (gKey != cKey && skip.Contains(gKey)) continue; // المستخدم مسح المحاضرة دي
                var group = course.Groups.FirstOrDefault(g => g.SourceFolder != null && Norm(g.SourceFolder) == gKey)
                            ?? (groupByFolder.TryGetValue(gKey, out var elsewhere) && lib.FindCourseOf(elsewhere) != null ? elsewhere : null);
                if (group == null && Tail(ss.Path, 2) is { Length: > 0 } gt
                    && course.Groups.Where(g => g.SourceFolder != null && Tail(g.SourceFolder, 2) == gt && SameFolder(g.SourceFolder, ss.Path)).ToList() is { Count: 1 } sameTail)
                {
                    group = sameTail[0];
                    group.SourceFolder = ss.Path;
                }
                if (group == null)
                {
                    group = new LectureGroup { Name = ss.Name, SourceFolder = ss.Path, Expanded = false };
                    course.Groups.Add(group);
                    groupByFolder[gKey] = group;
                    r.NewGroups++;
                }
                var groupCourse = lib.FindCourseOf(group) ?? course;

                var have = new HashSet<string>(group.Videos.Select(v => Norm(v.Path)));
                bool addedHere = false;
                foreach (string file in ss.Videos)
                {
                    string key = Norm(file);
                    if (have.Contains(key) || skip.Contains(key)) continue;
                    if (TryFind(file, out var existing))
                    {
                        // نفس الملف متضاف بالفعل (بإيدك، أو من مسار تاني للدرايف)
                        if (Norm(existing.V.Path) != key) { existing.V.Path = file; r.PathsUpdated++; } // المسار اللي بيشتغل دلوقتي
                        if (initial && existing.G != group)
                        {
                            // أول ربط بس: ننقله لمكانه في ترتيب الفولدر بتقدّمه وعلاماته وملاحظاته.
                            // بعد كده لو نقلته بإيدك لمحاضرة تانية، بيفضل مكان ما حطيته.
                            existing.G.Videos.Remove(existing.V);
                            touchedOld.Add(existing.G);
                            group.Videos.Add(existing.V);
                            byPath[key] = (existing.V, group);
                            have.Add(key);
                            r.Moved++;
                        }
                        else if (initial) r.Moved++;
                        continue;
                    }
                    var nv = VideoItem.FromFile(file);
                    nv.IsNew = !initial;
                    group.Videos.Add(nv);
                    byPath[key] = (nv, group);
                    have.Add(key);
                    r.NewVideos.Add(nv);
                    addedHere = true;
                }
                if (addedHere && !initial) r.GroupsWithNew.Add((groupCourse, group));

                // ترتيب الفيديوهات زي ترتيب الملفات (Part 01, Part 02…) — أول ربط، أو لما ينزل جديد بس
                // (عشان لو رتّبتها بإيدك المزامنة متبوّظش ترتيبك كل دقيقتين)
                if (initial || addedHere)
                {
                    var order = new Dictionary<string, int>();
                    for (int i = 0; i < ss.Videos.Count; i++) order.TryAdd(Norm(ss.Videos[i]), i);
                    group.Videos = group.Videos
                        .OrderBy(v => order.TryGetValue(Norm(v.Path), out int i) ? i : int.MaxValue)
                        .ToList();
                }

                var attached = new HashSet<string>(group.Attachments.Select(a => Norm(a.Target)));
                foreach (string file in ss.Files)
                {
                    if (!attached.Add(Norm(file)) || skip.Contains(Norm(file))) continue;
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
                var keepPaths = new HashSet<string>(keep.Videos.Select(v => Norm(v.Path)));
                foreach (var extra in dup.Skip(1).ToList())
                {
                    foreach (var v in extra.Videos)
                        if (keepPaths.Add(Norm(v.Path))) keep.Videos.Add(v);
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
