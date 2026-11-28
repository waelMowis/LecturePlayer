using System.Text;
using System.Text.RegularExpressions;

namespace LecturePlayer.Data;

/// <summary>
/// ترجمة برامج أردوينو من غير نت: arduino-cli + نواة arduino:avr متثبتين جنب البرنامج (فولدر arduino)
/// ← ملف hex بيشتغل في المحاكي (avr8js) جوه السبورة. الأخطاء بترجع برقم السطر وشرح عربي.
/// </summary>
public static class Arduino
{
    public sealed record Problem(int Line, int Col, string Kind, string Msg, string Orig);
    public sealed record Result(bool Ok, string? Hex, List<Problem> Problems, int Flash, int FlashMax, int Ram, int RamMax, string? Error, long Ms);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static string WorkDir => Path.Combine(Storage.AppDataDir, "arduino");

    /// <summary>فولدر الأدوات: جنب البرنامج (المثبّت)، أو (للمطورين) Releases\_tools\arduino فوق فولدر البناء.</summary>
    public static string? ToolsDir() => BundledDir("arduino", "arduino-cli.exe");

    /// <summary>فولدر مدمج مع البرنامج (arduino، scratch…): جنب البرنامج، أو (للمطورين) Releases\_tools\الاسم.</summary>
    public static string? BundledDir(string name, string marker)
    {
        string d = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(Path.Combine(d, marker))) return d;
        for (var p = new DirectoryInfo(AppContext.BaseDirectory); p != null; p = p.Parent)
        {
            string c = Path.Combine(p.FullName, "Releases", "_tools", name);
            if (File.Exists(Path.Combine(c, marker))) return c;
        }
        return null;
    }

    /// <summary>البرمجة بالكتل (مبنية على Scratch 3): الواجهة المجمّدة + مكتبة الوسائط.</summary>
    public static string? ScratchDir() => BundledDir("scratch", "scratch-gui.js");

    public static async Task<Result> CompileAsync(string code, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? tools = ToolsDir();
        if (tools == null) return Fail("أدوات ترجمة أردوينو غير موجودة بجانب البرنامج. أعد تثبيت «مسار» من أحدث إصدار.", sw);
        if (code.Length > 200_000) return Fail("البرنامج أطول من المسموح به.", sw);

        await Gate.WaitAsync(ct);
        try
        {
            string sketch = Path.Combine(WorkDir, "sketch", "masar");
            string build = Path.Combine(WorkDir, "build");
            string outDir = Path.Combine(WorkDir, "out");
            Directory.CreateDirectory(sketch);
            Directory.CreateDirectory(outDir);
            string hexPath = Path.Combine(outDir, "masar.ino.hex");
            if (File.Exists(hexPath)) File.Delete(hexPath);
            await File.WriteAllTextAsync(Path.Combine(sketch, "masar.ino"), code, new UTF8Encoding(false), ct);

            var env = new Dictionary<string, string>
            {
                ["ARDUINO_DIRECTORIES_DATA"] = Path.Combine(tools, "data"),
                ["ARDUINO_DIRECTORIES_USER"] = Path.Combine(tools, "user"),
                ["ARDUINO_DIRECTORIES_DOWNLOADS"] = Path.Combine(WorkDir, "downloads"),
                ["ARDUINO_BUILD_CACHE_PATH"] = Path.Combine(WorkDir, "cache"),
                ["ARDUINO_UPDATER_ENABLE_NOTIFICATION"] = "false",
                ["ARDUINO_METRICS_ENABLED"] = "false",
            };
            var args = new List<string>
            {
                "--config-file", Path.Combine(tools, "arduino-cli.yaml"), "--no-color",
                "compile", "--fqbn", "arduino:avr:uno", "--warnings", "default",
                "--build-path", build, "--output-dir", outDir, sketch
            };
            var r = await ProcessRunner.RunAsync(Path.Combine(tools, "arduino-cli.exe"), args, TimeSpan.FromMinutes(3), ct, workingDirectory: WorkDir, environment: env);
            string all = r.StdErr + "\n" + r.StdOut;
            var problems = Parse(all);
            if (r.TimedOut) return Fail("استغرقت الترجمة وقتًا أطول من المتوقع. أعد المحاولة.", sw);
            if (r.ExitCode != 0 || !File.Exists(hexPath))
            {
                if (problems.Count == 0)
                {
                    string last = all.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !l.StartsWith("Used ", StringComparison.Ordinal)) ?? "";
                    Log.Info("arduino compile failed: " + all.Trim());
                    problems.Add(new Problem(0, 0, "error", Explain(last, out _) ?? "تعذّرت ترجمة البرنامج.", last));
                }
                return new Result(false, null, problems, 0, 0, 0, 0, null, sw.ElapsedMilliseconds);
            }
            var (flash, flashMax, ram, ramMax) = Sizes(r.StdOut);
            return new Result(true, await File.ReadAllTextAsync(hexPath, ct), problems, flash, flashMax, ram, ramMax, null, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error("arduino compile", ex);
            return Fail("تعذّر تشغيل أداة الترجمة: " + ex.Message, sw);
        }
        finally { Gate.Release(); }
    }

    private static Result Fail(string msg, System.Diagnostics.Stopwatch sw) => new(false, null, new(), 0, 0, 0, 0, msg, sw.ElapsedMilliseconds);

    private static readonly Regex LineRx = new(@"(?:^|[\\/])masar\.ino:(\d+):(\d+):\s*(fatal error|error|warning):\s*(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex LinkRx = new(@"undefined reference to [`'‘]([\w:]+)", RegexOptions.CultureInvariant);

    public static List<Problem> Parse(string output)
    {
        var list = new List<Problem>();
        var seen = new HashSet<string>();
        foreach (Match m in LineRx.Matches(output))
        {
            string orig = Norm(m.Groups[4].Value.Trim());
            string kind = m.Groups[3].Value.StartsWith("warn", StringComparison.Ordinal) ? "warning" : "error";
            int line = int.Parse(m.Groups[1].Value), col = int.Parse(m.Groups[2].Value);
            if (!seen.Add(line + "|" + orig)) continue;
            list.Add(new Problem(line, col, kind, Explain(orig, out _) ?? (kind == "error" ? "خطأ في هذا السطر." : "تنبيه في هذا السطر."), orig));
        }
        foreach (Match m in LinkRx.Matches(output))
        {
            string fn = m.Groups[1].Value, orig = "undefined reference to " + fn;
            if (!seen.Add(orig)) continue;
            list.Add(new Problem(0, 0, "error", fn is "setup" or "loop"
                ? $"البرنامج يحتاج إلى الدالة {fn}()‏ حتى لو كانت فارغة."
                : $"الدالة «{fn}» مستخدمة لكنها غير معرّفة.", orig));
        }
        if (output.Contains("Sketch too big", StringComparison.Ordinal) || output.Contains("text section exceeds available space", StringComparison.Ordinal))
            list.Add(new Problem(0, 0, "error", "البرنامج أكبر من ذاكرة لوحة أردوينو أونو (32 كيلوبايت).", "Sketch too big"));
        return list;
    }

    private static string Norm(string s) => s.Replace('‘', '\'').Replace('’', '\'').Replace('“', '"').Replace('”', '"');

    private static readonly (Regex Rx, string Ar)[] Rules =
    [
        (new(@"^'([^']+)' was not declared in this scope"), "«{1}» غير معرّف في هذا الموضع. تأكد من كتابة الاسم بالحروف نفسها (الكبيرة والصغيرة مختلفة)، أو عرّفه قبل استخدامه."),
        (new(@"^'([^']+)' does not name a type"), "«{1}» ليس نوعًا معروفًا. هل أخطأت في كتابته، أو نسيت سطر include للمكتبة؟ وقد يعني أيضًا أن أمرًا كُتب خارج الدوال."),
        (new(@"^([^:]+\.h): No such file or directory"), "المكتبة «{1}» غير موجودة. المكتبات المتاحة: Servo و LiquidCrystal ومكتبات أردوينو الأساسية (EEPROM و SPI و Wire و SoftwareSerial)."),
        (new(@"^expected ';' before"), "تنقص فاصلة منقوطة «;» في نهاية أمر (غالبًا في نهاية السطر السابق)."),
        (new(@"^expected ';'"), "تنقص فاصلة منقوطة «;» في نهاية الأمر."),
        (new(@"^expected '\}' at end of input"), "ينقص قوس إغلاق «}». كل قوس «{» يحتاج إلى قوس «}» يقابله."),
        (new(@"^expected declaration before '\}' token"), "يوجد قوس إغلاق «}» زائد."),
        (new(@"^expected '\)'"), "ينقص قوس إغلاق «)»."),
        (new(@"^expected '\('"), "ينقص قوس فتح «(»."),
        (new(@"^expected '\]'"), "ينقص قوس إغلاق «]»."),
        (new(@"^expected primary-expression before '([^']+)'"), "تعبير ناقص قبل «{1}». راجع الأقواس والعمليات في هذا السطر."),
        (new(@"^expected unqualified-id before"), "صيغة غير متوقعة في هذا الموضع. راجع الأقواس والفواصل حوله."),
        (new(@"^expected initializer before"), "تعريف غير مكتمل: ربما تنقص فاصلة منقوطة، أو يوجد خطأ في كتابة اسم."),
        (new(@"^expected ',' or ';' before"), "تنقص فاصلة «,» أو فاصلة منقوطة «;» في هذا السطر."),
        (new(@"^a function-definition is not allowed here"), "لا يمكن تعريف دالة داخل دالة أخرى. غالبًا ينقص قوس إغلاق «}» قبل هذا السطر."),
        (new(@"^stray '"), "يوجد حرف غير مسموح به في الكود (ربما حرف عربي أو علامة تنصيص منسوخة من مستند). اكتب الأوامر بحروف إنجليزية، والنصوص العربية داخل علامتي تنصيص \"...\" فقط."),
        (new(@"^missing terminating "" character"), "نص غير مكتمل: تنقص علامة التنصيص «\"» في نهايته."),
        (new(@"^missing terminating ' character"), "حرف غير مكتمل: تنقص علامة «'» بعده."),
        (new(@"^redefinition of '([^']+)'"), "«{1}» معرّف أكثر من مرة."),
        (new(@"^conflicting declaration '([^']+)'"), "تعريف متعارض: «{1}» معرّف من قبل بنوع آخر."),
        (new(@"^too few arguments to function '([^']+)'"), "عدد القيم المُرسلة إلى الدالة «{1}» أقل من المطلوب."),
        (new(@"^too many arguments to function '([^']+)'"), "عدد القيم المُرسلة إلى الدالة «{1}» أكثر من المطلوب."),
        (new(@"^too few arguments"), "عدد القيم المُرسلة إلى الدالة أقل من المطلوب."),
        (new(@"^too many arguments"), "عدد القيم المُرسلة إلى الدالة أكثر من المطلوب."),
        (new(@"^no matching function for call to '([^'(]+)"), "لا توجد صيغة من «{1}» تقبل هذه القيم. راجع عدد القيم وأنواعها."),
        (new(@"^invalid conversion from '([^']+)' to '([^']+)'"), "لا يمكن تحويل قيمة من النوع «{1}» إلى النوع «{2}»."),
        (new(@"^cannot convert '([^']+)' to '([^']+)'"), "لا يمكن تحويل «{1}» إلى «{2}»."),
        (new(@"^lvalue required as left operand of assignment"), "الطرف الأيسر من «=» يجب أن يكون متغيرًا. هل تقصد المقارنة «==»؟"),
        (new(@"^suggest parentheses around assignment used as truth value"), "استُخدمت «=» داخل شرط. للمقارنة استخدم «==»."),
        (new(@"^'else' without a previous 'if'"), "«else» بدون «if» قبلها مباشرة. راجع الأقواس { } وعلامات «;» بعد الشرط."),
        (new(@"^break statement not within loop or switch"), "الأمر break يُستخدم داخل حلقة تكرار أو switch فقط."),
        (new(@"^return-statement with a value, in function returning 'void'"), "الدالة من النوع void لا تُرجع قيمة. احذف القيمة بعد return أو غيّر نوع الدالة."),
        (new(@"^control reaches end of non-void function"), "الدالة يجب أن تُرجع قيمة بالأمر return في نهايتها."),
        (new(@"^unused variable '([^']+)'"), "المتغير «{1}» معرّف لكنه غير مستخدم."),
        (new(@"^'([^']+)' is used uninitialized"), "المتغير «{1}» يُستخدم قبل إعطائه قيمة."),
        (new(@"^invalid operands of types"), "لا يمكن إجراء هذه العملية على هذين النوعين من القيم."),
        (new(@"^comparison between signed and unsigned"), "مقارنة بين عدد بإشارة وعدد بدون إشارة."),
        (new(@"^array subscript is above array bounds"), "رقم الخانة خارج حدود المصفوفة."),
        (new(@"^narrowing conversion"), "القيمة لا تتسع في هذا النوع."),
        (new(@"^integer overflow in expression"), "ناتج العملية أكبر من سعة النوع int (من ‎-32768 إلى 32767 في أونو)."),
        (new(@"^large integer implicitly truncated|overflow in conversion"), "القيمة أكبر من سعة النوع وستُقطع."),
    ];

    /// <summary>شرح عربي لرسالة المترجم (أو null لو مش معروفة).</summary>
    public static string? Explain(string msg, out bool known)
    {
        msg = Norm(msg.Trim());
        foreach (var (rx, ar) in Rules)
        {
            var m = rx.Match(msg);
            if (!m.Success) continue;
            known = true;
            string s = ar;
            for (int i = 1; i < m.Groups.Count; i++) s = s.Replace("{" + i + "}", m.Groups[i].Value.Trim());
            return s;
        }
        known = false;
        return null;
    }

    private static readonly Regex FlashRx = new(@"uses (\d+) bytes .*?Maximum is (\d+) bytes", RegexOptions.CultureInvariant);
    private static readonly Regex RamRx = new(@"Global variables use (\d+) bytes .*?Maximum is (\d+) bytes", RegexOptions.CultureInvariant);

    private static (int, int, int, int) Sizes(string stdout)
    {
        var f = FlashRx.Match(stdout);
        var r = RamRx.Match(stdout);
        static int I(Match m, int g) => m.Success ? int.Parse(m.Groups[g].Value) : 0;
        return (I(f, 1), I(f, 2), I(r, 1), I(r, 2));
    }
}
