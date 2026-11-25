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
            Item("حوّل كلام هذا الفيديو إلى نص مكتوب", () => _ = TranscribeAsync(TargetVideo(), show: true), Keys.Control | Keys.T),
            Item("حوّل كلام المحاضرة المحددة كلها إلى نص", () => _ = TranscribeGroupAsync(SelectedGroup() ?? _currentGroup)),
            Item("بحث في كلام كل الفيديوهات…", ShowTranscriptSearch, Keys.Control | Keys.Shift | Keys.F),
            subtitle,
            new ToolStripSeparator(),
            Item("لخّص لي هذا الفيديو", () => _ = SummarizeAsync(TargetVideo(), force: false)),
            Item("قسّم الفيديو إلى أجزاء بعناوين (كعلامات)", () => _ = ChaptersAsync(TargetVideo())),
            Item("اختبرني في هذا الفيديو", () => _ = QuizAsync(TargetVideo()), Keys.Control | Keys.Q),
            Item("اسأل عن أي شيء في الدورة…", AskCourse, Keys.Control | Keys.K),
            new ToolStripSeparator(),
            Item("انسخ الكلام المكتوب على الشاشة (كود أو نص)", () => _ = OcrFrameAsync(), display: "O"),
            new ToolStripSeparator(),
            Item("أوقف المهمة الجارية الآن", CancelSmart),
            Item("إعدادات الذكاء الاصطناعي…", SettingsDialog));
        return menu;
    }

    /// <summary>الفيديو الشغال، أو المحدد في القايمة.</summary>
    private VideoItem? TargetVideo() => _currentVideo ?? SelectedVideo();

    private void CancelSmart()
    {
        if (_smartCts == null) { ShowStatus("لا توجد مهمة قيد العمل"); return; }
        _smartCts.Cancel();
        ShowStatus("جارٍ الإلغاء…");
    }

    private CancellationTokenSource? BeginSmart()
    {
        if (_smartCts != null) { ShowStatus("توجد مهمة ذكية قيد العمل — انتظر حتى تنتهي أو ألغِها من «الأدوات ← الذكاء الاصطناعي»", 5); return null; }
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
            ? $"أداة تحويل الكلام إلى نص تأتي جاهزة مع البرنامج، وينقصها «ملف اللغة» فقط ({label}).\n" +
              "يُنزَّل مرة واحدة، وبعدها يعمل التحويل على جهازك دون إنترنت.\n\n"
            : "تحويل كلام الفيديو إلى نص يتم على جهازك، ويحتاج إلى أداة مجانية تُنزَّل مرة واحدة (وبعدها يعمل دون إنترنت):\n\n" +
              $"• الأداة (نحو 10 ميجابايت)\n• ملف اللغة: {label}\n\n";
        if (!Dialogs.Confirm(this, what + "يمكنك تغيير الدقة (وحجم الملف) من الإعدادات. هل أنزّله الآن؟", "تحويل الكلام إلى نص"))
            return false;
        UseWaitCursor = true;
        try
        {
            await Tools.DownloadWhisperAsync(model, new Progress<string>(m => ShowStatus(m, 120)), CancellationToken.None);
            ShowStatus("أداة تحويل الكلام إلى نص جاهزة ✓", 4);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("whisper-download", ex);
            Dialogs.Error(this, "تعذّر تنزيل أداة تحويل الكلام إلى نص:\n" + ex.Message);
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
        if (v.IsMoodlePage) return null; // صفحة نشاط (اختبار/ملف…) مش فيديو
        if (v.IsWebLesson)
        {
            // الفيديو محمي جوه صفحة يوديمي: مفيش ملف نقرا منه الصوت
            if (interactive) ShowStatus("دروس يوديمي ليس لها ترجمة/ملخص من «مسار» (الفيديو محمي داخل يوديمي) — استخدم ترجمة يوديمي نفسها", 6);
            return null;
        }
        if (v.IsOnline)
        {
            if (!await EnsureYtDlpAsync()) return null;
            ShowStatus($"جارٍ جلب ترجمة يوتيوب: {v.Title}…", 60);
            t = await Transcriber.FromYoutubeAsync(v.Path, _settings.TranscriptLanguage, ct);
            if (t == null)
            {
                string? local = PlayableLocalFile(v);
                if (local == null)
                {
                    if (interactive)
                        Dialogs.Info(this, $"الفيديو «{v.Title}» ليس له ترجمة على يوتيوب.\n\n" +
                                           "نزّله أولًا (انقر بالزر الأيمن ← تنزيل) ثم حوّل كلامه إلى نص (الأدوات ← الذكاء الاصطناعي ← حوّل كلام هذا الفيديو إلى نص مكتوب).");
                    return null;
                }
                t = await WhisperAsync(v, local, ct);
            }
        }
        else
        {
            if (!File.Exists(v.LocalPath))
            {
                if (interactive) Dialogs.Error(this, "ملف الفيديو غير موجود على هذا الجهاز:\n" + v.LocalPath);
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
                string eta = p is > 3 and < 100 ? $" — متبقٍ نحو {TimeFormat.Format((long)(sw.ElapsedMilliseconds * (100.0 - p) / p))}" : "";
                ShowStatus($"جارٍ تحويل كلام «{v.Title}» إلى نص: {p}%{eta}", 120);
            }), ct);
    }

    private async Task TranscribeAsync(VideoItem? v, bool show)
    {
        if (v == null) { ShowStatus("شغّل فيديو أو حدّده أولًا"); return; }
        var cts = BeginSmart();
        if (cts == null) return;
        try
        {
            var t = await GetTranscriptAsync(v, cts.Token);
            if (t == null) return;
            ShowStatus($"النص جاهز ✓ ({t.Cues.Count} سطر)", 4);
            if (show) ShowTranscriptText(v, t);
        }
        catch (OperationCanceledException) { ShowStatus("أُلغي"); }
        catch (Exception ex) { Log.Error("transcribe", ex); Dialogs.Error(this, "فشل تحويل الكلام إلى نص:\n" + ex.Message); }
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
        if (g == null) { ShowStatus("حدّد محاضرة أولًا"); return; }
        var todo = g.Videos.Where(v => !TranscriptStore.Has(_libraryPath, v)).ToList();
        if (todo.Count == 0) { ShowStatus("حُوّل كلام كل فيديوهات المحاضرة إلى نص ✓", 4); return; }
        if (todo.Any(v => !v.IsOnline) && !await EnsureWhisperAsync()) return;
        var cts = BeginSmart();
        if (cts == null) return;
        int done = 0, failed = 0;
        try
        {
            foreach (var v in todo)
            {
                cts.Token.ThrowIfCancellationRequested();
                ShowStatus($"جارٍ تحويل كلام المحاضرة إلى نص: {done + failed + 1} من {todo.Count}…", 60);
                try { if (await GetTranscriptAsync(v, cts.Token, interactive: false) != null) done++; else failed++; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failed++; Log.Error("transcribe " + v.Title, ex); }
            }
            ShowStatus($"اكتمل تحويل الكلام إلى نص: {done} ✓" + (failed > 0 ? $" — تعذّر {failed} (بلا ترجمة أو الملف غير موجود)" : ""), 10);
        }
        catch (OperationCanceledException) { ShowStatus($"أُلغي بعد {done} فيديو"); }
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
        string status = $"{idx.VideoCount} فيديو حُوّل كلامه إلى نص" + (missing > 0 ? $" — {missing} لم تُحوَّل بعد (حوّلها من «الأدوات ← الذكاء الاصطناعي»)" : "");
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
            if (Dialogs.Confirm(this, "هذه الميزات تستخدم الذكاء الاصطناعي Claude، وتحتاج إلى «مفتاح» تنشئه مرة واحدة من موقع console.anthropic.com.\n\n" +
                                      "يُحفظ المفتاح مشفّرًا على هذا الجهاز فقط، ولا يُكتب في ملف المكتبة.\n\nهل تفتح الإعدادات لإضافته؟", "Claude"))
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

        ShowStatus($"Claude يعمل على {what}…", 120);
        var reply = await claude.AskAsync(_settings.AiModel, prompt.System, prompt.User, prompt.MaxTokens, ct);
        AiBudget.Record(_settings, DateTime.UtcNow, reply);
        SaveSettings();
        Log.Info($"claude {what}: {reply.InputTokens}+{reply.OutputTokens} tokens");
        ShowStatus($"{what} جاهز ✓  (استهلك {reply.TotalTokens:N0} وحدة — هذا الشهر {AiBudget.UsedThisMonth(_settings, DateTime.UtcNow):N0})", 6);
        return reply.Text;
    }

    /// <summary>بيشغّل مهمة ذكية على فيديو: يجيب النص، وبعدين الشغل.</summary>
    private async Task WithTranscriptAsync(VideoItem? v, string what, Func<Transcript, CancellationToken, Task> work)
    {
        if (v == null) { ShowStatus("شغّل فيديو أو حدّده أولًا"); return; }
        var cts = BeginSmart();
        if (cts == null) return;
        try
        {
            var t = await GetTranscriptAsync(v, cts.Token);
            if (t == null) return;
            await work(t, cts.Token);
        }
        catch (OperationCanceledException) { ShowStatus("أُلغي"); }
        catch (ClaudeException ex) { Dialogs.Error(this, ex.Message); }
        catch (FormatException ex) { Dialogs.Error(this, $"ردّ Claude بشكل غير مفهوم ({what}). حاول مرة أخرى.\n\n{ex.Message}"); }
        catch (Exception ex) { Log.Error(what, ex); Dialogs.Error(this, $"فشل {what}:\n{ex.Message}"); }
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
            Act("ضعه في ملاحظات الفيديو", edited =>
            {
                v.Notes = string.IsNullOrWhiteSpace(v.Notes) ? edited : v.Notes.TrimEnd() + "\n\n---\n" + edited;
                v.Touch();
                MarkDirty();
                SaveLibrary();
                RefreshNotesPane();
                RefreshVideo(v);
                ShowStatus("وُضع في الملاحظات ✓");
            }),
            Act("ملخص جديد", text => { _ = SummarizeAsync(v, force: true); })).Show(this);
    }

    private Task ChaptersAsync(VideoItem? v) => WithTranscriptAsync(v, "الفصول", async (t, ct) =>
    {
        string? text = await RunClaudeAsync("الفصول", AiTasks.ChaptersPrompt(v!.Title, t), ct);
        if (text == null) return;
        long duration = v.DurationMs > 0 ? v.DurationMs : t.DurationMs + 60_000;
        var chapters = AiTasks.ParseChapters(text, duration);
        if (chapters.Count == 0) { Dialogs.Info(this, "لم يُرجع Claude فصولًا."); return; }

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
        Dialogs.Info(this, $"أُضيف {added} فصل كعلامات 📑 (في لوحة الملاحظات وعلى شريط التقديم).\n\n" +
                           string.Join("\n", chapters.Select(c => $"{Transcript.Short(c.TimeMs)}  {c.Title}")));
    });

    private Task QuizAsync(VideoItem? v) => WithTranscriptAsync(v, "الاختبار", async (t, ct) =>
    {
        string? text = await RunClaudeAsync("الاختبار", AiTasks.QuizPrompt(v!.Title, t), ct);
        if (text == null) return;
        var questions = AiTasks.ParseQuiz(text);
        if (questions.Count == 0) { Dialogs.Info(this, "لم يُرجع Claude أسئلة صالحة. حاول مرة أخرى."); return; }
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
        if (course == null) { Dialogs.Info(this, "شغّل فيديو أو حدّد دورة أولًا."); return; }
        var (idx, missing) = BuildIndex(course.AllVideos);
        if (idx.VideoCount == 0)
        {
            Dialogs.Info(this, $"لا يوجد أي فيديو في «{course.Name}» حُوّل كلامه إلى نص.\n\nحوّل كلام المحاضرات إلى نص أولًا («الأدوات ← الذكاء الاصطناعي ← حوّل كلام المحاضرة المحددة كلها إلى نص»)، " +
                               "ثم اسأل وسأجيبك من كلام الفيديوهات نفسها مع التوقيت.");
            return;
        }
        if (missing > 0) ShowStatus($"ملاحظة: {missing} فيديو في الدورة لم يُحوَّل كلامها إلى نص بعد، ولن تدخل في الإجابة", 8);

        new AskForm(course.Name, async (question, ct) =>
        {
            var top = idx.Retrieve(question, 8);
            if (top.Count == 0) return ("لم أجد أي جزء في نصوص الفيديوهات قريبًا من سؤالك. جرّب كلمات أخرى (أو المصطلح بالإنجليزية).", new List<AskSource>());
            var sources = top.Select((x, i) => new AskSource(i + 1, x.Video, x.Chunk.StartMs, x.Chunk.Text)).ToList();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            string? answer = await RunClaudeAsync("الإجابة", AiTasks.AskPrompt(course.Name, question, sources), cts.Token);
            return (answer ?? "أُلغي.", sources);
        }, PlayAt).Show(this);
    }

    // =====================================================================
    //  OCR
    // =====================================================================

    private async Task OcrFrameAsync()
    {
        if (_currentVideo == null || !IsActive) { ShowStatus("شغّل فيديو أولًا"); return; }
        if (!Ocr.IsSupported) { Dialogs.Info(this, "نسخ النص من الشاشة يحتاج إلى Windows 10 أو أحدث."); return; }
        bool wasPlaying = _mp.State == VLCState.Playing;
        if (wasPlaying) _mp.SetPause(true);

        string dir = Path.Combine(Storage.AppDataDir, "cache", "ocr");
        Directory.CreateDirectory(dir);
        string raw = Path.Combine(dir, "frame.png"), big = Path.Combine(dir, "frame-2x.png");
        try
        {
            File.Delete(raw);
            if (!_mp.TakeSnapshot(0, raw, 0, 0)) throw new InvalidOperationException("تعذّر التقاط صورة من الفيديو");
            for (int i = 0; i < 20 && !File.Exists(raw); i++) await Task.Delay(100); // VLC بيكتب الصورة في الخلفية
            if (!File.Exists(raw)) throw new InvalidOperationException("لم تُلتقط الصورة");
            await Task.Delay(150);
            UpscaleForOcr(raw, big);

            string text = await Ocr.RecognizeAsync(big);
            if (string.IsNullOrWhiteSpace(text)) { Dialogs.Info(this, "لم أجد نصًا واضحًا في هذه الصورة."); return; }
            var viewer = new TextViewerForm("النص الموجود على الشاشة", text, "code.txt",
                Act("افتحه في معمل الكود", edited => OpenCodeLab(edited)));
            viewer.UseCodeFont();
            viewer.Show(this);
            Clipboard.SetText(text);
            ShowStatus("نُسخ الكلام ✓ — الصقه في أي مكان (راجع المسافات في أول الأسطر)", 6);
        }
        catch (Exception ex)
        {
            Log.Error("ocr", ex);
            Dialogs.Error(this, "تعذّرت قراءة النص:\n" + ex.Message);
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
