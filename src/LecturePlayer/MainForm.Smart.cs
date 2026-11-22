using System.Drawing.Drawing2D;
using LecturePlayer.Data;
using LecturePlayer.UI;
using LibVLCSharp.Shared;

namespace LecturePlayer;

/// <summary>
/// المرحلة 3 — المشغل الذكي: نص الفيديو (يوتيوب أو whisper على الجهاز)، البحث في الكلام،
/// ملخص وفصول واختبار و«اسأل الكورس» بـ Claude، ونسخ الكود من الشاشة (OCR).
/// </summary>
internal sealed partial class MainForm
{
    private CancellationTokenSource? _smartCts;
    private VideoItem? _subtitleAttachedFor;

    private bool SmartBusy => _smartCts != null;

    private ToolStripMenuItem BuildSmartMenu()
    {
        var subtitle = Item("اعرض النص كترجمة على الفيديو", () => { });
        subtitle.Checked = _settings.ShowTranscriptAsSubtitle;
        subtitle.Click += (_, _) =>
        {
            _settings.ShowTranscriptAsSubtitle = !_settings.ShowTranscriptAsSubtitle;
            subtitle.Checked = _settings.ShowTranscriptAsSubtitle;
            _subtitleAttachedFor = null;
            if (_settings.ShowTranscriptAsSubtitle) AttachTranscriptSubtitle();
        };

        var menu = SubMenu("ذكاء",
            Item("حوّل كلام الفيديو ده لنص مكتوب", () => _ = TranscribeAsync(TargetVideo(), show: true), Keys.Control | Keys.T),
            Item("حوّل كلام المحاضرة المحددة كلها لنص", () => _ = TranscribeGroupAsync(SelectedGroup() ?? _currentGroup)),
            Item("بحث في كلام كل الفيديوهات…", ShowTranscriptSearch, Keys.Control | Keys.Shift | Keys.F),
            subtitle,
            new ToolStripSeparator(),
            Item("لخّصلي الفيديو ده", () => _ = SummarizeAsync(TargetVideo(), force: false)),
            Item("قسّم الفيديو لأجزاء بعناوين (كعلامات)", () => _ = ChaptersAsync(TargetVideo())),
            Item("اختبرني في الفيديو ده", () => _ = QuizAsync(TargetVideo()), Keys.Control | Keys.Q),
            Item("اسأل عن أي حاجة في الكورس…", AskCourse, Keys.Control | Keys.K),
            new ToolStripSeparator(),
            Item("انسخ الكلام المكتوب على الشاشة (كود أو نص)", () => _ = OcrFrameAsync(), display: "O"),
            new ToolStripSeparator(),
            Item("وقّف الشغل اللي شغال دلوقتي", CancelSmart),
            Item("إعدادات الذكاء الاصطناعي…", SettingsDialog));
        return menu;
    }

    /// <summary>الفيديو الشغال، أو المحدد في القايمة.</summary>
    private VideoItem? TargetVideo() => _currentVideo ?? SelectedVideo();

    private void CancelSmart()
    {
        if (_smartCts == null) { ShowStatus("مفيش حاجة شغالة"); return; }
        _smartCts.Cancel();
        ShowStatus("بيتلغي…");
    }

    private CancellationTokenSource? BeginSmart()
    {
        if (_smartCts != null) { ShowStatus("فيه شغل ذكي شغال — استنى لما يخلص أو ألغيه من منيو «ذكاء»", 5); return null; }
        return _smartCts = new CancellationTokenSource();
    }

    private void EndSmart(CancellationTokenSource cts)
    {
        if (_smartCts == cts) _smartCts = null;
        cts.Dispose();
    }

    private static (string Text, Action<string> Click) Act(string text, Action<string> click) => (text, click);

    private void PlayAt(VideoItem v, long ms)
    {
        var g = _library.FindGroupOf(v);
        if (g == null) return;
        if (v == _currentVideo && IsActive) { _mp.Time = Math.Max(0, ms); RestoreFromTray(); return; }
        PlayVideo(g, v, Math.Max(0, ms));
        RestoreFromTray();
    }

    // =====================================================================
    //  نص الفيديو
    // =====================================================================

    private async Task<bool> EnsureWhisperAsync()
    {
        string model = string.IsNullOrWhiteSpace(_settings.WhisperModel) ? "small" : _settings.WhisperModel;
        if (Tools.FindWhisper() != null && File.Exists(Tools.ModelPath(model))) return true;
        string label = Tools.WhisperModels.FirstOrDefault(m => m.Name == model).Label ?? model;
        // الأداة نفسها جاية مع البرنامج؛ ملف اللغة كبير (مئات الميجا) فبيتنزل مرة واحدة أول ما تحتاجه
        string what = Tools.FindWhisper() != null
            ? $"أداة تحويل الكلام لنص جاية جاهزة مع البرنامج، وناقص «ملف اللغة» بس ({label}).\n" +
              "بيتنزل مرة واحدة، وبعدها التحويل بيشتغل على جهازك من غير نت.\n\n"
            : "تحويل كلام الفيديو لنص بيحصل على جهازك، ومحتاج أداة مجانية تتنزل مرة واحدة (وبعدها بيشتغل من غير نت):\n\n" +
              $"• الأداة (حوالي 10 ميجا)\n• ملف اللغة: {label}\n\n";
        if (!Dialogs.Confirm(this, what + "تقدر تغيّر الدقة (وحجم الملف) من الإعدادات. أنزّله دلوقتي؟", "تحويل الكلام لنص"))
            return false;
        UseWaitCursor = true;
        try
        {
            await Tools.DownloadWhisperAsync(model, new Progress<string>(m => ShowStatus(m, 120)), CancellationToken.None);
            ShowStatus("أداة تحويل الكلام لنص جاهزة ✓", 4);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("whisper-download", ex);
            Dialogs.Error(this, "مقدرتش أنزّل أداة تحويل الكلام لنص:\n" + ex.Message);
            return false;
        }
        finally { UseWaitCursor = false; }
    }

    /// <summary>النص من التخزين، أو بيجيبه (يوتيوب / whisper) ويحفظه.</summary>
    private async Task<Transcript?> GetTranscriptAsync(VideoItem v, CancellationToken ct, bool interactive = true)
    {
        var existing = TranscriptStore.Load(_libraryPath, v);
        if (existing is { IsEmpty: false }) return existing;

        Transcript? t = null;
        if (v.IsUdemy)
        {
            // الفيديو محمي جوه صفحة يوديمي: مفيش ملف نقرا منه الصوت
            if (interactive) ShowStatus("دروس يوديمي مالهاش ترجمة/ملخص من «مسار» (الفيديو محمي جوه يوديمي) — استخدم ترجمة يوديمي نفسها", 6);
            return null;
        }
        if (v.IsOnline)
        {
            if (!await EnsureYtDlpAsync()) return null;
            ShowStatus($"بجيب ترجمة يوتيوب: {v.Title}…", 60);
            t = await Transcriber.FromYoutubeAsync(v.Path, _settings.TranscriptLanguage, ct);
            if (t == null)
            {
                string? local = PlayableLocalFile(v);
                if (local == null)
                {
                    if (interactive)
                        Dialogs.Info(this, $"الفيديو «{v.Title}» مالوش ترجمة على يوتيوب.\n\n" +
                                           "نزّله الأول (كليك يمين ← تنزيل) وبعدين حوّل كلامه لنص (ذكاء ← حوّل كلام الفيديو ده لنص مكتوب).");
                    return null;
                }
                t = await WhisperAsync(v, local, ct);
            }
        }
        else
        {
            if (!File.Exists(v.LocalPath))
            {
                if (interactive) Dialogs.Error(this, "ملف الفيديو مش موجود على الجهاز ده:\n" + v.LocalPath);
                return null;
            }
            t = await WhisperAsync(v, v.LocalPath, ct);
        }

        if (t == null || t.IsEmpty) return null;
        TranscriptStore.Save(_libraryPath, v, t);
        Log.Info($"transcript saved: {v.Title} ({t.Cues.Count} cues)");
        if (v == _currentVideo) { _subtitleAttachedFor = null; AttachTranscriptSubtitle(); }
        return t;
    }

    private async Task<Transcript?> WhisperAsync(VideoItem v, string file, CancellationToken ct)
    {
        if (!await EnsureWhisperAsync()) return null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        return await Transcriber.WithWhisperAsync(file, _settings.WhisperModel, _settings.TranscriptLanguage,
            new Progress<int>(p =>
            {
                string eta = p is > 3 and < 100 ? $" — فاضل حوالي {TimeFormat.Format((long)(sw.ElapsedMilliseconds * (100.0 - p) / p))}" : "";
                ShowStatus($"بحوّل كلام «{v.Title}» لنص: {p}%{eta}", 120);
            }), ct);
    }

    private async Task TranscribeAsync(VideoItem? v, bool show)
    {
        if (v == null) { ShowStatus("شغّل أو حدد فيديو الأول"); return; }
        var cts = BeginSmart();
        if (cts == null) return;
        try
        {
            var t = await GetTranscriptAsync(v, cts.Token);
            if (t == null) return;
            ShowStatus($"النص جاهز ✓ ({t.Cues.Count} سطر)", 4);
            if (show) ShowTranscriptText(v, t);
        }
        catch (OperationCanceledException) { ShowStatus("اتلغى"); }
        catch (Exception ex) { Log.Error("transcribe", ex); Dialogs.Error(this, "تحويل الكلام لنص فشل:\n" + ex.Message); }
        finally { EndSmart(cts); }
    }

    private void ShowTranscriptText(VideoItem v, Transcript t)
    {
        string text = string.Join("\n", t.Cues.Select(c => $"[{Transcript.Short(c.StartMs)}] {c.Text.Replace('\n', ' ')}"));
        new TextViewerForm("نص: " + v.Title, text, NotesExporter.SafeFileName(v.Title) + ".txt",
            Act("بحث في الكلام…", _ => ShowTranscriptSearch())).Show(this);
    }

    private async Task TranscribeGroupAsync(LectureGroup? g)
    {
        if (g == null) { ShowStatus("حدد محاضرة الأول"); return; }
        var todo = g.Videos.Where(v => !TranscriptStore.Has(_libraryPath, v)).ToList();
        if (todo.Count == 0) { ShowStatus("كل فيديوهات المحاضرة كلامها اتحوّل لنص ✓", 4); return; }
        if (todo.Any(v => !v.IsOnline) && !await EnsureWhisperAsync()) return;
        var cts = BeginSmart();
        if (cts == null) return;
        int done = 0, failed = 0;
        try
        {
            foreach (var v in todo)
            {
                cts.Token.ThrowIfCancellationRequested();
                ShowStatus($"بحوّل كلام المحاضرة لنص: {done + failed + 1} من {todo.Count}…", 60);
                try { if (await GetTranscriptAsync(v, cts.Token, interactive: false) != null) done++; else failed++; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failed++; Log.Error("transcribe " + v.Title, ex); }
            }
            ShowStatus($"خلص تحويل الكلام لنص: {done} ✓" + (failed > 0 ? $" — {failed} مقدرتش (مالهاش ترجمة أو الملف مش موجود)" : ""), 10);
        }
        catch (OperationCanceledException) { ShowStatus($"اتلغى بعد {done} فيديو"); }
        finally { EndSmart(cts); }
    }

    private void AttachTranscriptSubtitle()
    {
        var v = _currentVideo;
        if (!_settings.ShowTranscriptAsSubtitle || v == null || _subtitleAttachedFor == v || !IsActive) return;
        string path = TranscriptStore.PathFor(_libraryPath, v);
        if (!File.Exists(path)) return;
        _subtitleAttachedFor = v;
        try { _mp.AddSlave(MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, true); }
        catch (Exception ex) { Log.Error("attach transcript", ex); }
    }

    // =====================================================================
    //  البحث في الكلام
    // =====================================================================

    private (TranscriptIndex Index, int Missing) BuildIndex(IEnumerable<VideoItem> videos)
    {
        var idx = new TranscriptIndex();
        int missing = 0;
        foreach (var v in videos)
        {
            var t = TranscriptStore.Load(_libraryPath, v);
            if (t is { IsEmpty: false }) idx.Add(v, t);
            else missing++;
        }
        return (idx, missing);
    }

    private void ShowTranscriptSearch()
    {
        UseWaitCursor = true;
        var (idx, missing) = BuildIndex(_library.AllVideos);
        UseWaitCursor = false;
        string status = $"{idx.VideoCount} فيديو كلامه متحوّل لنص" + (missing > 0 ? $" — {missing} لسه (حوّلهم من منيو «ذكاء»)" : "");
        new TranscriptSearchForm(status, q => idx.Search(q), PlayAt).Show(this);
    }

    // =====================================================================
    //  Claude
    // =====================================================================

    private ClaudeClient? GetClaude()
    {
        string key = _settings.ClaudeApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            if (Dialogs.Confirm(this, "المميزات دي بتستخدم الذكاء الاصطناعي Claude، ومحتاجة «مفتاح» بتعمله مرة واحدة من موقع console.anthropic.com.\n\n" +
                                      "المفتاح بيتحفظ متشفّر على الجهاز ده بس، ومش بيتكتب في ملف المكتبة.\n\nتفتح الإعدادات تحطه؟", "Claude"))
                SettingsDialog();
            key = _settings.ClaudeApiKey;
            if (string.IsNullOrWhiteSpace(key)) return null;
        }
        return new ClaudeClient(key);
    }

    /// <summary>طلب لـ Claude مع الحد الشهري وتسجيل الاستهلاك.</summary>
    private async Task<string?> RunClaudeAsync(string what, (string System, string User, int MaxTokens) prompt, CancellationToken ct)
    {
        var claude = GetClaude();
        if (claude == null) return null;
        string? block = AiBudget.CheckCanSpend(_settings, DateTime.UtcNow, AiBudget.Estimate(prompt.System + prompt.User, prompt.MaxTokens));
        if (block != null) { Dialogs.Info(this, block); return null; }

        ShowStatus($"Claude بيشتغل على {what}…", 120);
        var reply = await claude.AskAsync(_settings.AiModel, prompt.System, prompt.User, prompt.MaxTokens, ct);
        AiBudget.Record(_settings, DateTime.UtcNow, reply);
        SaveSettings();
        Log.Info($"claude {what}: {reply.InputTokens}+{reply.OutputTokens} tokens");
        ShowStatus($"{what} جاهز ✓  (استهلك {reply.TotalTokens:N0} وحدة — الشهر ده {AiBudget.UsedThisMonth(_settings, DateTime.UtcNow):N0})", 6);
        return reply.Text;
    }

    /// <summary>بيشغّل مهمة ذكية على فيديو: يجيب النص، وبعدين الشغل.</summary>
    private async Task WithTranscriptAsync(VideoItem? v, string what, Func<Transcript, CancellationToken, Task> work)
    {
        if (v == null) { ShowStatus("شغّل أو حدد فيديو الأول"); return; }
        var cts = BeginSmart();
        if (cts == null) return;
        try
        {
            var t = await GetTranscriptAsync(v, cts.Token);
            if (t == null) return;
            await work(t, cts.Token);
        }
        catch (OperationCanceledException) { ShowStatus("اتلغى"); }
        catch (ClaudeException ex) { Dialogs.Error(this, ex.Message); }
        catch (FormatException ex) { Dialogs.Error(this, $"Claude رد بشكل مش مفهوم ({what}). جرب تاني.\n\n{ex.Message}"); }
        catch (Exception ex) { Log.Error(what, ex); Dialogs.Error(this, $"{what} فشل:\n{ex.Message}"); }
        finally { EndSmart(cts); }
    }

    private string SummaryPath(VideoItem v) => Path.ChangeExtension(TranscriptStore.PathFor(_libraryPath, v), ".summary.md");

    private Task SummarizeAsync(VideoItem? v, bool force)
    {
        if (v != null && !force && File.Exists(SummaryPath(v)))
        {
            ShowSummary(v, File.ReadAllText(SummaryPath(v)));
            return Task.CompletedTask;
        }
        return WithTranscriptAsync(v, "الملخص", async (t, ct) =>
        {
            string? text = await RunClaudeAsync("الملخص", AiTasks.SummaryPrompt(v!.Title, t), ct);
            if (text == null) return;
            File.WriteAllText(SummaryPath(v), text, new System.Text.UTF8Encoding(false));
            ShowSummary(v, text);
        });
    }

    private void ShowSummary(VideoItem v, string text)
    {
        new TextViewerForm("ملخص: " + v.Title, text, NotesExporter.SafeFileName(v.Title) + " - ملخص.md",
            Act("حطه في ملاحظات الفيديو", edited =>
            {
                v.Notes = string.IsNullOrWhiteSpace(v.Notes) ? edited : v.Notes.TrimEnd() + "\n\n---\n" + edited;
                v.Touch();
                MarkDirty();
                SaveLibrary();
                RefreshNotesPane();
                RefreshVideo(v);
                ShowStatus("اتحط في الملاحظات ✓");
            }),
            Act("ملخص جديد", text => { _ = SummarizeAsync(v, force: true); })).Show(this);
    }

    private Task ChaptersAsync(VideoItem? v) => WithTranscriptAsync(v, "الفصول", async (t, ct) =>
    {
        string? text = await RunClaudeAsync("الفصول", AiTasks.ChaptersPrompt(v!.Title, t), ct);
        if (text == null) return;
        long duration = v.DurationMs > 0 ? v.DurationMs : t.DurationMs + 60_000;
        var chapters = AiTasks.ParseChapters(text, duration);
        if (chapters.Count == 0) { Dialogs.Info(this, "Claude مرجعش فصول."); return; }

        int added = 0;
        foreach (var ch in chapters)
        {
            string note = "📑 " + ch.Title;
            if (v.Bookmarks.Any(b => Math.Abs(b.TimeMs - ch.TimeMs) < 3000 && b.Note.StartsWith("📑", StringComparison.Ordinal))) continue;
            v.Bookmarks.Add(new Bookmark { TimeMs = ch.TimeMs, Note = note });
            added++;
        }
        v.Bookmarks.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        v.Touch();
        MarkDirty();
        SaveLibrary();
        RefreshNotesPane();
        UpdateSeekMarkers();
        RefreshVideo(v);
        Dialogs.Info(this, $"اتضاف {added} فصل كعلامات 📑 (في لوحة الملاحظات وعلى شريط التقديم).\n\n" +
                           string.Join("\n", chapters.Select(c => $"{Transcript.Short(c.TimeMs)}  {c.Title}")));
    });

    private Task QuizAsync(VideoItem? v) => WithTranscriptAsync(v, "الاختبار", async (t, ct) =>
    {
        string? text = await RunClaudeAsync("الاختبار", AiTasks.QuizPrompt(v!.Title, t), ct);
        if (text == null) return;
        var questions = AiTasks.ParseQuiz(text);
        if (questions.Count == 0) { Dialogs.Info(this, "Claude مرجعش أسئلة صالحة. جرب تاني."); return; }
        var g = _library.FindGroupOf(v);
        new QuizForm(v.Title, questions, ms => PlayAt(v, ms), (score, total) =>
        {
            LogJournal(new JournalEntry
            {
                Kind = JournalEntry.Quiz, StartUtc = DateTime.UtcNow, Score = score, Total = total,
                VideoPath = v.Path, GroupId = g?.Id, CourseId = g != null ? _library.FindCourseOf(g)?.Id : null
            });
        }).Show(this);
    });

    private void AskCourse()
    {
        var course = (_currentGroup != null ? _library.FindCourseOf(_currentGroup) : null) ?? SelectedCourse();
        if (course == null) { Dialogs.Info(this, "شغّل فيديو أو حدد كورس الأول."); return; }
        var (idx, missing) = BuildIndex(course.AllVideos);
        if (idx.VideoCount == 0)
        {
            Dialogs.Info(this, $"مفيش ولا فيديو في «{course.Name}» كلامه متحوّل لنص.\n\nحوّل كلام المحاضرات لنص الأول (منيو «ذكاء» ← حوّل كلام المحاضرة المحددة كلها لنص)، " +
                               "وبعدين اسأل وأنا هجاوبك من كلام الفيديوهات نفسها مع الوقت.");
            return;
        }
        if (missing > 0) ShowStatus($"ملحوظة: {missing} فيديو في الكورس كلامهم لسه متحوّلش لنص، ومش هيدخلوا في الإجابة", 8);

        new AskForm(course.Name, async (question, ct) =>
        {
            var top = idx.Retrieve(question, 8);
            if (top.Count == 0) return ("ملقتش أي جزء في نصوص الفيديوهات قريب من سؤالك. جرب كلمات تانية (أو المصطلح بالإنجليزي).", new List<AskSource>());
            var sources = top.Select((x, i) => new AskSource(i + 1, x.Video, x.Chunk.StartMs, x.Chunk.Text)).ToList();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            string? answer = await RunClaudeAsync("الإجابة", AiTasks.AskPrompt(course.Name, question, sources), cts.Token);
            return (answer ?? "اتلغى.", sources);
        }, PlayAt).Show(this);
    }

    // =====================================================================
    //  OCR
    // =====================================================================

    private async Task OcrFrameAsync()
    {
        if (_currentVideo == null || !IsActive) { ShowStatus("شغّل فيديو الأول"); return; }
        if (!Ocr.IsSupported) { Dialogs.Info(this, "نسخ النص من الشاشة محتاج Windows 10 أو أحدث."); return; }
        bool wasPlaying = _mp.State == VLCState.Playing;
        if (wasPlaying) _mp.SetPause(true);

        string dir = Path.Combine(Storage.AppDataDir, "cache", "ocr");
        Directory.CreateDirectory(dir);
        string raw = Path.Combine(dir, "frame.png"), big = Path.Combine(dir, "frame-2x.png");
        try
        {
            File.Delete(raw);
            if (!_mp.TakeSnapshot(0, raw, 0, 0)) throw new InvalidOperationException("مقدرتش آخد صورة من الفيديو");
            for (int i = 0; i < 20 && !File.Exists(raw); i++) await Task.Delay(100); // VLC بيكتب الصورة في الخلفية
            if (!File.Exists(raw)) throw new InvalidOperationException("الصورة متكتبتش");
            await Task.Delay(150);
            UpscaleForOcr(raw, big);

            string text = await Ocr.RecognizeAsync(big);
            if (string.IsNullOrWhiteSpace(text)) { Dialogs.Info(this, "ملقتش نص واضح في الصورة دي."); return; }
            var viewer = new TextViewerForm("النص اللي على الشاشة", text, "code.txt",
                Act("افتحه في معمل الكود", edited => OpenCodeLab(edited)));
            viewer.UseCodeFont();
            viewer.Show(this);
            Clipboard.SetText(text);
            ShowStatus("الكلام اتنسخ ✓ — الصقه في أي مكان (راجع المسافات في أول السطور)", 6);
        }
        catch (Exception ex)
        {
            Log.Error("ocr", ex);
            Dialogs.Error(this, "مقدرتش أقرا النص:\n" + ex.Message);
        }
    }

    /// <summary>تكبير ×2 مع تنعيم: الـ OCR بيقرا الخط الصغير بتاع الأكواد أحسن بكتير.</summary>
    private static void UpscaleForOcr(string src, string dst)
    {
        using var img = Image.FromFile(src);
        // ×2 بحد أقصى أكبر مقاس يقبله محرك الويندوز (ولو الصورة أكبر منه بتصغر)
        double scale = Math.Min(2.0, (Ocr.MaxDimension - 8) / (double)Math.Max(img.Width, img.Height));
        int w = Math.Max(1, (int)(img.Width * scale)), h = Math.Max(1, (int)(img.Height * scale));
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(img, 0, 0, w, h);
        }
        bmp.Save(dst, System.Drawing.Imaging.ImageFormat.Png);
    }
}
