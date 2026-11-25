using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>الإعدادات: المذاكرة (الهدف، التذكير، بومودورو) والذكاء الاصطناعي.</summary>
internal static class SettingsDialog
{
    public static bool Show(IWin32Window owner, AppSettings s)
    {
        using var kit = new FormKit("الإعدادات", 600);
        bool adv = s.AdvancedMode == true;

        kit.Header("شكل البرنامج");
        var advanced = kit.Row("الوضع المتقدم", new ToggleSwitch { Text = "اعرض كل الأدوات", Checked = adv });
        kit.Note("الوضع البسيط بيعرض الأساسي بس: المكتبة، والتشغيل، والعلامات، ويوتيوب، والبث المباشر.\n" +
                 "المتقدم بيضيف: الذكاء الاصطناعي، والإحصائيات، والمراجعة، ووضع التركيز، ومعمل الكود.", 40);

        kit.Header("المكتبة");
        var openLast = kit.Row("لما البرنامج يفتح", new ToggleSwitch { Text = "افتح آخر مكتبة على طول", Checked = s.OpenLastLibraryOnStart });
        kit.Note("لو مقفولة: البرنامج بيفتح فاضي، وتختار المكتبة اللي هتشتغل عليها من «مكتباتك» أو «ملف ← فتح مكتبة».", 24);

        kit.Header("المذاكرة");
        var goal = kit.Row("الهدف اليومي (دقيقة)", new NumberBox { Minimum = 5, Maximum = 720, Increment = 5, Value = Math.Clamp(s.DailyGoalMinutes, 5, 720) });
        var remind = kit.Row("تذكير يومي", new ToggleSwitch { Text = "فكّرني لو مخلصتش الهدف", Checked = s.RemindersEnabled });
        var time = kit.Row("الساعة", new TimeBox { Value = TimeOnly.TryParse(s.ReminderTime, out var t) ? t : new TimeOnly(21, 0) });
        remind.CheckedChanged += (_, _) => time.Enabled = remind.Checked;
        time.Enabled = remind.Checked;
        var readSeconds = kit.Row("الصفحة «اتقرت» بعد (ثانية)", new NumberBox { Minimum = 2, Maximum = 120, Value = Math.Clamp(s.ReadSeconds, 2, 120) });
        var pagesGoal = kit.Row("هدف القراية اليومي (صفحة، 0 = من غير)", new NumberBox { Minimum = 0, Maximum = 500, Increment = 5, Value = Math.Clamp(s.DailyPagesGoal, 0, 500) });

        NumberBox? work = null, rest = null, recap = null;
        ToggleSwitch? pause = null, silence = null;
        if (adv)
        {
            kit.Header("بومودورو");
            work = kit.Row("تركيز (دقيقة)", new NumberBox { Minimum = 5, Maximum = 120, Value = Math.Clamp(s.PomodoroWorkMinutes, 5, 120) });
            rest = kit.Row("بريك (دقيقة)", new NumberBox { Minimum = 1, Maximum = 60, Value = Math.Clamp(s.PomodoroBreakMinutes, 1, 60) });
            pause = kit.Row("وقت البريك", new ToggleSwitch { Text = "وقّف الفيديو تلقائيًا", Checked = s.PomodoroPausesVideo });

            kit.Header("التشغيل الذكي");
            silence = kit.Row("السكوت", new ToggleSwitch { Text = "اتخطى فترات السكوت تلقائيًا (Shift+S)", Checked = s.SkipSilence });
            recap = kit.Row("«اللي فات» بعد", new NumberBox { Minimum = 0, Maximum = 60, Value = Math.Clamp(s.RecapAfterDays, 0, 60) });
            kit.Note("عدد أيام الغياب عن الكورس قبل ما يفكّرك وقفت فين (0 = متفكرنيش).");
        }

        kit.Header("يوتيوب والإنترنت");
        var autoSync = kit.Row("تحديث تلقائي", new ToggleSwitch { Text = "هات الفيديوهات الجديدة في قوائم التشغيل لوحدك", Checked = s.AutoSyncPlaylists });
        var syncHours = kit.Row("كل (ساعة)", new NumberBox { Minimum = 1, Maximum = 168, Value = Math.Clamp(s.PlaylistSyncHours, 1, 168) });
        kit.Note("التحديث اليدوي: Ctrl+R، أو كليك يمين على المحاضرة ← تحديث من يوتيوب.");
        var browsers = new[] { ("", "من غير"), ("firefox", "Firefox"), ("chrome", "Chrome"), ("edge", "Edge"), ("brave", "Brave"), ("opera", "Opera") };
        var cookies = kit.Row("تسجيل الدخول من المتصفح", new SelectBox { RightToLeft = RightToLeft.No });
        foreach (var (_, label) in browsers) cookies.Items.Add(label);
        cookies.SelectedIndex = Math.Max(0, Array.FindIndex(browsers, b => string.Equals(b.Item1, s.OnlineCookiesBrowser, StringComparison.OrdinalIgnoreCase)));
        kit.Note("لو يوتيوب قال «Sign in to confirm you're not a bot» أو الفيديو محتاج تسجيل دخول: اختار المتصفح اللي إنت مسجّل فيه دخول يوتيوب، " +
                 "والبرنامج هيستخدم تسجيل دخولك. Firefox الأضمن؛ Chrome وEdge ساعات بيمنعوا ده، فلو مااشتغلش اقفل المتصفح وجرّب تاني.", 54);

        kit.Header("دروس المنصات (يوديمي، IT Legend…)");
        var quality = kit.Row("الجودة", new SelectBox());
        foreach (var (_, label) in WebQuality.Caps) quality.Items.Add(label);
        quality.SelectedIndex = WebQuality.CapIndex(s.WebQualityCap);
        kit.Note("«مسار» بيختار الجودة من قايمة ⚙ في مشغّل الموقع نفسه بعد ما الدرس يشتغل كام ثانية — مرة لكل درس، ولو غيّرتها بإيدك مش هيرجّعها. " +
                 "مايقدرش يعدّي أعلى جودة اترفع بيها الكورس. الشارة جنب السرعة بتوريك الدقة الحقيقية للفيديو.", 54);

        kit.Header("نوشن");
        var notionAuto = kit.Row("التحديث التلقائي", new ToggleSwitch { Text = "حدّث نوشن لوحده (التقدّم كل 3 دقايق، والخطة كل 10)", Checked = s.NotionAutoSync });
        kit.Note(string.IsNullOrWhiteSpace(s.NotionToken)
            ? "مش متربط لسه: كليك يمين على أي موضوع ← 📤 نوشن ← أنشئ قاعدة نوشن، وهيطلب منك المفتاح والصفحة."
            : "متربط ✓ — المفتاح متشفّر على الجهاز ده. لتغيير المفتاح أو الصفحة: كليك يمين على موضوع ← 📤 نوشن ← إعدادات نوشن.", 40);

        kit.Header("التحديثات");
        var updates = kit.Row("تحديث تلقائي", new ToggleSwitch { Text = "اعرض عليّ الإصدارات الجديدة أول ما تنزل", Checked = s.CheckUpdates });
        kit.Note($"الإصدار الحالي: {UpdateChecker.CurrentVersion.ToString(3)}.  تقدر تتحقق بنفسك من: مساعدة ← التحقق من التحديثات.");

        TextBox? key = null;
        SelectBox? model = null, whisper = null, lang = null;
        NumberBox? cap = null;
        var langs = new[] { ("auto", "تلقائي"), ("ar", "عربي"), ("en", "إنجليزي") };
        if (adv)
        {
            kit.Header("الذكاء الاصطناعي (Claude)");
            key = kit.Row("مفتاح Claude", new TextBox { Text = s.ClaudeApiKey, UseSystemPasswordChar = true, RightToLeft = RightToLeft.No });
            kit.Note("بتعمله من console.anthropic.com — بيتحفظ متشفّر على الجهاز ده بس، ومش بيتكتب في ملف المكتبة.");
            model = kit.Row("الموديل", new SelectBox { Editable = true, RightToLeft = RightToLeft.No });
            model.Items.AddRange(ClaudeClient.KnownModels);
            model.Text = s.AiModel;
            cap = kit.Row("حد الاستخدام في الشهر", new NumberBox { Minimum = 0, Maximum = 100_000_000, Increment = 100_000, ThousandsSeparator = true, Value = Math.Clamp(s.AiMonthlyTokenCap, 0, 100_000_000) });
            kit.Note($"بوحدات Claude (الكلمة تقريبًا وحدة ونص). 0 = من غير حد.  استهلاك الشهر ده: {AiBudget.UsedThisMonth(s, DateTime.UtcNow):N0}.");

            kit.Header("تحويل كلام الفيديو لنص");
            whisper = kit.Row("الدقة", new SelectBox());
            foreach (var (name, label) in Tools.WhisperModels) whisper.Items.Add(label);
            whisper.SelectedIndex = Math.Max(0, Array.FindIndex(Tools.WhisperModels, m => m.Name == s.WhisperModel));
            kit.Note("أدق = أبطأ وحجمه أكبر. التحويل بيحصل على جهازك ومن غير نت.");
            lang = kit.Row("لغة الفيديوهات", new SelectBox());
            foreach (var (_, label) in langs) lang.Items.Add(label);
            lang.SelectedIndex = Math.Max(0, Array.FindIndex(langs, l => l.Item1 == s.TranscriptLanguage));
        }

        kit.Buttons();
        if (kit.Show(owner) != DialogResult.OK) return false;

        s.AdvancedMode = advanced.Checked;
        s.DailyGoalMinutes = (int)goal.Value;
        s.ReadSeconds = (int)readSeconds.Value;
        s.DailyPagesGoal = (int)pagesGoal.Value;
        s.RemindersEnabled = remind.Checked;
        s.ReminderTime = time.Value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (work != null) s.PomodoroWorkMinutes = (int)work.Value;
        if (rest != null) s.PomodoroBreakMinutes = (int)rest.Value;
        if (pause != null) s.PomodoroPausesVideo = pause.Checked;
        if (silence != null) s.SkipSilence = silence.Checked;
        if (recap != null) s.RecapAfterDays = (int)recap.Value;
        s.AutoSyncPlaylists = autoSync.Checked;
        s.PlaylistSyncHours = (int)syncHours.Value;
        s.CheckUpdates = updates.Checked;
        s.NotionAutoSync = notionAuto.Checked;
        s.WebQualityCap = WebQuality.Caps[Math.Max(0, quality.SelectedIndex)].Cap;
        s.OpenLastLibraryOnStart = openLast.Checked;
        s.OnlineCookiesBrowser = browsers[Math.Max(0, cookies.SelectedIndex)].Item1;
        if (key != null) s.ClaudeApiKey = key.Text;
        if (model != null) s.AiModel = string.IsNullOrWhiteSpace(model.Text) ? ClaudeClient.DefaultModel : model.Text.Trim();
        if (cap != null) s.AiMonthlyTokenCap = (long)cap.Value;
        if (whisper != null) s.WhisperModel = Tools.WhisperModels[Math.Max(0, whisper.SelectedIndex)].Name;
        if (lang != null) s.TranscriptLanguage = langs[Math.Max(0, lang.SelectedIndex)].Item1;
        return true;
    }
}
