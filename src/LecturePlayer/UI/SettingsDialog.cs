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
        var role = kit.Row("أستخدم البرنامج", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
        role.Items.AddRange(new object[] { "طالبًا", "معلمًا", "طالبًا ومعلمًا" });
        role.SelectedIndex = s.Role switch { "teacher" => 1, "both" => 2, _ => 0 };
        kit.Note("يغيّر الشريط الجانبي وأعلى الرئيسية: للمعلم «الحصص» وحصص اليوم ومجموعاته، وللطالب «جلساتي» والمراجعة والإحصائيات.", 40);
        var advanced = kit.Row("الوضع المتقدم", new ToggleSwitch { Text = "اعرض شريط الحالة", Checked = adv });
        kit.Note("شريط الحالة في الأسفل: رسائل البرنامج، واسم ملف المكتبة، ومذاكرة اليوم والسلسلة. والأوامر كلها موجودة في القوائم في الحالتين.", 40);

        kit.Header("المكتبة");
        var openLast = kit.Row("عند فتح البرنامج", new ToggleSwitch { Text = "افتح آخر مكتبة مباشرة", Checked = s.OpenLastLibraryOnStart });
        kit.Note("إن كانت معطّلة: يُفتح البرنامج فارغًا، وتختار المكتبة التي ستعمل عليها من «مكتباتك» أو «ملف ← فتح مكتبة».", 24);

        kit.Header("المذاكرة");
        var goal = kit.Row("الهدف اليومي (دقيقة)", new NumberBox { Minimum = 5, Maximum = 720, Increment = 5, Value = Math.Clamp(s.DailyGoalMinutes, 5, 720) });
        var remind = kit.Row("تذكير يومي", new ToggleSwitch { Text = "ذكّرني إن لم أُكمل الهدف", Checked = s.RemindersEnabled });
        var time = kit.Row("الساعة", new TimeBox { Value = TimeOnly.TryParse(s.ReminderTime, out var t) ? t : new TimeOnly(21, 0) });
        remind.CheckedChanged += (_, _) => time.Enabled = remind.Checked;
        time.Enabled = remind.Checked;
        var readSeconds = kit.Row("تُعدّ الصفحة «مقروءة» بعد (ثانية)", new NumberBox { Minimum = 2, Maximum = 120, Value = Math.Clamp(s.ReadSeconds, 2, 120) });
        var pagesGoal = kit.Row("هدف القراءة اليومي (صفحة، 0 = بدون)", new NumberBox { Minimum = 0, Maximum = 500, Increment = 5, Value = Math.Clamp(s.DailyPagesGoal, 0, 500) });

        NumberBox? work = null, rest = null, recap = null;
        ToggleSwitch? pause = null, silence = null;
        if (adv)
        {
            kit.Header("بومودورو");
            work = kit.Row("تركيز (دقيقة)", new NumberBox { Minimum = 5, Maximum = 120, Value = Math.Clamp(s.PomodoroWorkMinutes, 5, 120) });
            rest = kit.Row("الاستراحة (دقيقة)", new NumberBox { Minimum = 1, Maximum = 60, Value = Math.Clamp(s.PomodoroBreakMinutes, 1, 60) });
            pause = kit.Row("وقت الاستراحة", new ToggleSwitch { Text = "أوقف الفيديو تلقائيًا", Checked = s.PomodoroPausesVideo });

            kit.Header("التشغيل الذكي");
            silence = kit.Row("الصمت", new ToggleSwitch { Text = "تخطَّ فترات الصمت تلقائيًا (Shift+S)", Checked = s.SkipSilence });
            recap = kit.Row("«ما فاتك» بعد", new NumberBox { Minimum = 0, Maximum = 60, Value = Math.Clamp(s.RecapAfterDays, 0, 60) });
            kit.Note("عدد أيام الغياب عن الدورة قبل أن يذكّرك أين توقفت (0 = لا تذكير).");
        }

        kit.Header("يوتيوب والإنترنت");
        var autoSync = kit.Row("تحديث تلقائي", new ToggleSwitch { Text = "اجلب الفيديوهات الجديدة في قوائم التشغيل تلقائيًا", Checked = s.AutoSyncPlaylists });
        var syncHours = kit.Row("كل (ساعة)", new NumberBox { Minimum = 1, Maximum = 168, Value = Math.Clamp(s.PlaylistSyncHours, 1, 168) });
        kit.Note("التحديث اليدوي: Ctrl+R، أو انقر بالزر الأيمن على المحاضرة ← تحديث من يوتيوب.");
        var browsers = new[] { ("", "بدون"), ("firefox", "Firefox"), ("chrome", "Chrome"), ("edge", "Edge"), ("brave", "Brave"), ("opera", "Opera") };
        var cookies = kit.Row("تسجيل الدخول من المتصفح", new SelectBox { RightToLeft = RightToLeft.No });
        foreach (var (_, label) in browsers) cookies.Items.Add(label);
        cookies.SelectedIndex = Math.Max(0, Array.FindIndex(browsers, b => string.Equals(b.Item1, s.OnlineCookiesBrowser, StringComparison.OrdinalIgnoreCase)));
        kit.Note("إن قال يوتيوب «Sign in to confirm you're not a bot» أو احتاج الفيديو تسجيل دخول: اختر المتصفح الذي سجّلت فيه الدخول إلى يوتيوب، " +
                 "وسيستخدم البرنامج تسجيل دخولك. Firefox الأضمن؛ Chrome وEdge يمنعان ذلك أحيانًا، فإن لم ينجح أغلق المتصفح وحاول مرة أخرى.", 54);

        kit.Header("دروس المنصات (يوديمي، IT Legend…)");
        var quality = kit.Row("الجودة", new SelectBox());
        foreach (var (_, label) in WebQuality.Caps) quality.Items.Add(label);
        quality.SelectedIndex = WebQuality.CapIndex(s.WebQualityCap);
        kit.Note("يختار «مسار» الجودة من قائمة ⚙ في مشغّل الموقع نفسه بعد أن يعمل الدرس بضع ثوانٍ — مرة لكل درس، وإن غيّرتها يدويًا فلن يعيدها. " +
                 "لا يمكنه تجاوز أعلى جودة رُفعت بها الدورة. الشارة بجانب السرعة تُظهر الدقة الحقيقية للفيديو.", 54);

        kit.Header("نوشن");
        var notionAuto = kit.Row("التحديث التلقائي", new ToggleSwitch { Text = "حدّث نوشن تلقائيًا (التقدّم كل 3 دقائق، والخطة كل 10)", Checked = s.NotionAutoSync });
        kit.Note(string.IsNullOrWhiteSpace(s.NotionToken)
            ? "غير مرتبط بعد: انقر بالزر الأيمن على أي موضوع ← 📤 نوشن ← أنشئ قاعدة نوشن، وسيطلب منك المفتاح والصفحة."
            : "مرتبط ✓ — المفتاح مشفّر على هذا الجهاز. لتغيير المفتاح أو الصفحة: انقر بالزر الأيمن على موضوع ← 📤 نوشن ← إعدادات نوشن.", 40);

        kit.Header("التحديثات");
        var updates = kit.Row("تحديث تلقائي", new ToggleSwitch { Text = "اعرض عليّ الإصدارات الجديدة فور صدورها", Checked = s.CheckUpdates });
        kit.Note($"الإصدار الحالي: {UpdateChecker.CurrentVersion.ToString(3)}.  يمكنك التحقق بنفسك من: مساعدة ← التحقق من التحديثات.");

        TextBox? key = null;
        SelectBox? model = null, whisper = null, lang = null;
        NumberBox? cap = null;
        var langs = new[] { ("auto", "تلقائي"), ("ar", "عربي"), ("en", "الإنجليزية") };
        if (adv)
        {
            kit.Header("الذكاء الاصطناعي (Claude)");
            key = kit.Row("مفتاح Claude", new TextBox { Text = s.ClaudeApiKey, UseSystemPasswordChar = true, RightToLeft = RightToLeft.No });
            kit.Note("تنشئه من console.anthropic.com — يُحفظ مشفّرًا على هذا الجهاز فقط، ولا يُكتب في ملف المكتبة.");
            model = kit.Row("النموذج", new SelectBox { Editable = true, RightToLeft = RightToLeft.No });
            model.Items.AddRange(ClaudeClient.KnownModels);
            model.Text = s.AiModel;
            cap = kit.Row("حد الاستخدام في الشهر", new NumberBox { Minimum = 0, Maximum = 100_000_000, Increment = 100_000, ThousandsSeparator = true, Value = Math.Clamp(s.AiMonthlyTokenCap, 0, 100_000_000) });
            kit.Note($"بوحدات Claude (الكلمة نحو وحدة ونصف). 0 = بلا حد.  استهلاك هذا الشهر: {AiBudget.UsedThisMonth(s, DateTime.UtcNow):N0}.");

            kit.Header("تحويل كلام الفيديو إلى نص");
            whisper = kit.Row("الدقة", new SelectBox());
            foreach (var (name, label) in Tools.WhisperModels) whisper.Items.Add(label);
            whisper.SelectedIndex = Math.Max(0, Array.FindIndex(Tools.WhisperModels, m => m.Name == s.WhisperModel));
            kit.Note("أدق = أبطأ وحجمه أكبر. يتم التحويل على جهازك ودون إنترنت.");
            lang = kit.Row("لغة الفيديوهات", new SelectBox());
            foreach (var (_, label) in langs) lang.Items.Add(label);
            lang.SelectedIndex = Math.Max(0, Array.FindIndex(langs, l => l.Item1 == s.TranscriptLanguage));
        }

        kit.Buttons();
        if (kit.Show(owner) != DialogResult.OK) return false;

        s.AdvancedMode = advanced.Checked;
        s.Role = role.SelectedIndex switch { 1 => "teacher", 2 => "both", _ => "student" };
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
