using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>أساس الشبابيك الجانبية (مش بتوقف البرنامج — تقدر تكمل تتفرج وهي مفتوحة).</summary>
internal abstract class ToolWindow : Form
{
    protected ToolWindow(string title, int w, int h)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        RightToLeft = RightToLeft.Yes;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(w, h);
        MinimumSize = new Size(Math.Min(w, 420), Math.Min(h, 300));
        Padding = new Padding(10, 8, 10, 4);
        KeyPreview = true;
        HandleCreated += (_, _) => NativeMethods.UseDarkTitleBar(this);
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        if (Theme.AppIcon is { } icon) Icon = icon;
    }

    /// <summary>
    /// بعد ما الشباك يتبني: كل المحتوى (غير شريط الأزرار) بيتنقل جوه كارت مدور،
    /// عشان كل الشبابيك تبقى بنفس شكل البرنامج.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var content = Controls.Cast<Control>().Where(c => !IsBar(c)).ToList();
        if (content.Count == 0) return;
        var card = new Card { Dock = DockStyle.Fill, Fill = Theme.Panel };
        var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(10, 8, 10, 6) };
        SuspendLayout();
        foreach (var c in content) { Controls.Remove(c); inner.Controls.Add(c); } // نفس الترتيب ← نفس الـ Dock
        card.Controls.Add(inner);
        Controls.Add(card);
        card.BringToFront();
        ResumeLayout(true);
    }

    private static bool IsBar(Control c) => c.Tag is "bar";

    protected static PillButton Btn(string text, Action click, bool accent = false)
    {
        var b = new PillButton(text, accent ? PillStyle.Primary : PillStyle.Ghost) { Height = 34, Margin = new Padding(8, 0, 0, 0) };
        b.Width = Math.Max(90, b.Width);
        b.Click += (_, _) => click();
        return b;
    }

    protected static FlowLayoutPanel ButtonBar() =>
        new() { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(2, 10, 2, 0), BackColor = Theme.Window, Tag = "bar" };

    protected static TextBox ReadOnlyText(string text = "") => FormKit.Style(new TextBox
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"), Font = new Font("Segoe UI", 10.5f), WordWrap = true
    });
}

/// <summary>عرض نص (ملخص، نص OCR…) مع نسخ وحفظ وأزرار إضافية.</summary>
internal sealed class TextViewerForm : ToolWindow
{
    private readonly TextBox _box;

    /// <summary>للأكواد: من الشمال لليمين وخط ثابت العرض.</summary>
    public void UseCodeFont()
    {
        _box.RightToLeft = RightToLeft.No;
        _box.Font = new Font("Consolas", 11f);
        _box.WordWrap = false;
        _box.ScrollBars = ScrollBars.Both;
    }

    public TextViewerForm(string title, string text, string saveName, params (string Text, Action<string> Click)[] extra)
        : base(title, 720, 600)
    {
        var box = _box = ReadOnlyText(text);
        box.ReadOnly = false; // تقدر تعدّل قبل ما تنسخ أو تحفظ
        var bar = ButtonBar();
        bar.Controls.Add(Btn("نسخ", () => { Clipboard.SetText(box.Text.Length > 0 ? box.Text : " "); ShowCopied(); }, accent: true));
        bar.Controls.Add(Btn("حفظ…", () => Save(box.Text, saveName)));
        foreach (var (t, click) in extra) bar.Controls.Add(Btn(t, () => click(box.Text)));
        bar.Controls.Add(Btn("إغلاق", Close));
        Controls.Add(box);
        Controls.Add(bar);

        void ShowCopied() => Text = title + "  — نُسخ ✓";
    }

    private void Save(string text, string name)
    {
        using var dlg = new SaveFileDialog { Filter = "Markdown (*.md)|*.md|نص (*.txt)|*.txt", FileName = name };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dlg.FileName, text, new System.Text.UTF8Encoding(true)); }
        catch (Exception ex) { Dialogs.Error(this, ex.Message); }
    }
}

/// <summary>اختبار اختيار من متعدد، سؤال سؤال، مع الشرح والرجوع للجزء في الفيديو.</summary>
internal sealed class QuizForm : ToolWindow
{
    private readonly List<QuizQuestion> _questions;
    private readonly Action<long> _jump;
    private readonly Action<int, int> _finished;
    private readonly Label _header = new() { Dock = DockStyle.Top, Height = 28, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _question = new() { Dock = DockStyle.Top, Height = 80, Font = new Font("Segoe UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.TopRight };
    private readonly FlowLayoutPanel _options = new() { Dock = DockStyle.Top, Height = 210, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Panel };
    private readonly Label _explain = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopRight, Font = new Font("Segoe UI", 10.5f) };
    private readonly PillButton _check, _next, _goTo;
    private int _index, _score;
    private bool _answered;

    public QuizForm(string title, List<QuizQuestion> questions, Action<long> jump, Action<int, int> finished, string goToText = "اذهب إلى هذا الجزء في الفيديو")
        : base("اختبار: " + title, 640, 520)
    {
        _questions = questions;
        _jump = jump;
        _finished = finished;
        var bar = ButtonBar();
        _check = Btn("تأكيد", Check, accent: true);
        _next = Btn("التالي ←", Next);
        _goTo = Btn(goToText, () =>
        {
            if (_questions[_index].TimeMs is { } t) _jump(t);
        });
        bar.Controls.Add(_check);
        bar.Controls.Add(_next);
        bar.Controls.Add(_goTo);
        Controls.Add(_explain);
        Controls.Add(_options);
        Controls.Add(_question);
        Controls.Add(_header);
        Controls.Add(bar);
        ShowQuestion();
    }

    private void ShowQuestion()
    {
        var q = _questions[_index];
        _answered = false;
        _header.Text = $"سؤال {_index + 1} من {_questions.Count}     الدرجة: {_score}";
        _question.Text = q.Question;
        _options.Controls.Clear();
        for (int i = 0; i < q.Options.Count; i++)
        {
            var rb = new OptionCard { Text = q.Options[i], Tag = i, Width = _options.ClientSize.Width - 24 };
            rb.DoubleClick += (_, _) => Check();
            _options.Controls.Add(rb);
        }
        _explain.Text = "";
        _check.Enabled = true;
        _next.Enabled = false;
        _goTo.Visible = false;
    }

    private void Check()
    {
        if (_answered) return;
        var chosen = _options.Controls.OfType<OptionCard>().FirstOrDefault(r => r.Checked);
        if (chosen == null) { _explain.ForeColor = Theme.Muted; _explain.Text = "اختر إجابة أولًا."; return; }
        _answered = true;
        var q = _questions[_index];
        bool right = (int)chosen.Tag! == q.Answer;
        if (right) _score++;
        foreach (var rb in _options.Controls.OfType<OptionCard>())
        {
            int i = (int)rb.Tag!;
            if (i == q.Answer) rb.ForeColor = Theme.Success;
            else if (rb == chosen) rb.ForeColor = Theme.Danger;
            rb.Enabled = i == q.Answer || rb == chosen;
        }
        _explain.ForeColor = right ? Theme.Success : Theme.Danger;
        _explain.Text = (right ? "✓ صحيح! " : "✗ ليست هذه. ") + q.Explanation;
        _header.Text = $"سؤال {_index + 1} من {_questions.Count}     الدرجة: {_score}";
        _check.Enabled = false;
        _next.Enabled = true;
        _next.Text = _index == _questions.Count - 1 ? "النتيجة" : "التالي ←";
        _goTo.Visible = q.TimeMs != null;
    }

    private void Next()
    {
        if (_index < _questions.Count - 1) { _index++; ShowQuestion(); return; }
        _finished(_score, _questions.Count);
        int pct = _score * 100 / Math.Max(1, _questions.Count);
        _options.Controls.Clear();
        _question.Text = $"النتيجة: {_score} من {_questions.Count}  ({pct}%)";
        _explain.ForeColor = Theme.Text;
        _explain.Text = pct >= 80 ? "ممتاز — فهمت الفيديو جيدًا 👏"
            : pct >= 50 ? "جيد، لكن راجع الأجزاء التي أخطأت فيها."
            : "تحتاج إلى إعادة هذه الأجزاء — استخدم «اذهب إلى هذا الجزء» في الأسئلة التي أخطأت فيها في المرة القادمة.";
        _check.Enabled = _next.Enabled = false;
        _goTo.Visible = false;
    }
}

/// <summary>«اسأل الكورس»: سؤال ← إجابة من نصوص الفيديوهات مع المصادر (دبل كليك يشغّل اللحظة دي).</summary>
internal sealed class AskForm : ToolWindow
{
    private readonly Func<string, CancellationToken, Task<(string Answer, List<AskSource> Sources)>> _ask;
    private readonly Action<VideoItem, long> _play;
    private readonly TextBox _question = FormKit.Style(new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f) });
    private readonly TextBox _answer = ReadOnlyText();
    private readonly ListView _sources = DarkList.Setup(new ListView { Dock = DockStyle.Bottom, Height = 160 });
    private readonly PillButton _go;
    private CancellationTokenSource? _cts;

    public AskForm(string courseName, Func<string, CancellationToken, Task<(string, List<AskSource>)>> ask, Action<VideoItem, long> play)
        : base("اسأل الدورة: " + courseName, 760, 640)
    {
        _ask = ask;
        _play = play;
        var top = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 4, 0, 6) };
        _go = Btn("اسأل", () => _ = AskAsync(), accent: true);
        _go.Dock = DockStyle.Left;
        top.Controls.Add(_question);
        top.Controls.Add(_go);
        _question.PlaceholderText = "اكتب سؤالك… (مثلًا: ما الفرق بين interface و abstract class؟)";
        _question.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = AskAsync(); } };

        _sources.Columns.Add("#", 40);
        _sources.Columns.Add("الفيديو", 260);
        _sources.Columns.Add("الوقت", 70);
        _sources.Columns.Add("المقطع", 360);
        DarkList.ReverseColumns(_sources);
        _sources.DoubleClick += (_, _) =>
        {
            if (_sources.SelectedItems.Count > 0 && _sources.SelectedItems[0].Tag is AskSource s) _play(s.Video, s.TimeMs);
        };
        var hint = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = Theme.Muted, Text = "المصادر (النقر المزدوج يشغّل هذه اللحظة):", TextAlign = ContentAlignment.BottomRight };

        Controls.Add(_answer);
        Controls.Add(hint);
        Controls.Add(_sources);
        Controls.Add(top);
        FormClosed += (_, _) => _cts?.Cancel();
        Shown += (_, _) => _question.Focus();
    }

    private async Task AskAsync()
    {
        string q = _question.Text.Trim();
        if (q.Length == 0) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _go.Enabled = false;
        _answer.Text = "جارٍ البحث في نصوص الفيديوهات وسؤال Claude…";
        _sources.Items.Clear();
        try
        {
            var (answer, sources) = await _ask(q, _cts.Token);
            _answer.Text = answer.Replace("\r\n", "\n").Replace("\n", "\r\n");
            var cited = AiTasks.CitedSources(answer);
            foreach (var s in sources.OrderBy(s => cited.Contains(s.Number) ? 0 : 1).ThenBy(s => s.Number))
            {
                var item = new ListViewItem(new[]
                {
                    s.Number.ToString(), s.Video.Title, Transcript.Short(s.TimeMs),
                    s.Text.Length > 120 ? s.Text[..120] + "…" : s.Text
                }) { Tag = s, ForeColor = cited.Contains(s.Number) ? Theme.Text : Theme.Muted };
                _sources.Items.Add(item);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _answer.Text = ex.Message; }
        finally { if (!IsDisposed) _go.Enabled = true; }
    }
}

/// <summary>بحث في نصوص كل الفيديوهات (Ctrl+Shift+F).</summary>
internal sealed class TranscriptSearchForm : ToolWindow
{
    private readonly Func<string, List<TranscriptIndex.Hit>> _search;
    private readonly Action<VideoItem, long> _play;
    private readonly TextBox _query = FormKit.Style(new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 11f) });
    private readonly Label _info = new() { Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight };
    private readonly ListView _list = DarkList.Setup(new ListView { Dock = DockStyle.Fill });
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 300 };
    private readonly string _status;

    public TranscriptSearchForm(string status, Func<string, List<TranscriptIndex.Hit>> search, Action<VideoItem, long> play)
        : base("بحث في كلام الفيديوهات", 780, 560)
    {
        _search = search;
        _play = play;
        _status = status;
        _query.PlaceholderText = "اكتب كلمة أو أكثر… (يجب أن تكون كل الكلمات في المقطع نفسه)";
        _list.Columns.Add("الفيديو", 230);
        _list.Columns.Add("الوقت", 70);
        _list.Columns.Add("الكلام", 440);
        DarkList.ReverseColumns(_list);
        _list.DoubleClick += (_, _) => PlaySelected();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) PlaySelected(); };
        _query.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _query.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _list.Items.Count > 0) { _list.Focus(); _list.Items[0].Selected = true; e.Handled = true; }
        };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Run(); };
        _info.Text = status;
        Controls.Add(_list);
        Controls.Add(_info);
        Controls.Add(_query);
        FormClosed += (_, _) => _debounce.Dispose();
        Shown += (_, _) => _query.Focus();
    }

    private void Run()
    {
        string q = _query.Text.Trim();
        _list.BeginUpdate();
        _list.Items.Clear();
        if (q.Length >= 2)
        {
            var hits = _search(q);
            foreach (var h in hits)
                _list.Items.Add(new ListViewItem(new[] { h.Video.Title, Transcript.Short(h.TimeMs), h.Snippet }) { Tag = h });
            _info.Text = hits.Count == 0 ? "لا توجد نتائج. " + _status : $"{hits.Count} نتيجة — النقر المزدوج أو Enter يشغّل هذه اللحظة";
        }
        else _info.Text = _status;
        _list.EndUpdate();
    }

    private void PlaySelected()
    {
        if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is TranscriptIndex.Hit h) _play(h.Video, Math.Max(0, h.TimeMs - 1500));
    }
}

/// <summary>بحث في نص كل الكتب (بالعربي من غير التشكيل، أو بالإنجليزي).</summary>
internal sealed class BookSearchForm : ToolWindow
{
    private readonly Func<string, List<Data.BookText.Hit>> _search;
    private readonly Action<Data.BookText.Hit, string> _open;
    private readonly TextBox _query = FormKit.Style(new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 11f) });
    private readonly Label _info = new() { Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight };
    private readonly ListView _list = DarkList.Setup(new ListView { Dock = DockStyle.Fill });
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 300 };
    private string _status = "";

    public int ResultCount => _list.Items.Count;

    public BookSearchForm(Func<string, List<Data.BookText.Hit>> search, Action<Data.BookText.Hit, string> open)
        : base("بحث داخل كل الكتب", 820, 560)
    {
        _search = search;
        _open = open;
        _query.PlaceholderText = "اكتب كلمة أو أكثر… (يجب أن تكون كل الكلمات في الصفحة نفسها، والتشكيل لا يؤثر)";
        _list.Columns.Add("الكتاب", 220);
        _list.Columns.Add("الصفحة", 60);
        _list.Columns.Add("الكلام", 500);
        DarkList.ReverseColumns(_list);
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) OpenSelected(); };
        _query.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _query.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _list.Items.Count > 0) { _list.Focus(); _list.Items[0].Selected = true; e.Handled = true; }
        };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Rerun(); };
        Controls.Add(_list);
        Controls.Add(_info);
        Controls.Add(_query);
        FormClosed += (_, _) => _debounce.Dispose();
        Shown += (_, _) => _query.Focus();
    }

    public void SetQuery(string q) { _query.Text = q; Rerun(); }

    public void SetStatus(string s)
    {
        _status = s;
        if (_query.Text.Trim().Length < 2) _info.Text = s;
    }

    public void Rerun()
    {
        if (IsDisposed) return;
        string q = _query.Text.Trim();
        _list.BeginUpdate();
        _list.Items.Clear();
        if (q.Length >= 2)
        {
            var hits = _search(q);
            foreach (var h in hits)
                _list.Items.Add(new ListViewItem(new[] { h.Book.Title, h.Page.ToString(), h.Snippet }) { Tag = h });
            _info.Text = hits.Count == 0 ? "لا توجد نتائج. " + _status : $"{hits.Count} نتيجة — النقر المزدوج أو Enter يفتح الكتاب على الصفحة";
        }
        else _info.Text = _status;
        _list.EndUpdate();
    }

    private void OpenSelected()
    {
        if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is Data.BookText.Hit h) _open(h, _query.Text.Trim());
    }
}
