using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// مساعدة المستخدم الجديد: الوضع البسيط/المتقدم، والجولة التعريفية.
/// </summary>
internal sealed partial class MainForm
{
    // =====================================================================
    //  الوضع البسيط والمتقدم
    // =====================================================================

    private readonly List<GlyphButton> _advancedActivity = new();
    private ToolStripMenuItem _advancedItem = null!;

    private bool AdvancedMode => _settings.AdvancedMode == true;

    /// <summary>بيعلّم عنصر في المنيو إنه من أدوات الوضع المتقدم (بيستخبى في البسيط).</summary>
    private static T Adv<T>(T item) where T : ToolStripItem
    {
        item.Tag = AdvTag;
        return item;
    }
    private const string AdvTag = "adv";

    /// <summary>أول تشغيل بعد الميزة دي: المستخدم الجديد بيبدأ بسيط، واللي كان بيستخدم البرنامج قبل كده بيفضل متقدم.</summary>
    private void InitAdvancedMode()
    {
        if (_settings.AdvancedMode == null)
        {
            bool existingUser = !string.IsNullOrEmpty(_settings.LastRunVersion);
            _settings.AdvancedMode = existingUser;
            if (existingUser) _settings.TourShown = true; // مش محتاج جولة
            try { Storage.SaveSettings(_settings); } catch { }
        }
        ApplyMode();
    }

    private void SetAdvancedMode(bool on)
    {
        _settings.AdvancedMode = on;
        ApplyMode();
        SaveSettings();
        ShowBanner(on
                ? "اتفتح الوضع المتقدم: كل الأدوات بقت ظاهرة (الذكاء الاصطناعي، الإحصائيات، المراجعة، التركيز، معمل الكود)"
                : "رجعت للوضع البسيط: الأدوات المتقدمة اتخبت (تقدر ترجّعها من مساعدة ← الوضع المتقدم)",
            BannerKind.Info, seconds: 8);
    }

    private void ApplyMode()
    {
        bool adv = AdvancedMode;
        foreach (ToolStripItem top in _menu.Items) ApplyMode(top, adv);
        foreach (var b in _advancedActivity) b.Visible = adv;
        if (_advancedItem != null) _advancedItem.Checked = adv;
    }

    private static void ApplyMode(ToolStripItem item, bool adv)
    {
        // Available مش Visible: Visible بيرجع false لأي عنصر في منيو مقفولة.
        if (Equals(item.Tag, AdvTag)) item.Available = adv;
        if (item is ToolStripDropDownItem dd)
        {
            foreach (ToolStripItem child in dd.DropDownItems) ApplyMode(child, adv);
            TidySeparators(dd.DropDownItems);
        }
    }

    /// <summary>بعد إخفاء عناصر: مفيش فاصل في الأول ولا في الآخر ولا فاصلين ورا بعض.</summary>
    private static void TidySeparators(ToolStripItemCollection items)
    {
        ToolStripItem? lastShown = null;
        foreach (ToolStripItem i in items)
        {
            if (i is ToolStripSeparator sep)
            {
                sep.Available = lastShown != null && lastShown is not ToolStripSeparator;
                if (sep.Available) lastShown = sep;
                continue;
            }
            if (i.Available) lastShown = i;
        }
        if (lastShown is ToolStripSeparator tail) tail.Available = false;
    }

    // =====================================================================
    //  الجولة التعريفية
    // =====================================================================

    private void StartTourOrGuide() => StartTour();

    /// <summary>الجولة السريعة (3 خطوات). بتظهر لوحدها أول مرة، وموجودة في منيو مساعدة.</summary>
    private void StartTour()
    {
        var choice = TourForm.Run(this);
        if (!_settings.TourShown) { _settings.TourShown = true; SaveSettings(); }
        switch (choice)
        {
            case TourChoice.AddFolder: ImportCourseDialog(); break;
            case TourChoice.AddUrl: AddUrlsDialog(null); break;
            case TourChoice.Live: ShowLiveWindow(); break;
            default:
                ShowBanner("لو احتجت أي حاجة: F1 لدليل الاستخدام، أو مساعدة ← جولة سريعة", BannerKind.Info, seconds: 10);
                break;
        }
    }

    /// <summary>أول تشغيل للبرنامج خالص: نعرض الجولة (إلا لو اتفتح بملف فيديو، ساعتها منقاطعش).</summary>
    private bool ShouldShowFirstRunTour(bool openedWithFiles) => !_settings.TourShown && !openedWithFiles;
}
