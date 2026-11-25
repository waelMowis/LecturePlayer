namespace LecturePlayer.Data;

/// <summary>
/// الدمج بين الأجهزة: لما ملف القائمة (على OneDrive مثلًا) يتغير من جهاز تاني
/// وإحنا فاتحينه، بندمج اللي على الديسك مع اللي في الذاكرة بدل ما حد يمسح شغل التاني.
///
/// القواعد:
/// • حالة الفيديو (مكان الاستكمال، المشاهدة، الملاحظات، العلامات، المدة): الأحدث (UpdatedUtc) يكسب.
/// • كورس أو محاضرة أو فيديو اتضاف على الجهاز التاني: بيتضاف هنا.
/// • حاجة اتمسحت هنا في الجلسة دي: مبترجعش من الجهاز التاني (deletedKeys).
/// • الهيكل (الترتيب والأسماء): اللي هنا هو اللي بيتحفظ.
/// </summary>
public static class LibrarySync
{
    public sealed class Result
    {
        public int UpdatedVideos;
        public int AddedCourses;
        public int AddedGroups;
        public int AddedVideos;
        public int UpdatedInfo;
        public int AddedTopics;
        public int AddedPlaces;
        public int AddedLive;
        public int AddedBooks;
        public int UpdatedBooks;
        public int Total => UpdatedVideos + AddedCourses + AddedGroups + AddedVideos + UpdatedInfo + AddedTopics + AddedPlaces + AddedLive + AddedBooks + UpdatedBooks;
    }

    public static string VideoKey(VideoItem v) => "v:" + v.Path.ToUpperInvariant();
    public static string GroupKey(LectureGroup g) => "g:" + g.Id;
    public static string CourseKey(Course c) => "c:" + c.Id;
    public static string TopicKey(Topic t) => "t:" + t.Id;
    public static string PlaceKey(Place p) => "p:" + p.Id;
    public static string LinkKey(LinkedFolder l) => "k:" + l.Id;
    public static string LiveKey(LiveChannel l) => "l:" + l.Id;
    public static string ShelfKey(Shelf s) => "s:" + s.Id;
    public static string BookKey(Book b) => "b:" + b.Id;

    public static Result Merge(CourseLibrary local, CourseLibrary remote, ISet<string>? deletedKeys = null)
    {
        var result = new Result();
        deletedKeys ??= new HashSet<string>();

        var localVideos = new Dictionary<string, VideoItem>(StringComparer.Ordinal);
        foreach (var v in local.AllVideos) localVideos.TryAdd(VideoKey(v), v);

        // 1) حالة الفيديوهات الموجودة عند الاتنين
        foreach (var rv in remote.AllVideos)
        {
            if (!localVideos.TryGetValue(VideoKey(rv), out var lv)) continue;
            if (rv.UpdatedUtc <= lv.UpdatedUtc) continue;
            CopyState(rv, lv);
            result.UpdatedVideos++;
        }

        // 1.5) المواضيع: الجديدة بتتضاف، والاسم الأحدث يكسب
        foreach (var rt in remote.Topics)
        {
            if (deletedKeys.Contains(TopicKey(rt))) continue;
            var lt = local.Topics.FirstOrDefault(t => t.Id == rt.Id);
            if (lt == null) { local.Topics.Add(rt); result.AddedTopics++; }
            else if (rt.InfoUpdatedUtc > lt.InfoUpdatedUtc) { lt.Name = rt.Name; lt.IconKey = rt.IconKey; lt.ColorHex = rt.ColorHex; lt.SeparatorBefore = rt.SeparatorBefore; lt.SeparatorTitle = rt.SeparatorTitle; lt.InfoUpdatedUtc = rt.InfoUpdatedUtc; result.UpdatedInfo++; }
        }

        // 1.55) الأماكن: الجديدة بتتضاف، والأحدث (الاسم والموضوع والفولدرات) يكسب
        foreach (var rp in remote.Places ?? new List<Place>())
        {
            if (deletedKeys.Contains(PlaceKey(rp))) continue;
            var lp = local.Places.FirstOrDefault(p => p.Id == rp.Id);
            if (lp == null) { local.Places.Add(rp); result.AddedPlaces++; }
            else if (rp.InfoUpdatedUtc > lp.InfoUpdatedUtc)
            {
                lp.Name = rp.Name; lp.IconKey = rp.IconKey; lp.ColorHex = rp.ColorHex; lp.TopicId = rp.TopicId; lp.ParentId = rp.ParentId;
                lp.InfoUpdatedUtc = rp.InfoUpdatedUtc; result.UpdatedInfo++;
            }
        }

        // 1.57) الفولدرات المتابَعة
        foreach (var rk in remote.Links ?? new List<LinkedFolder>())
        {
            if (deletedKeys.Contains(LinkKey(rk))) continue;
            var lk = local.Links.FirstOrDefault(x => x.Id == rk.Id)
                     ?? local.Links.FirstOrDefault(x => FolderLink.Norm(x.Path) == FolderLink.Norm(rk.Path));
            if (lk == null) { local.Links.Add(rk); result.UpdatedInfo++; }
            else if (rk.InfoUpdatedUtc > lk.InfoUpdatedUtc)
            {
                lk.Path = rk.Path; lk.SingleCourse = rk.SingleCourse; lk.TopicId = rk.TopicId; lk.PlaceId = rk.PlaceId; lk.Ignored = (rk.Ignored ?? new()).ToList();
                lk.InfoUpdatedUtc = rk.InfoUpdatedUtc; result.UpdatedInfo++;
            }
        }

        // 1.6) البث المباشر: القنوات الجديدة بتتضاف، والتعديل الأحدث يكسب
        foreach (var rl in remote.Live ?? new List<LiveChannel>())
        {
            if (deletedKeys.Contains(LiveKey(rl))) continue;
            var ll = local.Live.FirstOrDefault(x => x.Id == rl.Id);
            if (ll == null) { local.Live.Add(rl); result.AddedLive++; }
            else if (rl.InfoUpdatedUtc > ll.InfoUpdatedUtc)
            {
                ll.Name = rl.Name; ll.Url = rl.Url; ll.Kind = rl.Kind; ll.Group = rl.Group; ll.Tags = rl.Tags;
                ll.Logo = rl.Logo; ll.StationUuid = rl.StationUuid; ll.Favorite = rl.Favorite; ll.InfoUpdatedUtc = rl.InfoUpdatedUtc;
                result.UpdatedInfo++;
            }
        }

        // 1.7) الكتب
        MergeBooks(local, remote, deletedKeys, result);

        // 2) الحاجات الجديدة اللي اتضافت على الجهاز التاني
        foreach (var rc in remote.Courses)
        {
            if (deletedKeys.Contains(CourseKey(rc))) continue;
            var lc = local.Courses.FirstOrDefault(c => c.Id == rc.Id);
            if (lc == null)
            {
                // كورس جديد كله (من غير المحاضرات/الفيديوهات اللي اتمسحت هنا)
                rc.Groups.RemoveAll(g => deletedKeys.Contains(GroupKey(g)) || local.AllGroups.Any(x => x.Id == g.Id));
                foreach (var g in rc.Groups) g.Videos.RemoveAll(v => deletedKeys.Contains(VideoKey(v)) || localVideos.ContainsKey(VideoKey(v)));
                local.Courses.Add(rc);
                result.AddedCourses++;
                foreach (var v in rc.AllVideos) localVideos.TryAdd(VideoKey(v), v);
                continue;
            }

            if (rc.InfoUpdatedUtc > lc.InfoUpdatedUtc) { CopyCourseInfo(rc, lc); result.UpdatedInfo++; }

            foreach (var rg in rc.Groups)
            {
                if (deletedKeys.Contains(GroupKey(rg))) continue;
                var lg = local.AllGroups.FirstOrDefault(g => g.Id == rg.Id);
                if (lg != null && rg.InfoUpdatedUtc > lg.InfoUpdatedUtc)
                {
                    lg.Attachments = rg.Attachments.Select(CloneAttachment).ToList();
                    lg.SourceUrl = rg.SourceUrl;
                    lg.SourceFolder = rg.SourceFolder;
                    if (rg.LastSyncedUtc > lg.LastSyncedUtc || lg.LastSyncedUtc == null) lg.LastSyncedUtc = rg.LastSyncedUtc;
                    lg.InfoUpdatedUtc = rg.InfoUpdatedUtc;
                    result.UpdatedInfo++;
                }
                if (lg == null)
                {
                    rg.Videos.RemoveAll(v => deletedKeys.Contains(VideoKey(v)) || localVideos.ContainsKey(VideoKey(v)));
                    lc.Groups.Add(rg);
                    result.AddedGroups++;
                    foreach (var v in rg.Videos) localVideos.TryAdd(VideoKey(v), v);
                    continue;
                }

                foreach (var rv in rg.Videos)
                {
                    string key = VideoKey(rv);
                    if (deletedKeys.Contains(key) || localVideos.ContainsKey(key)) continue;
                    lg.Videos.Add(rv);
                    localVideos[key] = rv;
                    result.AddedVideos++;
                }
            }
        }

        MergeNotion(local, remote, localVideos);
        return result;
    }

    /// <summary>
    /// ربط نوشن: أرقام الصفوف والمربعات اللي جهاز تاني عملها بتتاخد (عشان مانعملش صف تاني لنفس الكورس)،
    /// والخطة (الموعد والحضور والمهام) الأحدث تكسب.
    /// </summary>
    private static void MergeNotion(CourseLibrary local, CourseLibrary remote, Dictionary<string, VideoItem> localVideos)
    {
        // موضوع اتعمل من جديد على الجهاز التاني (شكل أحدث): الربط الجديد وأرقامه كلها تكسب
        var rebuilt = new HashSet<string>();
        foreach (var rt in remote.Topics)
        {
            if (rt.Notion == null) continue;
            var lt = local.Topics.FirstOrDefault(t => t.Id == rt.Id);
            if (lt == null) continue;
            if (lt.Notion == null || (lt.Notion.PageId != rt.Notion.PageId && rt.Notion.CreatedUtc > lt.Notion.CreatedUtc))
            {
                if (lt.Notion != null) rebuilt.Add(lt.Id);
                lt.Notion = rt.Notion;
            }
            else if (lt.Notion.PageId == rt.Notion.PageId)
            {
                lt.Notion.LogDbId ??= rt.Notion.LogDbId;
                lt.Notion.LogDsId ??= rt.Notion.LogDsId;
                if (rt.Notion.PlanPulledUtc > (lt.Notion.PlanPulledUtc ?? DateTime.MinValue)) lt.Notion.PlanPulledUtc = rt.Notion.PlanPulledUtc;
            }
        }
        foreach (var rc in remote.Courses)
        {
            var lc = local.Courses.FirstOrDefault(c => c.Id == rc.Id);
            if (lc == null) continue;
            bool force = lc.TopicId != null && rebuilt.Contains(lc.TopicId);
            if (force || (lc.NotionPageId == null && rc.NotionPageId != null))
            {
                lc.NotionPageId = rc.NotionPageId; lc.NotionDsId = rc.NotionDsId;
                lc.NotionLecturesDbId = rc.NotionLecturesDbId; lc.NotionLecturesDsId = rc.NotionLecturesDsId;
            }
            else if (lc.NotionLecturesDsId == null && rc.NotionLecturesDsId != null && lc.NotionPageId == rc.NotionPageId)
            { lc.NotionLecturesDbId = rc.NotionLecturesDbId; lc.NotionLecturesDsId = rc.NotionLecturesDsId; }
            foreach (var rg in rc.Groups)
            {
                var lg = local.AllGroups.FirstOrDefault(g => g.Id == rg.Id);
                if (lg == null) continue;
                if (force || (lg.NotionPageId == null && rg.NotionPageId != null))
                {
                    lg.NotionPageId = rg.NotionPageId; lg.NotionDsId = rg.NotionDsId; lg.NotionNotesBlockId = rg.NotionNotesBlockId;
                    foreach (var rv in rg.Videos)
                        if (localVideos.TryGetValue(VideoKey(rv), out var fv)) fv.NotionBlockId = rv.NotionBlockId;
                }
                lg.NotionNotesBlockId ??= rg.NotionNotesBlockId;
                if (rg.Plan != null && (lg.Plan == null || rg.Plan.UpdatedUtc > lg.Plan.UpdatedUtc)) lg.Plan = rg.Plan.Clone();
            }
        }
        foreach (var rv in remote.AllVideos)
            if (rv.NotionBlockId != null && localVideos.TryGetValue(VideoKey(rv), out var lv) && lv.NotionBlockId == null)
                lv.NotionBlockId = rv.NotionBlockId;
    }

    /// <summary>
    /// الرفوف والكتب: الجديد بيتضاف، والبيانات الأحدث تكسب، والصفحات المقروءة بتتجمّع من الجهازين (مفيش تعارض)،
    /// والمكان (آخر صفحة والتكبير) والعلامات من الأحدث.
    /// </summary>
    private static void MergeBooks(CourseLibrary local, CourseLibrary remote, ISet<string> deletedKeys, Result result)
    {
        foreach (var rs in remote.Shelves ?? new List<Shelf>())
        {
            if (deletedKeys.Contains(ShelfKey(rs))) continue;
            var ls = local.Shelves.FirstOrDefault(x => x.Id == rs.Id);
            if (ls == null) { local.Shelves.Add(rs); result.UpdatedInfo++; }
            else if (rs.InfoUpdatedUtc > ls.InfoUpdatedUtc)
            {
                ls.Name = rs.Name; ls.IconKey = rs.IconKey; ls.ColorHex = rs.ColorHex; ls.ParentId = rs.ParentId; ls.SourceFolder = rs.SourceFolder;
                ls.InfoUpdatedUtc = rs.InfoUpdatedUtc; result.UpdatedInfo++;
            }
        }
        foreach (var rb in remote.Books ?? new List<Book>())
        {
            if (deletedKeys.Contains(BookKey(rb))) continue;
            var lb = local.Books.FirstOrDefault(x => x.Id == rb.Id);
            if (lb == null) { local.Books.Add(rb); result.AddedBooks++; continue; }
            if (MergeBook(lb, rb)) result.UpdatedBooks++;
        }
    }

    /// <summary>بيدمج نسخة الكتاب التانية في دي. true لو حاجة اتغيرت.</summary>
    public static bool MergeBook(Book target, Book other)
    {
        bool changed = false;
        if (other.InfoUpdatedUtc > target.InfoUpdatedUtc)
        {
            target.Title = other.Title; target.Author = other.Author; target.ShelfId = other.ShelfId;
            target.IconKey = other.IconKey; target.ColorHex = other.ColorHex;
            target.InfoUpdatedUtc = other.InfoUpdatedUtc;
            changed = true;
        }
        var read = target.Read;
        string before = read.ToString();
        read.UnionWith(other.Read);
        string after = read.ToString();
        if (after != before) { target.ReadPages = after; changed = true; }
        if (target.PageCount <= 0 && other.PageCount > 0) { target.PageCount = other.PageCount; changed = true; }
        if (target.Chapters.Count == 0 && other.Chapters.Count > 0) { target.Chapters = other.Chapters.ToList(); changed = true; }
        if (string.IsNullOrEmpty(target.Fingerprint) && !string.IsNullOrEmpty(other.Fingerprint)) { target.Fingerprint = other.Fingerprint; changed = true; }
        if (other.ProgressUtc > target.ProgressUtc)
        {
            target.LastPage = other.LastPage; target.LastTop = other.LastTop; target.LastLeft = other.LastLeft;
            target.Zoom = other.Zoom; target.Spread = other.Spread; target.RightToLeft = other.RightToLeft;
            target.MarkedDone = other.MarkedDone; target.Notes = other.Notes; target.LastOpenedUtc = other.LastOpenedUtc;
            target.InkCount = other.InkCount;
            var marks = other.Marks.ToList();
            // علامة اتعملت هنا بعد آخر تغيير هناك: تفضل
            marks.AddRange(target.Marks.Where(m => m.CreatedUtc > other.ProgressUtc && marks.All(x => x.Id != m.Id)));
            target.Marks = marks.OrderBy(m => m.Page).ToList();
            var hls = other.Highlights.ToList();
            hls.AddRange(target.Highlights.Where(h => h.CreatedUtc > other.ProgressUtc && hls.All(x => x.Id != h.Id)));
            target.Highlights = hls.OrderBy(h => h.Page).ToList();
            target.LastCfi = other.LastCfi;
            target.ProgressUtc = other.ProgressUtc;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// بعد التراجع: الهيكل بيرجع لورا، لكن حالة المشاهدة والعلامات والملاحظات الأحدث تفضل.
    /// </summary>
    public static int CopyNewerStates(CourseLibrary target, CourseLibrary source)
    {
        foreach (var tb in target.Books)
            if (source.Books.FirstOrDefault(x => x.Id == tb.Id) is { } sb && sb.ProgressUtc > tb.ProgressUtc)
            {
                var info = (tb.Title, tb.Author, tb.ShelfId, tb.IconKey, tb.ColorHex, tb.InfoUpdatedUtc);
                MergeBook(tb, sb);
                (tb.Title, tb.Author, tb.ShelfId, tb.IconKey, tb.ColorHex, tb.InfoUpdatedUtc) = info; // التراجع بيرجّع البيانات، التقدم بس اللي يفضل
            }
        var src = new Dictionary<string, VideoItem>(StringComparer.Ordinal);
        foreach (var v in source.AllVideos) src.TryAdd(VideoKey(v), v);
        int n = 0;
        foreach (var v in target.AllVideos)
        {
            if (!src.TryGetValue(VideoKey(v), out var s) || s.UpdatedUtc <= v.UpdatedUtc) continue;
            CopyState(s, v);
            n++;
        }
        return n;
    }

    /// <summary>بيانات الكورس (الحالة، الوسوم، الموعد…) — مش الاسم ولا الهيكل.</summary>
    public static void CopyCourseInfo(Course from, Course to)
    {
        to.Status = from.Status;
        to.Priority = from.Priority;
        to.IconKey = from.IconKey;
        to.UdemyId = from.UdemyId;
        to.ColorHex = from.ColorHex;
        to.Tags = from.Tags.ToList();
        to.Instructor = from.Instructor;
        to.SourceUrl = from.SourceUrl;
        to.Goal = from.Goal;
        to.Deadline = from.Deadline;
        to.StudyDays = from.StudyDays.ToList();
        to.Rate = from.Rate;
        to.TopicId = from.TopicId;
        to.PlaceId = from.PlaceId;
        to.SourceFolder = from.SourceFolder;
        to.InfoUpdatedUtc = from.InfoUpdatedUtc;
    }

    private static Attachment CloneAttachment(Attachment a) =>
        new() { Id = a.Id, Name = a.Name, Target = a.Target, RelativePath = a.RelativePath };

    private static void CopyState(VideoItem from, VideoItem to)
    {
        to.LastPositionMs = from.LastPositionMs;
        to.Watched = from.Watched;
        to.IsNew = from.IsNew;
        to.Notes = from.Notes;
        to.Bookmarks = from.Bookmarks.Select(b => new Bookmark
        {
            Id = b.Id, TimeMs = b.TimeMs, Note = b.Note, CreatedUtc = b.CreatedUtc,
            ReviewDueUtc = b.ReviewDueUtc, ReviewStage = b.ReviewStage
        }).ToList();
        if (from.DurationMs > 0) to.DurationMs = from.DurationMs;
        if (!string.IsNullOrWhiteSpace(from.Title)) to.Title = from.Title;
        to.UpdatedUtc = from.UpdatedUtc;
    }
}
