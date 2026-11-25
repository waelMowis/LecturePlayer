using System.Text;
using System.Text.Json;

namespace LecturePlayer.Data;

public sealed record Chapter(long TimeMs, string Title);

public sealed record QuizQuestion(string Question, List<string> Options, int Answer, string Explanation, long? TimeMs);

public sealed record AskSource(int Number, VideoItem Video, long TimeMs, string Text);

/// <summary>
/// المهام اللي بتستخدم Claude: ملخص، فصول، اختبار، واسأل الكورس.
/// البرومبتات والتحليل هنا (بتتختبر من غير نت)، والإرسال في ClaudeClient.
/// </summary>
public static class AiTasks
{
    private const string Style =
        "اكتب بالعربية الفصحى المبسّطة الواضحة، واترك المصطلحات التقنية وأسماء الأكواد بالإنجليزية كما هي. " +
        "لا تخترع معلومات غير موجودة في النص.";

    // ---------------- ملخص ----------------

    public static (string System, string User, int MaxTokens) SummaryPrompt(string videoTitle, Transcript t) => (
        "أنت مساعد مذاكرة يلخّص محاضرات الفيديو لطالب. " + Style,
        $"هذا نص فيديو بعنوان «{videoTitle}» (في أول كل سطر الوقت):\n\n<transcript>\n{t.ToTimedText()}\n</transcript>\n\n" +
        "اكتب ملخص Markdown يحتوي على:\n" +
        "1. **الفكرة الأساسية** في سطرين.\n" +
        "2. **النقاط المهمة** (bullets) وبجانب كل نقطة وقتها بالشكل [د:ث].\n" +
        "3. **مصطلحات** مع شرح سطر لكل منها (إن وجدت).\n" +
        "4. **أكواد أو أوامر مهمة** في code blocks (إن ذُكرت).\n" +
        "5. **أسئلة لمراجعة نفسك** (3 أسئلة).",
        1800);

    // ---------------- فصول ----------------

    public static (string System, string User, int MaxTokens) ChaptersPrompt(string videoTitle, Transcript t) => (
        "أنت تقسّم فيديوهات تعليمية إلى فصول. " + Style + " أجب بـ JSON فقط دون أي كلام آخر.",
        $"نص فيديو «{videoTitle}» (مدته نحو {Transcript.Short(t.DurationMs)}):\n\n<transcript>\n{t.ToTimedText()}\n</transcript>\n\n" +
        "قسّمه إلى فصول (من 4 إلى 15 حسب الطول)، يبدأ كل فصل عند تغيّر حقيقي في الموضوع. يبدأ أول فصل عند 0:00.\n" +
        "أجب بهذا الشكل بالضبط:\n[{\"time\":\"0:00\",\"title\":\"عنوان قصير\"}, …]",
        1200);

    public static List<Chapter> ParseChapters(string reply, long durationMs = 0)
    {
        var list = new List<Chapter>();
        using var doc = ParseJsonFromReply(reply);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
            : doc.RootElement.TryGetProperty("chapters", out var c) ? c : default;
        if (arr.ValueKind != JsonValueKind.Array) throw new FormatException("الرد ليس قائمة فصول");
        foreach (var e in arr.EnumerateArray())
        {
            string? title = Str(e, "title");
            long? t = Transcript.ParseShort(Str(e, "time"));
            if (string.IsNullOrWhiteSpace(title) || t == null) continue;
            if (durationMs > 0 && t > durationMs) continue;
            list.Add(new Chapter(t.Value, title.Trim()));
        }
        return list.GroupBy(c => c.TimeMs).Select(g => g.First()).OrderBy(c => c.TimeMs).ToList();
    }

    // ---------------- اختبار ----------------

    public static (string System, string User, int MaxTokens) QuizPrompt(string videoTitle, Transcript t, int count = 6) => (
        "أنت معلم تكتب أسئلة اختيار من متعدد تقيس الفهم لا الحفظ. " + Style + " أجب بـ JSON فقط.",
        $"نص فيديو «{videoTitle}»:\n\n<transcript>\n{t.ToTimedText()}\n</transcript>\n\n" +
        $"اكتب {count} أسئلة اختيار من متعدد على أهم الأفكار. لكل سؤال 4 اختيارات، واحد فقط صحيح، والخاطئة مقنعة. " +
        "نوّع موضع الإجابة الصحيحة. ولكل سؤال الوقت الذي شُرحت فيه الإجابة.\n" +
        "الشكل بالضبط:\n[{\"question\":\"…\",\"options\":[\"…\",\"…\",\"…\",\"…\"],\"answer\":0,\"explanation\":\"لماذا هذه هي الصحيحة\",\"time\":\"4:05\"}]",
        2500);

    public static List<QuizQuestion> ParseQuiz(string reply)
    {
        var list = new List<QuizQuestion>();
        using var doc = ParseJsonFromReply(reply);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
            : doc.RootElement.TryGetProperty("questions", out var q) ? q : default;
        if (arr.ValueKind != JsonValueKind.Array) throw new FormatException("الرد ليس قائمة أسئلة");
        foreach (var e in arr.EnumerateArray())
        {
            string? question = Str(e, "question");
            if (string.IsNullOrWhiteSpace(question) || !e.TryGetProperty("options", out var opts) || opts.ValueKind != JsonValueKind.Array) continue;
            var options = opts.EnumerateArray().Select(o => o.ToString().Trim()).Where(o => o.Length > 0).ToList();
            int answer = e.TryGetProperty("answer", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt32() : -1;
            if (options.Count < 2 || answer < 0 || answer >= options.Count) continue;
            list.Add(new QuizQuestion(question.Trim(), options, answer, Str(e, "explanation") ?? "", Transcript.ParseShort(Str(e, "time"))));
        }
        return list;
    }

    // ---------------- الكتب ----------------

    public static (string System, string User, int MaxTokens) BookSummaryPrompt(string book, string part, string pagesText) => (
        "أنت مساعد مذاكرة يلخّص فصول الكتب لطالب. " + Style,
        $"هذا نص «{part}» من كتاب «{book}» (قبل كل صفحة رقمها بالشكل [ص 12]):\n\n<book>\n{pagesText}\n</book>\n\n" +
        "اكتب ملخص Markdown يحتوي على:\n" +
        "1. **الفكرة الأساسية** في سطرين.\n" +
        "2. **النقاط المهمة** (bullets) وبجانب كل نقطة رقم الصفحة بالشكل (ص 12).\n" +
        "3. **مصطلحات وتعريفات** مع شرح سطر لكل منها (إن وجدت).\n" +
        "4. **أمثلة أو أكواد مهمة** (إن وجدت).\n" +
        "5. **أسئلة لمراجعة نفسك** (3 أسئلة).",
        2000);

    public static (string System, string User, int MaxTokens) BookExplainPrompt(string book, int page, string pagesText, string? question) => (
        "أنت معلم صبور تشرح لطالب صفحة من كتاب يذاكره. " + Style,
        $"هذا نص من كتاب «{book}» حول الصفحة {page}:\n\n<book>\n{pagesText}\n</book>\n\n" +
        (string.IsNullOrWhiteSpace(question)
            ? $"اشرح الصفحة {page} بأسلوب بسيط: الفكرة، ولماذا هي مهمة، ومثال يوضّحها، وأي مصطلح صعب فيها. وإن كان فيها كود فاشرحه سطرًا سطرًا."
            : $"سؤال الطالب: {question}\nأجب من هذا النص فقط، وإن لم تكن الإجابة فيه فقل ذلك واشرح ما تستطيع منه."),
        1500);

    public static (string System, string User, int MaxTokens) BookQuizPrompt(string book, string part, string pagesText, int count = 6) => (
        "أنت معلم تكتب أسئلة اختيار من متعدد تقيس الفهم لا الحفظ. " + Style + " أجب بـ JSON فقط.",
        $"هذا نص «{part}» من كتاب «{book}» (قبل كل صفحة رقمها [ص 12]):\n\n<book>\n{pagesText}\n</book>\n\n" +
        $"اكتب {count} أسئلة اختيار من متعدد على أهم الأفكار. لكل سؤال 4 اختيارات، واحد فقط صحيح، والخاطئة مقنعة. " +
        "نوّع موضع الإجابة الصحيحة. ولكل سؤال رقم الصفحة التي فيها الإجابة.\n" +
        "الشكل بالضبط:\n[{\"question\":\"…\",\"options\":[\"…\",\"…\",\"…\",\"…\"],\"answer\":0,\"explanation\":\"لماذا هذه هي الصحيحة\",\"page\":12}]",
        2500);

    /// <summary>أسئلة الكتاب: الصفحة بتتحط مكان الوقت (QuizForm بيرجّعها لـ «روح للصفحة»).</summary>
    public static List<QuizQuestion> ParseBookQuiz(string reply, int maxPage)
    {
        var list = new List<QuizQuestion>();
        using var doc = ParseJsonFromReply(reply);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
            : doc.RootElement.TryGetProperty("questions", out var q) ? q : default;
        if (arr.ValueKind != JsonValueKind.Array) throw new FormatException("الرد ليس قائمة أسئلة");
        foreach (var e in arr.EnumerateArray())
        {
            string? question = Str(e, "question");
            if (string.IsNullOrWhiteSpace(question) || !e.TryGetProperty("options", out var opts) || opts.ValueKind != JsonValueKind.Array) continue;
            var options = opts.EnumerateArray().Select(o => o.ToString().Trim()).Where(o => o.Length > 0).ToList();
            int answer = e.TryGetProperty("answer", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt32() : -1;
            if (options.Count < 2 || answer < 0 || answer >= options.Count) continue;
            long? page = e.TryGetProperty("page", out var pg) && pg.ValueKind == JsonValueKind.Number && pg.GetInt32() is var n && n >= 1 && (maxPage <= 0 || n <= maxPage) ? n : null;
            list.Add(new QuizQuestion(question.Trim(), options, answer, Str(e, "explanation") ?? "", page));
        }
        return list;
    }

    // ---------------- اسأل الكورس ----------------

    public static (string System, string User, int MaxTokens) AskPrompt(string courseName, string question, IReadOnlyList<AskSource> sources)
    {
        var sb = new StringBuilder();
        foreach (var s in sources)
            sb.Append($"<source n=\"{s.Number}\" video=\"{s.Video.Title}\" time=\"{Transcript.Short(s.TimeMs)}\">\n{s.Text}\n</source>\n");
        return (
            $"أنت مساعد مذاكرة لدورة «{courseName}». تجيب من مقاطع نصوص فيديوهات الدورة فقط. " + Style +
            " بعد كل معلومة ضع رقم المصدر بالشكل [1] أو [2][3]. " +
            "إن لم تكن الإجابة في المقاطع فقل ذلك بصراحة واقترح على الطالب أين يبحث، ولا تُجب من عندك.",
            $"<sources>\n{sb}</sources>\n\nالسؤال: {question}",
            1500);
    }

    /// <summary>أرقام المصادر اللي اتذكرت في الرد ([1] [2]…).</summary>
    public static List<int> CitedSources(string reply)
    {
        var list = new List<int>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(reply, @"\[(\d{1,2})\]"))
            if (int.TryParse(m.Groups[1].Value, out int n) && !list.Contains(n)) list.Add(n);
        return list;
    }

    // ---------------- مساعدات ----------------

    /// <summary>JSON من رد الموديل حتى لو جوه ```json أو قبله كلام.</summary>
    public static JsonDocument ParseJsonFromReply(string reply)
    {
        string s = reply.Trim();
        int fence = s.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            int nl = s.IndexOf('\n', fence);
            int close = nl >= 0 ? s.IndexOf("```", nl, StringComparison.Ordinal) : -1;
            if (nl >= 0 && close > nl) s = s[(nl + 1)..close].Trim();
        }
        int start = s.IndexOfAny(new[] { '[', '{' });
        if (start < 0) throw new FormatException("لا يوجد JSON في الرد");
        char open = s[start], closeCh = open == '[' ? ']' : '}';
        int end = s.LastIndexOf(closeCh);
        if (end <= start) throw new FormatException("الرد ينقصه JSON");
        return JsonDocument.Parse(s[start..(end + 1)], new JsonDocumentOptions { AllowTrailingCommas = true });
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;
}
