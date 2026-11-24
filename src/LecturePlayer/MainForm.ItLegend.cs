using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>IT Legend (itlegend.net) — الخطوة الأولى: الدخول جوه «مسار» وتسجيل شكل بيانات المنهج عشان الاستيراد يتبني عليها.</summary>
internal sealed partial class MainForm
{
    private void ItLegendCapture()
    {
        if (UdemyWeb.RuntimeVersion == null) { ShowWebView2Missing(); return; }
        using var form = new PlatformCaptureForm("IT Legend — ادخل وافتح كورس", "https://itlegend.net/ar/login", "itlegend-capture.txt");
        form.ShowDialog(this);
        if (form.Captured > 0)
            ShowBanner($"✓ اتسجّل شكل بيانات IT Legend ({form.Captured} نوع) — قول لـ Claude «سجّلت» عشان يكمّل الاستيراد", BannerKind.Success, seconds: 15);
        else ShowStatus("مااتسجّلش حاجة — لازم تدخل وتفتح كورس قبل ما تقفل الشباك", 6);
    }
}
