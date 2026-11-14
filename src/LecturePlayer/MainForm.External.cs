using LecturePlayer.Data;

namespace LecturePlayer;

/// <summary>
/// «فتح بواسطة ← مسار» على ملف فيديو:
/// لو الفيديو موجود في المكتبة يشتغل من مكانه (ويكمّل من آخر نقطة)،
/// ولو مش موجود يتضاف لمحاضرة نفس الفولدر لو موجودة، وإلا لكورس جديد باسم الفولدر.
/// </summary>
internal sealed partial class MainForm
{
    private IReadOnlyList<string>? _pendingExternal;

    internal void OpenExternalFiles(IReadOnlyList<string> paths)
    {
        var files = paths
            .Where(p => File.Exists(p) && MediaFiles.IsMedia(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0) return;

        RestoreFromTray();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();

        VideoItem? Find(string f) => _library.AllGroups.SelectMany(g => g.Videos)
            .FirstOrDefault(v => string.Equals(v.Path, f, StringComparison.OrdinalIgnoreCase));

        foreach (var inDir in files.Where(f => Find(f) == null)
                     .GroupBy(f => Path.GetDirectoryName(f) ?? "", StringComparer.OrdinalIgnoreCase))
        {
            string dir = inDir.Key;
            // محاضرة فيها فيديوهات من نفس الفولدر؟ نضيف جنبهم
            LectureGroup? target = _library.AllGroups.FirstOrDefault(g => g.Videos.Any(v =>
                string.Equals(Path.GetDirectoryName(v.Path), dir, StringComparison.OrdinalIgnoreCase)));
            if (target == null)
            {
                string name = new DirectoryInfo(dir).Name.TrimEnd('\\', ':');
                if (string.IsNullOrWhiteSpace(name)) name = "فيديوهات";
                var course = new Course { Name = name, Expanded = true };
                _library.Courses.Add(course);
                target = new LectureGroup { Name = name, Expanded = true };
                course.Groups.Add(target);
            }
            AddFiles(target, inDir);
        }

        var first = Find(files[0]);
        if (first != null && _library.FindGroupOf(first) is { } group)
        {
            SelectItem(first);
            PlayVideo(group, first);
        }
        SaveLibrary();
    }
}
