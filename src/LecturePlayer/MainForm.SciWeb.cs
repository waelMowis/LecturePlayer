using System.Text.Json;
using LecturePlayer.UI;

namespace LecturePlayer;

internal partial class MainForm
{
    /// <summary>السبورة طلبت مصدر أونلاين (PhET / GeoGebra / Desmos): نافذة جوه «مسار»، واللقطة بترجع للصفحة المفتوحة.</summary>
    private void OnSciWebRequested(string url, string title, string source)
    {
        var f = SciWebForm.Open(this, url, title, source);
        f.Captured -= OnSciCaptured;
        f.Captured += OnSciCaptured;
    }

    private async void OnSciCaptured(byte[] jpeg, string url, string title, string source)
    {
        try
        {
            if (_reader == null || IsDisposed) return;
            string json = JsonSerializer.Serialize(new { img = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg), url, title, source });
            await _reader.EvalAsync("masar.sciCaptured(" + json + ")");
        }
        catch (Exception ex) { Data.Log.Error("sci capture", ex); }
    }
}
