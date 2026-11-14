using LecturePlayer.Data;

namespace LecturePlayer.UI;

/// <summary>الإعدادات: المذاكرة (الهدف، التذكير، بومودورو) والذكاء الاصطناعي.</summary>
internal static class SettingsDialog
{
    public static bool Show(IWin32Window owner, AppSettings s)
    {
        using var kit = new FormKit("الإعدادات", 600);

        kit.Header("المذاكرة");
        var goal = kit.Row("الهدف اليومي (دقيقة)", new NumberBox { Minimum = 5, Maximum = 720, Increment = 5, Value = Math.Clamp(s.DailyGoalMinutes, 5, 720) });
        var remind = kit.Row("تذكير يومي", new ToggleSwitch { Text = "فكّرني لو مخلصتش الهدف", Checked = s.RemindersEnabled });
        var time = kit.Row("الساعة", new TimeBox { Value = TimeOnly.TryParse(s.ReminderTime, out var t) ? t : new TimeOnly(21, 0) });
        remind.CheckedChanged += (_, _) => time.Enabled = remind.Checked;
        time.Enabled = remind.Checked;

        kit.Header("بومودورو");
        var work = kit.Row("تركيز (دقيقة)", new NumberBox { Minimum = 5, Maximum = 120, Value = Math.Clamp(s.PomodoroWorkMinutes, 5, 120) });
        var rest = kit.Row("بريك (دقيقة)", new NumberBox { Minimum = 1, Maximum = 60, Value = Math.Clamp(s.PomodoroBreakMinutes, 1, 60) });
        var pause = kit.Row("وقت البريك", new ToggleSwitch { Text = "وقّف الفيديو تلقائيًا", Checked = s.PomodoroPausesVideo });

        kit.Header("التشغيل الذكي");
        var silence = kit.Row("السكوت", new ToggleSwitch { Text = "اتخطى فترات السكوت تلقائيًا (Shift+S)", Checked = s.SkipSilence });
        var recap = kit.Row("«اللي فات» بعد", new NumberBox { Minimum = 0, Maximum = 60, Value = Math.Clamp(s.RecapAfterDays, 0, 60) });
        kit.Note("عدد أيام الغياب عن الكورس قبل ما يفكّرك وقفت فين (0 = متفكرنيش).");

        kit.Header("يوتيوب والإنترنت");
        var autoSync = kit.Row("تحديث تلقائي", new ToggleSwitch { Text = "هات الفيديوهات الجديدة لوحدك", Checked = s.AutoSyncPlaylists });
        var syncHours = kit.Row("كل (ساعة)", new NumberBox { Minimum = 1, Maximum = 168, Value = Math.Clamp(s.PlaylistSyncHours, 1, 168) });
        kit.Note("التحديث اليدوي: Ctrl+R، أو كليك يمين على المحاضرة ← تحديث من يوتيوب.");
        var browsers = new[] { ("", "من غير"), ("firefox", "Firefox"), ("chrome", "Chrome"), ("edge", "Edge"), ("brave", "Brave"), ("opera", "Opera") };
        var cookies = kit.Row("كوكيز المتصفح", new SelectBox { RightToLeft = RightToLeft.No });
        foreach (var (_, label) in browsers) cookies.Items.Add(label);
        cookies.SelectedIndex = Math.Max(0, Array.FindIndex(browsers, b => string.Equals(b.Item1, s.OnlineCookiesBrowser, StringComparison.OrdinalIgnoreCase)));
        kit.Note("لو يوتيوب قال «Sign in to confirm you're not a bot» أو الفيديو محتاج تسجيل دخول: اختار المتصفح اللي فاتح بيه يوتيوب. " +
                 "Firefox الأضمن؛ Chrome وEdge الجداد ساعات بيمنعوا قراية الكوكيز، فلو مااشتغلش اقفل المتصفح وجرّب تاني.");

        kit.Header("التحديثات");
        var updates = kit.Row("تحديث تلقائي", new ToggleSwitch { Text = "اعرض عليّ الإصدارات الجديدة أول ما تنزل", Checked = s.CheckUpdates });
        kit.Note($"الإصدار الحالي: {UpdateChecker.CurrentVersion.ToString(3)}.  تقدر تتحقق بنفسك من: مساعدة ← التحقق من التحديثات.");

        kit.Header("الذكاء الاصطناعي (Claude)");
        var key = kit.Row("مفتاح API", new TextBox { Text = s.ClaudeApiKey, UseSystemPasswordChar = true, RightToLeft = RightToLeft.No });
        kit.Note("من console.anthropic.com — بيتحفظ متشفّر على الجهاز ده بس، ومش بيتكتب في ملف القائمة.");
        var model = kit.Row("الموديل", new SelectBox { Editable = true, RightToLeft = RightToLeft.No });
        model.Items.AddRange(ClaudeClient.KnownModels);
        model.Text = s.AiModel;
        var cap = kit.Row("حد التوكنز في الشهر", new NumberBox { Minimum = 0, Maximum = 100_000_000, Increment = 100_000, ThousandsSeparator = true, Value = Math.Clamp(s.AiMonthlyTokenCap, 0, 100_000_000) });
        kit.Note($"0 = من غير حد.  استهلاك الشهر ده: {AiBudget.UsedThisMonth(s, DateTime.UtcNow):N0} توكن.");

        kit.Header("تفريغ الفيديوهات");
        var whisper = kit.Row("موديل whisper", new SelectBox());
        foreach (var (name, label) in Tools.WhisperModels) whisper.Items.Add(label);
        whisper.SelectedIndex = Math.Max(0, Array.FindIndex(Tools.WhisperModels, m => m.Name == s.WhisperModel));
        var lang = kit.Row("لغة الفيديوهات", new SelectBox());
        var langs = new[] { ("auto", "تلقائي"), ("ar", "عربي"), ("en", "إنجليزي") };
        foreach (var (_, label) in langs) lang.Items.Add(label);
        lang.SelectedIndex = Math.Max(0, Array.FindIndex(langs, l => l.Item1 == s.TranscriptLanguage));

        Action apply = () =>
        {
            s.SkipSilence = silence.Checked;
            s.AutoSyncPlaylists = autoSync.Checked;
            s.CheckUpdates = updates.Checked;
            s.OnlineCookiesBrowser = browsers[Math.Max(0, cookies.SelectedIndex)].Item1;
            s.PlaylistSyncHours = (int)syncHours.Value;
            s.RecapAfterDays = (int)recap.Value;
            s.ClaudeApiKey = key.Text;
            s.AiModel = string.IsNullOrWhiteSpace(model.Text) ? ClaudeClient.DefaultModel : model.Text.Trim();
            s.AiMonthlyTokenCap = (long)cap.Value;
            s.WhisperModel = Tools.WhisperModels[Math.Max(0, whisper.SelectedIndex)].Name;
            s.TranscriptLanguage = langs[Math.Max(0, lang.SelectedIndex)].Item1;
        };

        kit.Buttons();
        if (kit.Show(owner) != DialogResult.OK) return false;

        s.DailyGoalMinutes = (int)goal.Value;
        s.RemindersEnabled = remind.Checked;
        s.ReminderTime = time.Value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        s.PomodoroWorkMinutes = (int)work.Value;
        s.PomodoroBreakMinutes = (int)rest.Value;
        s.PomodoroPausesVideo = pause.Checked;
        apply();
        return true;
    }
}
