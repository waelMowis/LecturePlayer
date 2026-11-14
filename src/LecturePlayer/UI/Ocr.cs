namespace LecturePlayer.UI;

/// <summary>
/// قراية النص من صورة (OCR) بمحرك الويندوز نفسه (Windows.Media.Ocr) — من غير نت ومن غير أدوات إضافية.
/// اللغات المتاحة = لغات الويندوز المتثبتة (الإنجليزي موجود دايمًا تقريبًا).
/// </summary>
internal static class Ocr
{
#if WINDOWS10_0_17763_0_OR_GREATER
    public static bool IsSupported => true;

    /// <summary>أكبر طول/عرض للصورة يقبله المحرك.</summary>
    public static int MaxDimension => (int)Windows.Media.Ocr.OcrEngine.MaxImageDimension;

    public static async Task<string> RecognizeAsync(string imagePath, string? preferredLanguage = "en-US")
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(imagePath));
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

        Windows.Media.Ocr.OcrEngine? engine = null;
        if (!string.IsNullOrEmpty(preferredLanguage))
        {
            var lang = new Windows.Globalization.Language(preferredLanguage);
            if (Windows.Media.Ocr.OcrEngine.IsLanguageSupported(lang)) engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(lang);
        }
        engine ??= Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null) throw new InvalidOperationException("مفيش لغة OCR متثبتة في الويندوز.");

        if (bitmap.PixelWidth > Windows.Media.Ocr.OcrEngine.MaxImageDimension || bitmap.PixelHeight > Windows.Media.Ocr.OcrEngine.MaxImageDimension)
            throw new InvalidOperationException("الصورة كبيرة على محرك OCR.");

        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
    }
#else
    public static bool IsSupported => false;
    public static int MaxDimension => 2600;

    public static Task<string> RecognizeAsync(string imagePath, string? preferredLanguage = "en-US") =>
        throw new PlatformNotSupportedException("OCR محتاج Windows 10 أو أحدث.");
#endif
}
