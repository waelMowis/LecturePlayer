using System.Text.Json;
using System.Text.Json.Nodes;
using LecturePlayer.Data;

namespace LecturePlayer;

/// <summary>
/// الشرح على الكتب (أقلام، ألوان، أشكال، نصوص، ملصقات): القارئ بيرسم، والبرنامج بيحفظ ملف لكل كتاب
/// في «‹المكتبة›.annotations» ويدمجه مع أي نسخة جاية من جهاز تاني (Google Drive/OneDrive).
/// </summary>
internal partial class MainForm
{
    private sealed class InkSession
    {
        public required Book Book { get; init; }
        public required string File { get; init; }
        public required JsonObject Doc { get; set; }
        public DateTime Stamp { get; set; }
        public bool Dirty { get; set; }
        public int Gen { get; set; } = -1;
        public DateTime ChangedUtc { get; set; }
    }

    /// <summary>جلسة الكتاب المفتوح + اللي قبله (رسايله الأخيرة ممكن توصل بعد ما فتحت كتاب تاني).</summary>
    private InkSession? _ink;
    private readonly Dictionary<int, InkSession> _inkByGen = new();
    private System.Windows.Forms.Timer? _inkSaveTimer;
    private long _dueInkDisk;

    /// <summary>بيقرا شرح الكتاب قبل ما يتفتح (null = مفيش مكتبة).</summary>
    private JsonObject? BeginInkSession(Book b)
    {
        if (!HasLibrary) return null;
        SaveAllInk();
        string file = InkStore.FileFor(_libraryPath, b.Id);
        var doc = InkStore.Load(file);
        _ink = new InkSession { Book = b, File = file, Doc = doc, Stamp = InkStore.Stamp(file) };
        UpdateInkCount(_ink);
        return doc;
    }

    /// <summary>بعد ما القارئ فتح الكتاب: رقم الفتحة بيربط رسايله بالجلسة.</summary>
    private void BindInkSession(int gen)
    {
        if (_ink == null) return;
        _ink.Gen = gen;
        _inkByGen[gen] = _ink;
        foreach (int old in _inkByGen.Keys.Where(k => k < gen - 3).ToList()) _inkByGen.Remove(old);
    }

    private void OnInkChanged(int gen, JsonElement pages, JsonElement tombs)
    {
        if (!_inkByGen.TryGetValue(gen, out var s)) { Log.Info($"ink: message for unknown gen {gen}"); return; }
        if (!InkStore.Apply(s.Doc, pages, tombs)) return;
        s.Dirty = true;
        s.ChangedUtc = DateTime.UtcNow;
        if (s != _ink) SaveInk(s);          // كتاب اتقفل: يتحفظ على طول
        else ScheduleInkSave();
    }

    private void ScheduleInkSave()
    {
        if (_inkSaveTimer == null)
        {
            _inkSaveTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _inkSaveTimer.Tick += (_, _) => { _inkSaveTimer!.Stop(); SaveAllInk(); };
        }
        _inkSaveTimer.Stop();
        _inkSaveTimer.Start();
    }

    private void SaveAllInk()
    {
        foreach (var s in _inkByGen.Values.Distinct().ToList()) if (s.Dirty) SaveInk(s);
        if (_ink is { Dirty: true } cur && !_inkByGen.ContainsValue(cur)) SaveInk(cur);
    }

    /// <summary>
    /// بيحفظ — ولو الملف اتغير من جهاز تاني بعد ما قريناه، بيدمج الأول (ومايضيعش رسم أي جهاز).
    /// </summary>
    private void SaveInk(InkSession s)
    {
        try
        {
            var disk = InkStore.Stamp(s.File);
            HashSet<string>? fromRemote = null;
            if (disk != s.Stamp && disk != DateTime.MinValue)
            {
                fromRemote = InkStore.Merge(s.Doc, InkStore.Load(s.File));
                Log.Info($"ink: merged remote changes ({fromRemote.Count} pages) for {s.Book.Title}");
            }
            s.Stamp = InkStore.Save(s.File, s.Doc);
            s.Dirty = false;
            UpdateInkCount(s);
            if (fromRemote is { Count: > 0 } && s == _ink && ReaderShown && _currentBook == s.Book) _ = _reader?.MergeInkAsync(s.Doc);
        }
        catch (Exception ex)
        {
            Log.Error("ink save", ex);
            ShowBanner("مقدرتش أحفظ الشرح: " + ex.Message + " — هحاول تاني", BannerKind.Warning, seconds: 8, key: "ink");
            ScheduleInkSave();
        }
    }

    /// <summary>كل 15 ثانية والكتاب مفتوح: لو جهاز تاني رسم على نفس الكتاب (الدرايف نزّل نسخة أحدث) يبان قدامك.</summary>
    private void InkTick()
    {
        if (_ink is not { } s || !ReaderShown || _currentBook != s.Book || s.Dirty) return;
        if (!Due(ref _dueInkDisk, 15000)) return;
        var disk = InkStore.Stamp(s.File);
        if (disk == s.Stamp || disk == DateTime.MinValue) return;
        var changed = InkStore.Merge(s.Doc, InkStore.Load(s.File));
        s.Stamp = disk;
        if (changed.Count == 0) return;
        UpdateInkCount(s);
        _ = _reader?.MergeInkAsync(s.Doc);
        ShowStatus("وصل شرح جديد على الكتاب ده من جهاز تاني", 4);
    }

    private void UpdateInkCount(InkSession s)
    {
        int n = InkStore.Count(s.Doc);
        if (s.Book.InkCount == n) return;
        s.Book.InkCount = n;
        s.Book.TouchProgress();
        MarkPositionDirty();
        if (_shelfMode) RefreshBookRows(s.Book);
    }

    private void OnInkPrefsChanged(string json)
    {
        if (json.Length > 2_000_000) { Log.Info("ink prefs too large: " + json.Length); return; }
        _settings.InkPrefs = json;
        try { Storage.SaveSettings(_settings); } catch (Exception ex) { Log.Info("ink prefs save: " + ex.Message); }
    }

    /// <summary>قبل القفل: القارئ يبعت آخر رسم، وبعدين كل حاجة تتحفظ.</summary>
    private async Task FlushInkForCloseAsync()
    {
        if (ReaderShown && _reader != null)
        {
            try
            {
                await _reader.FlushInkAsync().WaitAsync(TimeSpan.FromSeconds(2));
                await Task.Delay(150); // الرسالة توصل
            }
            catch { }
        }
        SaveAllInk();
    }

    /// <summary>فتح/قفل أدوات الشرح في الكتاب المفتوح (من القائمة أو الاختصار).</summary>
    private void ToggleInkMode()
    {
        if (!ReaderShown || _currentBook == null || _reader == null) return;
        if (BookFiles.FormatOf(BookFile(_currentBook)) == "epub")
        {
            ShowStatus("الأقلام للـ PDF بس — في الـ EPUB استخدم التظليل (حدد الكلام)", 5);
            return;
        }
        _ = _reader.EvalAsync("masar.ink()");
    }

    private Book? _inkOnOpen;

    /// <summary>يفتح الكتاب وأدوات الشرح جاهزة.</summary>
    private void OpenBookForInk(Book b)
    {
        if (ReaderShown && _currentBook == b) { _ = _reader?.SetInkModeAsync(true); return; }
        _inkOnOpen = b;
        OpenBook(b);
    }

    private bool _presentFullscreen;

    /// <summary>وضع العرض في القارئ: لو مش ملء الشاشة بنعمله، ولما يخلص نرجّع زي ما كان.</summary>
    private void OnReaderPresent(bool on)
    {
        if (on) HideBanner();   // التنبيهات مابتظهرش فوق العرض
        if (on && !_fullscreen) { _presentFullscreen = true; ToggleFullscreen(); }
        else if (!on && _presentFullscreen) { _presentFullscreen = false; if (_fullscreen) ToggleFullscreen(); }
    }

    private static string InkMeta(Book b) => b.InkCount > 0 ? " 🖊" : "";
}
