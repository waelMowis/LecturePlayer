using System.Runtime.InteropServices;

namespace LecturePlayer.Data;

/// <summary>
/// بيستنى ملف PDF جديد يظهر في فولدر التنزيلات (المستخدم نزّله من متصفحه بحسابه)، ولما يخلص تنزيل بيبلّغ بمساره.
/// المتصفحات بتكتب ملف مؤقت (.crdownload / .part) وبعدين بتغيّر اسمه — فبنستنى الاسم النهائي وإن الحجم يثبت.
/// </summary>
public sealed class DownloadsWatcher : IDisposable
{
    private readonly FileSystemWatcher _fsw;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly HashSet<string> _before;
    private readonly System.Threading.Timer _poll;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private int _done;

    /// <summary>الملف وصل (بيتنده على الواجهة).</summary>
    public event Action<string>? Found;
    public string Folder { get; }

    public DownloadsWatcher(string? folder = null)
    {
        Folder = folder ?? DownloadsFolder;
        Directory.CreateDirectory(Folder);
        _before = Directory.EnumerateFiles(Folder).Where(BookFiles.IsBookFile).Select(f => f.ToUpperInvariant()).ToHashSet();
        _fsw = new FileSystemWatcher(Folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, IncludeSubdirectories = false };
        _fsw.Created += (_, e) => Check(e.FullPath);
        _fsw.Renamed += (_, e) => Check(e.FullPath);
        _fsw.Changed += (_, e) => Check(e.FullPath);
        _fsw.EnableRaisingEvents = true;
        // احتياطي لو حدث اتفوّت (فولدرات الشبكة/الدرايف)
        _poll = new System.Threading.Timer(_ => Scan(), null, 2000, 2000);
    }

    private void Scan()
    {
        try
        {
            foreach (string f in Directory.EnumerateFiles(Folder).Where(BookFiles.IsBookFile))
                if (!_before.Contains(f.ToUpperInvariant())) Check(f);
        }
        catch { }
    }

    private void Check(string path)
    {
        if (_done != 0 || !BookFiles.IsBookFile(path)) return;
        if (_before.Contains(path.ToUpperInvariant())) return;
        _ = Task.Run(async () =>
        {
            // الحجم يثبت والملف يتفتح من غير ما حد ماسكه
            long last = -1;
            for (int i = 0; i < 120 && _done == 0; i++)
            {
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists) return;
                    if (info.LastWriteTimeUtc < _startedUtc.AddSeconds(-5) && info.CreationTimeUtc < _startedUtc.AddSeconds(-5)) return; // ملف قديم
                    if (info.Length > 0 && info.Length == last)
                    {
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            var head = new byte[1024];
                            int n = fs.Read(head, 0, head.Length);
                            if (!BookSources.LooksLikeBook(head, n)) return;
                        }
                        if (Interlocked.Exchange(ref _done, 1) != 0) return;
                        if (_ui != null) _ui.Post(_ => Found?.Invoke(path), null); else Found?.Invoke(path);
                        return;
                    }
                    last = info.Length;
                }
                catch (IOException) { /* لسه بيتكتب */ }
                catch (Exception) { return; }
                await Task.Delay(700);
            }
        });
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _done, 1);
        _fsw.Dispose();
        _poll.Dispose();
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    /// <summary>فولدر التنزيلات بتاع المستخدم (حتى لو اتنقل لدرايف تاني).</summary>
    public static string DownloadsFolder
    {
        get
        {
            try
            {
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p) ?? "";
                    Marshal.FreeCoTaskMem(p);
                    if (Directory.Exists(s)) return s;
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }
}
