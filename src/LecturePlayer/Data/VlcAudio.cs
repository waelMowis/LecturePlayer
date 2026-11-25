using LibVLCSharp.Shared;

namespace LecturePlayer.Data;

/// <summary>
/// استخراج صوت أي فيديو لملف WAV (16 كيلو، قناة واحدة — اللي أداة تحويل الكلام لنص محتاجاه) بالمشغل نفسه (VLC)
/// اللي جاي مع البرنامج، من غير ffmpeg.
/// </summary>
public static class VlcAudio
{
    public static async Task ExtractWavAsync(string input, string outWav, CancellationToken ct)
    {
        if (File.Exists(outWav)) File.Delete(outWav);
        using var lib = new LibVLC("--quiet", "--no-video", "--no-sub-autodetect-file");
        string dst = outWav.Replace("\\", "/");
        using var media = new Media(lib, input, FromType.FromPath,
            ":no-video", ":no-sout-video", ":sout-keep",
            ":sout=#transcode{vcodec=none,acodec=s16l,channels=1,samplerate=16000}:std{access=file,mux=wav,dst=\"" + dst + "\"}");
        using var mp = new MediaPlayer(media);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        mp.EndReached += (_, _) => done.TrySetResult(true);
        mp.EncounteredError += (_, _) => done.TrySetResult(false);
        mp.Stopped += (_, _) => done.TrySetResult(true);
        using var reg = ct.Register(() => done.TrySetCanceled(ct));
        if (!mp.Play()) throw new InvalidOperationException("لم يستطع المشغل فتح الفيديو لاستخراج صوته");
        bool ok;
        try { ok = await done.Task; }
        finally
        {
            // Stop لازم يتنده بره ثريد أحداث VLC
            await Task.Run(() => { try { mp.Stop(); } catch { } });
        }
        if (!ok) throw new InvalidOperationException("لم يستطع المشغل قراءة صوت الفيديو");
        var info = new FileInfo(outWav);
        if (!info.Exists || info.Length < 1024) throw new InvalidOperationException("صوت الفيديو فارغ");
    }
}
