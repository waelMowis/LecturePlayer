using System.Reflection;
using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// فحص شاشة البث: الراديو لا يحتاج أداة يوتيوب، وأعمدة الجداول المعكوسة تُرسم في أماكنها،
/// والنقر المزدوج على المحطة يشغّلها وتُميَّز في القائمة، وتنزيل الأدوات لا يتصادم مع نفسه.
/// </summary>
internal sealed partial class MainForm
{
    internal void RunLiveChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("live: " + what); }

        // الراديو: رابطه بث مباشرة حتى لو انتهى بـ .php — لا ينتظر أداة يوتيوب
        Check(!NeedsResolver(new LiveChannel { Kind = LiveKind.Radio, Url = "https://radio.example.com/stream.php?id=4" }), "radio .php is a stream");
        Check(NeedsResolver(new LiveChannel { Kind = LiveKind.Radio, Url = "https://www.youtube.com/watch?v=abc" }), "radio on youtube needs the resolver");
        Check(NeedsResolver(new LiveChannel { Kind = LiveKind.Tv, Url = "https://tv.example.com/watch/channel.html" }) && !NeedsResolver(new LiveChannel { Kind = LiveKind.Tv, Url = "https://cdn.example.com/a/index.m3u8" }), "tv page vs stream");

        // جدول بأعمدة معكوسة: العمود الأول (الاسم) على اليمين فعلًا
        using (var host = new Form { Size = new Size(700, 300), StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ShowInTaskbar = false })
        {
            var lv = DarkList.Setup(new ListView { Dock = DockStyle.Fill });
            lv.Columns.Add("الاسم", 300); lv.Columns.Add("البلد", 160); lv.Columns.Add("النوع", 120); lv.Columns.Add("الجودة", 90);
            DarkList.ReverseColumns(lv);
            host.Controls.Add(lv);
            host.Show();
            var item = lv.Items.Add(new ListViewItem(new[] { "اسم", "بلد", "نوع", "جودة" }));
            Application.DoEvents();
            var name = DarkList.CellBounds(lv, item, 0, item.Bounds);
            var quality = DarkList.CellBounds(lv, item, 3, item.Bounds);
            Check(name.Left > quality.Left && name.Left == item.Bounds.Left + 90 + 120 + 160 && name.Width == 300, $"name column on the right: {name} vs {quality}");
        }

        // شاشة البث: المفضلة، والنقر المزدوج يشغّل، والمحطة الشغالة مميزة
        var lib = new CourseLibrary();
        var fav = new LiveChannel { Name = "إذاعة الفحص", Url = "https://radio.example.com/stream.php", Kind = LiveKind.Radio, Group = "Egypt", Tags = "quran", Favorite = true };
        var fav2 = new LiveChannel { Name = "قناة الفحص", Url = "https://cdn.example.com/x.m3u8", Kind = LiveKind.Tv, Favorite = true };
        lib.Live.Add(fav);
        lib.Live.Add(fav2);
        LiveChannel? playing = null;
        using (var form = new LiveForm(new LiveForm.Host(() => lib, c => playing = c, _ => { }, _ => { }, () => { }, () => playing, () => playing = null)))
        {
            form.Show(this);
            Application.DoEvents();
            var list = (ListView)typeof(LiveForm).GetField("_list", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Check(list.Items.Count == 2, "favorites listed: " + list.Items.Count);
            var row = list.Items[1];
            var at = DarkList.CellBounds(list, row, 0, row.Bounds);
            var dbl = typeof(Control).GetMethod("OnMouseDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
            dbl.Invoke(list, new object[] { new MouseEventArgs(MouseButtons.Left, 2, at.Left + 20, at.Top + at.Height / 2, 0) });
            Check(playing == fav2, "double click plays the row: " + playing?.Name);
            typeof(LiveForm).GetMethod("MarkPlaying", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            Check(row.SubItems[0].Text.StartsWith("🔊 ") && row.ForeColor == Theme.Success && !list.Items[0].SubItems[0].Text.StartsWith("🔊"), "playing row marked: " + row.SubItems[0].Text);
            SaveSmokeShot(form, "live-form");
            playing = null;
            typeof(LiveForm).GetMethod("MarkPlaying", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            Check(!row.SubItems[0].Text.StartsWith("🔊"), "mark removed after stop");
            form.Close();
        }
        Check(_activityLive.Caption == "بث", "live button in the side bar");
        Console.WriteLine("SMOKE live: ok");
    }
}
