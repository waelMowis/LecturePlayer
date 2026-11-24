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
        "اكتب بالعربي المصري البسيط الواضح، وسيب المصطلحات التقنية وأسماء الأكواد بالإنجليزي زي ما هي. " +
        "متخترعش معلومات مش موجودة في النص.";

    // ---------------- ملخص ----------------

    public static (string System, string User, int MaxTokens) SummaryPrompt(string videoTitle, Transcript t) => (
        "إنت مساعد مذاكرة بيلخص محاضرات فيديو لطالب. " + Style,
        $"ده نص فيديو بعنوان «{videoTitle}» (كل سطر في أوله الوقت):\n\n<transcript>\n{t.ToTimedText()}\n</transcript>\n\n" +
        "اعمل ملخص Markdown فيه:\n" +
        "1. **الفكرة الأساسية** في سطرين.\n" +
        "2. **النقط المهمة** (bullets) وجنب كل نقطة وقتها بالشكل [م:ث].\n" +
        "3. **مصطلحات** مع شرح سطر لكل واحد (لو فيه).\n" +
        "4. **أكواد أو أوامر مهمة** في code blocks (لو اتذكرت).\n" +
        "5. **أسئلة تراجع بيها نفسك** (3 أسئلة).",
        1800);

    // ---------------- فصول ----------------

    public static (string System, string User, int MaxTokens) ChaptersPrompt(string videoTitle, Transcript t) => (
        "إنت بتقسم فيديوهات تعليمية لفصول. " + Style + " رد بـ JSON بس من غير أي كلام تاني.",
        $"نص فيديو «{videoTitle}» (مدته حوالي {Transcript.Short(t.DurationMs)}):\n\n<transcript>\n{t.ToTimedText()}\n</transcript>\n\n" +
        "قسّمه لفصول (من 4 لـ 15 حسب الطول)، كل فصل بيبدأ عند تغيير حقيقي في الموضوع. أول فصل يبدأ 0:00.\n" +
        "رد بالشكل ده بالظبط:\n[{\"time\":\"0:00\",\"title\":\"عنوان قصير\"}, …]",
        1200);

    public static List<Chapter> ParseChapters(string reply, long durationMs = 0)
    {
        var list = new List<Chapter>();
        using var doc = ParseJsonFromReply(reply);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
            : doc.RootElement.TryGetProperty("chapters", out var c) ? c : default;
        if (arr.ValueKind != JsonValueKind.Array) throw new FormatException("الرد مش قايمة فصول");
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
        "إنت مدرس بيعمل أسئلة اختيار من متعدد تقيس الفهم مش الحفظ. " + Style + " رد بـ JSON بس.",
        $"نص فيديو «{videoTitle}»:\n\n<transcript>\n{t.ToTimedText()}\n</transcript>\n\n" +
        $"اعمل {count} أسئلة اختيار من متعدد على أهم الأفكار. كل سؤال 4 اختيارات، واحد بس صح، والغلط يكون مقنع. " +
        "نوّع مكان الإجابة الصح. ولكل سؤال الوقت اللي الإجابة اتشرحت فيه.\n" +
        "الشكل بالظبط:\n[{\"question\":\"…\",\"options\":[\"…\",\"…\",\"…\",\"…\"],\"answer\":0,\"explanation\":\"ليه دي الصح\",\"time\":\"4:05\"}]",
        2500);

    public static List<QuizQuestion> ParseQuiz(string reply)
    {
        var list = new List<QuizQuestion>();
        using var doc = ParseJsonFromReply(reply);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
            : doc.RootElement.TryGetProperty("questions", out var q) ? q : default;
        if (arr.ValueKind != JsonValueKind.Array) throw new FormatException("الرد مش قايمة أسئلة");
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
        "إنت مساعد مذاكرة بيلخص فصول كتب لطالب. " + Style,
        $"ده نص «{part}» من كتاب «{book}» (قبل كل صفحة رقمها بالشكل [ص 12]):\n\n<book>\n{pagesText}\n</book>\n\n" +
        "اعمل ملخص Markdown فيه:\n" +
        "1. **الفكرة الأساسية** في سطرين.\n" +
        "2. **النقط المهمة** (bullets) وجنب كل نقطة رقم الصفحة بالشكل (ص 12).\n" +
        "3. **مصطلحات وتعريفات** مع شرح سطر لكل واحد (لو فيه).\n" +
        "4. **أمثلة أو أكواد مهمة** (لو فيه).\n" +
        "5. **أسئلة تراجع بيها نفسك** (3 أسئلة).",
        2000);

    public static (string System, string User, int MaxTokens) BookExplainPrompt(string book, int page, string pagesText, string? question) => (
        "إنت مدرس صبور بيشرح لطالب صفحة من كتاب بيذاكره. " + Style,
        $"ده نص من كتاب «{book}» حوالين صفحة {page}:\n\n<book>\n{pagesText}\n</book>\n\n" +
        (string.IsNullOrWhiteSpace(question)
            ? $"اشرح صفحة {page} بأسلوب بسيط: الفكرة، وليه مهمة، ومثال من عندك يوضّحها، وأي مصطلح صعب فيها. ولو فيها كود اشرحه سطر سطر."
            : $"سؤال الطالب: {question}\nجاوب من النص ده بس، ولو الإجابة مش فيه قول كده واشرح اللي تقدر عليه منه."),
        1500);

    public static (string System, string User, int MaxTokens) BookQuizPrompt(string book, string part, string pagesText, int count = 6) => (
        "إنت مدرس بيعمل أسئلة اختيار من متعدد تقيس الفهم مش الحفظ. " + Style + " رد بـ JSON بس.",
        $"ده نص «{part}» من كتاب «{book}» (قبل كل صفحة رقمها [ص 12]):\n\n<book>\n{pagesText}\n</book>\n\n" +
        $"اعمل {count} أسئلة اختيار من متعدد على أهم الأفكار. كل سؤال 4 اختيارات، واحد بس صح، والغلط يكون مقنع. " +
        "نوّع مكان الإجابة الصح. ولكل سؤال رقم الصفحة اللي فيها الإجابة.\n" +
        "الشكل بالظبط:\n[{\"question\":\"…\",\"options\":[\"…\",\"…\",\"…\",\"…\"],\"answer\":0,\"explanation\":\"ليه دي الصح\",\"page\":12}]",
        2500);

    /// <summary>أسئلة الكتاب: الصفحة بتتحط مكان الوقت (QuizForm بيرجّعها لـ «روح للصفحة»).</summary>
    public static List<QuizQuestion> ParseBookQuiz(string reply, int maxPage)
    {
        var list = new List<QuizQuestion>();
        using var doc = ParseJsonFromReply(reply);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
            : doc.RootElement.TryGetProperty("questions", out var q) ? q : default;
        if (arr.ValueKind != JsonValueKind.Array) throw new FormatException("الرد مش قايمة أسئلة");
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
            $"إنت مساعد مذاكرة لكورس «{courseName}». بتجاوب من مقاطع من نصوص فيديوهات الكورس بس. " + Style +
            " بعد كل معلومة حط رقم المصدر بالشكل [1] أو [2][3]. " +
            "لو المقاطع مفيهاش الإجابة قول كده بصراحة واقترح الطالب يدور فين، ومتجاوبش من دماغك.",
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
        if (start < 0) throw new FormatException("مفيش JSON في الرد");
        char open = s[start], closeCh = open == '[' ? ']' : '}';
        int end = s.LastIndexOf(closeCh);
        if (end <= start) throw new FormatException("JSON ناقص في الرد");
        return JsonDocument.Parse(s[start..(end + 1)], new JsonDocumentOptions { AllowTrailingCommas = true });
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;
}
