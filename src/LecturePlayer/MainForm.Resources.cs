using System.Diagnostics;
using LecturePlayer.Data;
using LibVLCSharp.Shared;

namespace LecturePlayer;

/// <summary>
/// مراقبة استهلاك البرنامج على جهاز المستخدم: كل 10 ثواني عينة، وكل 5 دقايق سطر في السجل
/// (المعالج المتوسط والأعلى، والذاكرة، وكان بيعمل إيه)، ولو المعالج فضل عالي 30 ثانية بيتسجل ومعاه السبب المحتمل.
/// كده نعرف من السجل الحقيقي إيه اللي بيستهلك، بدل التخمين.
/// </summary>
internal sealed partial class MainForm
{
    private readonly Process _self = Process.GetCurrentProcess();
    private TimeSpan _cpuLast;
    private DateTime _cpuLastAt = DateTime.MinValue;
    private readonly List<double> _cpuWindow = new();     // عينات آخر 5 دقايق
    private readonly Queue<double> _cpuRecent = new();    // آخر 3 عينات (30 ثانية)
    private DateTime _lastHighLog = DateTime.MinValue;
    private DateTime _windowStartUtc = DateTime.UtcNow;

    /// <summary>بيتنده من OnUiTick كل 10 ثواني.</summary>
    private void SampleResources()
    {
        try
        {
            _self.Refresh();
            var now = DateTime.UtcNow;
            var cpu = _self.TotalProcessorTime;
            if (_cpuLastAt != DateTime.MinValue)
            {
                double pct = (cpu - _cpuLast).TotalMilliseconds / (now - _cpuLastAt).TotalMilliseconds * 100; // % من نواة واحدة
                _cpuWindow.Add(pct);
                _cpuRecent.Enqueue(pct);
                while (_cpuRecent.Count > 3) _cpuRecent.Dequeue();

                if (_cpuRecent.Count == 3 && _cpuRecent.Min() > 50 && now - _lastHighLog > TimeSpan.FromMinutes(5))
                {
                    _lastHighLog = now;
                    Log.Info($"resources HIGH CPU for 30s: {string.Join(" / ", _cpuRecent.Select(x => x.ToString("F0") + "%"))} of one core — {ResourceContext()}");
                }
                if (_cpuWindow.Count >= 30) // 5 دقايق
                {
                    double minutes = (now - _windowStartUtc).TotalMinutes;
                    string wakeups = minutes > 0 ? $"ui {_uiTicksWindow / minutes:F0}/min" : "";
                    _uiTicksWindow = 0;
                    _windowStartUtc = now;
                    Log.Info($"resources (5 min): cpu avg {_cpuWindow.Average():F1}% peak {_cpuWindow.Max():F0}% of one core ({Environment.ProcessorCount} cores), " +
                             $"{(OnBattery ? "battery" : "ac")}, {wakeups}, " +
                             $"memory {_self.WorkingSet64 / 1048576} MB (private {_self.PrivateMemorySize64 / 1048576}, .NET {GC.GetTotalMemory(false) / 1048576}), " +
                             $"threads {_self.Threads.Count}, handles {_self.HandleCount} — {ResourceContext()}");
                    _cpuWindow.Clear();
                }
            }
            _cpuLast = cpu;
            _cpuLastAt = now;
        }
        catch { /* المراقبة مش لازم توقف البرنامج */ }
    }

    /// <summary>البرنامج كان بيعمل إيه وقت العينة.</summary>
    private string ResourceContext()
    {
        var parts = new List<string>();
        if (_currentLive != null) parts.Add($"live {_currentLive.Kind} «{_currentLive.Name}» {_mp.State}");
        else if (_currentVideo is { } v)
        {
            string kind = v.IsOnline ? "online" : LibraryFiles.IsCloudPath(v.LocalPath) ? "cloud file" : "local file";
            string size = "";
            try { uint w = 0, h = 0; if (_mp.Size(0, ref w, ref h) && w > 0) size = $" {w}x{h}"; } catch { }
            parts.Add($"video {kind}{size} {_mp.State} rate {_mp.Rate:0.##}");
        }
        else parts.Add("idle");
        if (_probeQueue.Count > 0 || _probing) parts.Add($"reading durations ({_probeQueue.Count} left)");
        if (_folderSyncRunning) parts.Add("checking followed folders");
        if (_library.Links.Count > 0) parts.Add($"{_library.Links.Count} followed folders");
        parts.Add($"library {_library.AllVideos.Count()} videos");
        if (_miniMode) parts.Add("mini");
        if (_fullscreen) parts.Add("fullscreen");
        return string.Join(", ", parts);
    }
}
