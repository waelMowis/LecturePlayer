using System.Text.Json;

namespace LecturePlayer.UI;

internal sealed partial class ReaderPane
{
    private static readonly JsonSerializerOptions ArduJson = new(JsonSerializerDefaults.Web);

    /// <summary>محاكي أردوينو: الصفحة بتبعت الكود ← نترجمه من غير نت ← النتيجة (hex أو الأخطاء) ترجع لـ masar.arduResult.</summary>
    private async Task CompileArduinoAsync(string id, string code)
    {
        Data.Arduino.Result r;
        try { r = await Task.Run(() => Data.Arduino.CompileAsync(code)); }
        catch (Exception ex) { r = new(false, null, new(), 0, 0, 0, 0, ex.Message, 0); }
        Data.Log.Info($"arduino: compile {(r.Ok ? "ok" : "failed")} in {r.Ms} ms ({r.Problems.Count} problems, {r.Flash} bytes)");
        await EvalAsync($"masar.arduResult({JsonSerializer.Serialize(id)}, {JsonSerializer.Serialize(r, ArduJson)})");
    }
}
