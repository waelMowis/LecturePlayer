using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// تكرار بعد ما الفيديو يخلص: من غير ← الفيديو ده (🔂) ← المحاضرة كلها (🔁).
/// مفيد للحفظ (قرآن، تمارين نطق) ولما تحب تعيد درس كذا مرة.
/// </summary>
internal sealed partial class MainForm
{
    private readonly GlyphButton _btnRepeat = MakeButton(Glyphs.RepeatAll);
    private ToolStripMenuItem _repeatOffItem = null!, _repeatOneItem = null!, _repeatAllItem = null!;

    private int RepeatMode => Math.Clamp(_settings.RepeatMode, 0, 2);

    private void InitRepeat()
    {
        _btnRepeat.Click += (_, _) => SetRepeatMode((RepeatMode + 1) % 3, announce: true);
        UpdateRepeatUi();
    }

    private ToolStripMenuItem[] BuildRepeatMenuItems()
    {
        _repeatOffItem = Item("من غير تكرار", () => SetRepeatMode(0, announce: true));
        _repeatOneItem = Item("🔂  كرّر الفيديو ده", () => SetRepeatMode(1, announce: true), display: "L");
        _repeatAllItem = Item("🔁  كرّر المحاضرة كلها", () => SetRepeatMode(2, announce: true));
        return new[] { _repeatOffItem, _repeatOneItem, _repeatAllItem };
    }

    private void SetRepeatMode(int mode, bool announce)
    {
        _settings.RepeatMode = Math.Clamp(mode, 0, 2);
        SaveSettings();
        UpdateRepeatUi();
        if (announce)
            ShowStatus(RepeatMode switch
            {
                1 => "🔂 الفيديو ده هيتعاد لما يخلص",
                2 => "🔁 المحاضرة كلها هتتعاد من أولها لما تخلص",
                _ => "التكرار اتقفل"
            }, 3);
    }

    private void UpdateRepeatUi()
    {
        _btnRepeat.Text = RepeatMode == 1 ? Glyphs.RepeatOne : Glyphs.RepeatAll;
        _btnRepeat.Active = RepeatMode != 0;
        _toolTip.SetToolTip(_btnRepeat, RepeatMode switch
        {
            1 => "بيكرّر الفيديو ده — اضغط عشان تكرّر المحاضرة كلها (L)",
            2 => "بيكرّر المحاضرة كلها — اضغط عشان تقفل التكرار (L)",
            _ => "تكرار: مقفول — اضغط عشان تكرّر الفيديو ده لما يخلص (L)"
        });
        if (_repeatOffItem != null)
        {
            _repeatOffItem.Checked = RepeatMode == 0;
            _repeatOneItem.Checked = RepeatMode == 1;
            _repeatAllItem.Checked = RepeatMode == 2;
        }
    }

    /// <summary>بيتنده لما الفيديو يخلص. true = اتعاد (فمنكمّلش للي بعده).</summary>
    private bool TryRepeatAfterEnd(VideoItem v)
    {
        if (_currentGroup is not { } g) return false;
        switch (RepeatMode)
        {
            case 1:
                Log.Info("repeat video: " + v.Title);
                BeginInvoke(() => PlayVideo(g, v, 0));
                return true;
            case 2:
                // آخر فيديو في المحاضرة ← نرجع لأولها؛ غير كده بنكمّل للي بعده حتى لو «التالي تلقائيًا» مقفول
                int i = g.Videos.IndexOf(v);
                var next = i >= 0 && i + 1 < g.Videos.Count ? g.Videos[i + 1] : g.Videos.FirstOrDefault();
                if (next == null) return false;
                Log.Info("repeat lecture: " + next.Title);
                BeginInvoke(() => PlayVideo(g, next, 0));
                return true;
        }
        return false;
    }
}
