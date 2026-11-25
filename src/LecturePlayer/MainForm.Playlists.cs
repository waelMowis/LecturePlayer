using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// قوائم يوتيوب «حيّة»: المحاضرة اللي جاية من قائمة تشغيل بتفتكر رابطها،
/// والتحديث (يدوي أو تلقائي كل كام ساعة) بيجيب الفيديوهات اللي اتضافت للقائمة ويعلّمها «جديد».
/// </summary>
internal sealed partial class MainForm
{
    private bool _syncingPlaylists;

    /// <summary>groups = null ← كل المحاضرات المربوطة. quiet = تحديث تلقائي (من غير رسايل لو مفيش جديد).</summary>
    private async Task SyncPlaylistsAsync(IReadOnlyList<LectureGroup>? groups, bool quiet)
    {
        if (_syncingPlaylists) { if (!quiet) ShowStatus("التحديث يعمل بالفعل…"); return; }
        groups ??= _library.AllGroups.Where(g => g.IsPlaylist).ToList();
        if (groups.Count == 0)
        {
            if (!quiet) Dialogs.Info(this, "لا توجد محاضرات مربوطة بقوائم يوتيوب بعد.\n\n" +
                                         "تُربط القوائم الجديدة تلقائيًا عند إضافتها. أما القديمة: انقر بالزر الأيمن على المحاضرة ← «ربط بقائمة تشغيل يوتيوب».");
            return;
        }
        if (quiet ? YtDlp.FindYtDlp() == null : !await EnsureYtDlpAsync()) return;

        _syncingPlaylists = true;
        var report = new List<(LectureGroup Group, int Added, int Missing)>();
        var errors = new List<string>();
        bool checkpointed = false;
        try
        {
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (_closing) return;
                ShowStatus($"🔄 جارٍ تحديث قوائم يوتيوب… ({i + 1} من {groups.Count}) {g.Name}", 60);
                OnlineListing listing;
                try { listing = await YtDlp.ListAsync(g.SourceUrl!, wholePlaylist: true, CancellationToken.None); }
                catch (Exception ex) { errors.Add($"{g.Name}: {ex.Message}"); continue; }
                if (_closing) return;

                // الـ Checkpoint قبل أول تعديل بس (عشان Ctrl+Z يرجّع التحديث كله مرة واحدة)
                if (!checkpointed && listing.Entries.Count > 0) { Checkpoint("تحديث قوائم يوتيوب"); checkpointed = true; }
                var r = PlaylistSync.Apply(g, listing, DateTime.UtcNow);
                MarkDirty();
                if (r.Added.Count > 0 || r.MissingFromSource > 0) report.Add((g, r.Added.Count, r.MissingFromSource));
            }
        }
        finally { _syncingPlaylists = false; }

        int total = report.Sum(x => x.Added);
        SaveLibrary();
        if (total > 0)
        {
            RebuildTree();
            RefreshHome();
            var first = report.First(x => x.Added > 0).Group;
            string where = string.Join("، ", report.Where(x => x.Added > 0).Select(x => $"«{x.Group.Name}» ({x.Added})"));
            Notify($"🆕 {total} فيديو جديد", where);
            if (!quiet && first.Videos.FirstOrDefault(v => v.IsNew) is { } v) SelectItem(v);
        }
        else ShowStatus(errors.Count == 0 ? "🔄 قوائم يوتيوب محدّثة — لا توجد فيديوهات جديدة ✓" : "", 5);

        if (!quiet)
        {
            var lines = new List<string>();
            if (total > 0) lines.Add($"أُضيف {total} فيديو جديد (عليها علامة «جديد» في المكتبة):\n" +
                                     string.Join("\n", report.Where(x => x.Added > 0).Select(x => $"• {x.Group.Name}: {x.Added}")));
            int missing = report.Sum(x => x.Missing);
            if (missing > 0) lines.Add($"\nيوجد {missing} فيديو حُذفت من قوائم يوتيوب (أو أصبحت خاصة). تركتها عندك بتقدّمك وملاحظاتك.");
            if (errors.Count > 0) lines.Add("\nتعذّر التحديث:\n" + string.Join("\n", errors.Take(5)));
            if (lines.Count > 0) Dialogs.Info(this, string.Join("\n", lines), "تحديث قوائم يوتيوب");
        }
    }

    /// <summary>محاضرة قديمة (اتضافت قبل الميزة دي): نربطها برابط القائمة.</summary>
    private void LinkPlaylist(LectureGroup g)
    {
        string initial = "";
        try { if (Clipboard.ContainsText() && PlaylistSync.IsPlaylistUrl(Clipboard.GetText())) initial = Clipboard.GetText().Trim(); } catch { }
        string? url = Dialogs.Input(this, "ربط بقائمة يوتيوب", $"رابط قائمة التشغيل التي جاءت منها «{g.Name}» (فيه list=):", initial);
        if (url == null) return;
        if (!MediaFiles.IsUrl(url) || !PlaylistSync.IsPlaylistUrl(url))
        {
            Dialogs.Error(this, "هذا ليس رابط قائمة تشغيل. يجب أن يحتوي الرابط على list= أو /playlist");
            return;
        }
        Checkpoint("ربط بقائمة يوتيوب");
        g.SourceUrl = url;
        g.InfoUpdatedUtc = DateTime.UtcNow;
        MarkDirty();
        RefreshGroup(g);
        _ = SyncPlaylistsAsync(new[] { g }, quiet: false);
    }

    private void ClearNew(LectureGroup g)
    {
        foreach (var v in g.Videos.Where(v => v.IsNew)) { v.IsNew = false; v.Touch(); }
        MarkDirty();
        RebuildTree();
    }

    /// <summary>بيتنادى كل دقيقة: لو فيه قوائم عدّى عليها الوقت المحدد، نحدّثها في الخلفية.</summary>
    private void CheckPlaylistSyncDue()
    {
        if (!_settings.AutoSyncPlaylists || _syncingPlaylists || _closing) return;
        var due = PlaylistSync.Due(_library, DateTime.UtcNow, TimeSpan.FromHours(Math.Max(1, _settings.PlaylistSyncHours)));
        if (due.Count > 0) _ = SyncPlaylistsAsync(due, quiet: true);
    }

    /// <summary>للشاشة الرئيسية: الفيديوهات الجديدة اللي لسه متفتحتش.</summary>
    private List<(LectureGroup Group, VideoItem Video)> NewVideos() =>
        _library.AllGroups.SelectMany(g => g.Videos.Where(v => v.IsNew).Select(v => (g, v))).ToList();
}
