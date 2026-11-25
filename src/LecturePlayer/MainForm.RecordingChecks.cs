using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>
/// فحص التسجيل على ويندوز حقيقي: شاشة «مسار» + كاميرا وهمية (lavfi) + صوت الجهاز + أقلام على الشاشة + لمعة الماوس،
/// إيقاف مؤقت وعلامات، وبعدين قص وتغيير المشهد والحفظ كدرس بعلامات.
/// </summary>
internal sealed partial class MainForm
{
    internal void RunRecordingChecks()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("record: " + what); }
        void Pump(int ms) { var w = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < w) { Application.DoEvents(); Thread.Sleep(15); } }
        T Run<T>(Func<Task<T>> f) { var t = f(); while (!t.IsCompleted) Pump(30); return t.GetAwaiter().GetResult(); }
        if (!HasLibrary) return;
        if (Tools.FindFfmpeg() == null) Run(async () => { await Tools.DownloadFfmpegAsync(new Progress<string>(), CancellationToken.None); return 0; });
        string ff = Tools.FindFfmpeg() ?? throw new Exception("record: no ffmpeg");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? final = null, work = null;
        try
        {
            // شباك الإعداد (صورة)
            SmokeDialogShots = true;
            try { Check(RecordSetupDialog(new() { "كاميرا الفحص" }, AudioRecorder.Microphones(), null) == null, "setup dialog (shot)"); }
            finally { SmokeDialogShots = false; }

            // ملاحظات التحديث من غير ## و**، واختيار مقاس الكاميرا، وترتيب شاشات DXGI، ومستوى المايك
            Check(PlainNotes("## عنوان\n\n- **مهم** كلام `كود`\n  - فرعي") == "■ عنوان\n\n• مهم كلام كود\n      ◦ فرعي", "release notes as plain text: " + PlainNotes("## عنوان\n\n- **مهم** كلام `كود`\n  - فرعي"));
            string listing = "[dshow @ 0] DirectShow video device options (from video devices)\n[dshow @ 0]  Pin \"Capture\"\n" +
                "[dshow @ 0]   vcodec=mjpeg  min s=3840x2160 fps=30 max s=3840x2160 fps=30\n" +
                "[dshow @ 0]   vcodec=mjpeg  min s=1280x720 fps=5 max s=1280x720 fps=30\n" +
                "[dshow @ 0]   pixel_format=yuyv422  min s=1280x720 fps=5 max s=1280x720 fps=10\n" +
                "[dshow @ 0]   pixel_format=yuyv422  min s=640x480 fps=5 max s=640x480 fps=30\n";
            Check(CameraFeed.PickMode(listing) == "-video_size 1280x720 -framerate 30 -vcodec mjpeg", "camera mode pick: " + CameraFeed.PickMode(listing));
            Check(CameraFeed.PickMode("[dshow @ 0]   pixel_format=yuyv422  min s=640x480 fps=5 max s=640x480 fps=30\n") == "", "camera mode: keep default when no HD");
            Console.WriteLine("SMOKE record: dxgi outputs [" + string.Join(", ", DxgiOutputs.FirstAdapterOutputs()) + "], screens [" + string.Join(", ", Screen.AllScreens.Select(x => x.DeviceName)) + "]");
            var pcm = new byte[8]; BitConverter.GetBytes((short)16384).CopyTo(pcm, 0);
            Check(Math.Abs(AudioRecorder.Peak(new NAudio.Wave.WaveFormat(48000, 16, 2), pcm, 8) - 0.5f) < 0.01f, "mic level from 16-bit PCM");
            Check(AudioRecorder.Peak(new NAudio.Wave.WaveFormat(48000, 16, 2), new byte[8], 8) == 0, "silence = 0");

            // زراير الشريط الجانبي: سبورة (قايمة) وتسجيل
            Check(_activityBoard.Caption == "سبورة" && _activityRecord.Caption == "تسجيل", "rail buttons");
            ShowBoardActivityMenu(_activityBoard);
            Pump(300);
            var bm = LastBoardMenu!;
            Check(bm.Visible && bm.Items.OfType<ToolStripMenuItem>().Any(i => i.Text!.Contains("سبورة جديدة")) && bm.Items.OfType<ToolStripMenuItem>().Any(i => i.Text!.Contains("سبوراتي")), "board rail menu");
            SaveSmokeShot(this, "rail-board-menu");
            bm.Close();

            string enc = Run(() => ScreenRecorder.PickEncoderAsync(ff));
            bool dda = Run(() => ScreenRecorder.DdagrabWorksAsync(ff));
            Console.WriteLine($"SMOKE record: encoder={enc} ddagrab={dda} mics={AudioRecorder.Microphones().Count}");

            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            var area = Rectangle.Intersect(Bounds, Screen.FromControl(this).Bounds);
            SmokeCameraInput = "-f lavfi -i testsrc2=size=640x360:rate=30,realtime";
            var choice = new RecChoice("masar", 0, null, IntPtr.Zero, null, SystemAudio: true, Camera: "كاميرا الفحص",
                RecordingEdit.Scene.ScreenWithCam, CameraWindow.Shape.Circle, CamFile: true, CursorFx: true, Countdown: false, "تسجيل الفحص", null);
            Check(Run(() => StartRecordingAsync(choice)), "recording started");
            var s = _rec!;
            Check(s.Rec.Running && s.Bar.Visible, "recorder + bar up: " + s.Rec.Source + "/" + s.Rec.Encoder);
            Check(_activityRecord.Caption == "إنهاء", "rail record button shows «إنهاء» while recording");
            // أقلام على الشاشة: مستطيل أحمر تقيل في نص «مسار»
            ToggleScreenInk();
            Check(_screenInk != null, "screen ink opened");
            Pump(300);
            var c0 = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
            var ink = _screenInk!;
            var o = ink.PointToClient(c0);
            ink.AddStroke(ScreenInk.Tool.Pen, Color.FromArgb(255, 0, 0), 28, new PointF(o.X - 120, o.Y), new PointF(o.X + 120, o.Y));
            Pump(1500);
            SaveSmokeShot(this, "record-live");
            RecordingMark("علامة الفحص");
            double markAt = s.Rec.Elapsed.TotalSeconds;
            Pump(1200);
            Run(async () => { await TogglePauseAsync(); return 0; });
            Check(s.Rec.Paused, "paused");
            var pausedAt = s.Rec.Elapsed;
            Pump(1200);
            Check(Math.Abs((s.Rec.Elapsed - pausedAt).TotalMilliseconds) < 50, "clock stops while paused");
            Run(async () => { await TogglePauseAsync(); return 0; });
            Check(!s.Rec.Paused, "resumed");
            Pump(2500);
            Check(s.CamWin != null && s.CamWin.FramesShown > 5, "camera preview frames: " + s.CamWin?.FramesShown);
            double recorded = s.Rec.Elapsed.TotalSeconds;
            ToggleScreenInk();
            Check(_screenInk == null, "screen ink closed");
            // قص: نشيل أول نص ثانية، وثانية بعد العلامة (العلامة نفسها لازم تفضل وتتزحلق) — والمشهد يتغير لـ «الكاميرا كبيرة» فوق شمال
            SmokeRecordEdit = new RecordEdit("تسجيل الفحص", 0.5, 0, new() { (markAt + 0.8, markAt + 1.8) }, RecordingEdit.Scene.CamBigScreenSmall, RecordingEdit.Corner.TopLeft);
            SmokeKeepRecording = true;
            work = s.Dir;
            var v = Run(() => StopRecordingAsync(save: true));
            Check(v != null && File.Exists(v.Path), "saved: " + v?.Path + " log: " + s.Rec.LastLog);
            final = v!.Path;
            Check(!Recording && !s.Bar.Visible, "bar closed");
            Check(_activityRecord.Caption == "تسجيل", "rail record button back to «تسجيل»");
            Console.WriteLine("SMOKE record: camera sync offsets " + string.Join(", ", s.Cam!.Offsets.Select(x => x.ToString("0.000"))));
            Check(s.Cam.Offsets.Count == 2, "camera aligned per segment (2 segments after pause)");
            double dur = Run(() => ScreenRecorder.DurationAsync(ff, final));
            double expect = recorded - 0.5 - 1.0;
            Console.WriteLine($"SMOKE record: recorded {recorded:0.00}s → final {dur:0.00}s (expect ≈{expect:0.00}), marks: {string.Join(" | ", v.Bookmarks.Select(b => b.TimeMs + ":" + b.Note))}");
            Check(Math.Abs(dur - expect) < 1.2, $"duration after trim {dur:0.00} vs {expect:0.00}");
            var mk = v.Bookmarks.FirstOrDefault(b => b.Note == "علامة الفحص");
            Check(mk != null && Math.Abs(mk.TimeMs / 1000.0 - (markAt - 0.5)) < 0.6, $"manual mark became a bookmark, shifted by the trimmed start ({mk?.TimeMs} vs {(markAt - 0.5) * 1000:0})");
            Check(_library.Courses.Any(c => c.Name == "تسجيلاتي" && c.Groups.Any(g => g.Videos.Contains(v))), "added to «تسجيلاتي»");
            Check(LastRecordingCamera != null && File.Exists(LastRecordingCamera) && File.Exists(Path.ChangeExtension(final, null) + " — كاميرا.mp4"), "camera file kept");

            // الشاشة الخام: الأحمر بتاع القلم موجود في الفيديو، وشريط التحكم مش موجود
            string png = Path.Combine(work, "frame.png");
            var r = Run(() => ScreenRecorder.RunAsync(ff, $"-hide_banner -v error -y -ss 1.6 -i \"{LastRecordingScreen}\" -frames:v 1 \"{png}\"", 30_000));
            Check(r.Code == 0 && File.Exists(png), "frame extracted: " + r.Err);
            using (var bmp = new Bitmap(png))
            {
                double kx = (double)bmp.Width / area.Width, ky = (double)bmp.Height / area.Height;
                int px = (int)((c0.X - area.X) * kx), py = (int)((c0.Y - area.Y) * ky);
                int red = 0;
                for (int dx = -60; dx <= 60; dx += 10) { var p = bmp.GetPixel(Math.Clamp(px + dx, 0, bmp.Width - 1), Math.Clamp(py, 0, bmp.Height - 1)); if (p.R > 180 && p.G < 90 && p.B < 90) red++; }
                Check(red >= 8, $"screen-ink stroke visible in the recording ({red}/13 red samples at {px},{py})");
                bmp.Save(Path.Combine(work, "frame-copy.png"));
                SaveSmokeShotFile(Path.Combine(work, "frame-copy.png"), "record-frame");
            }
            // الملف النهائي: المشهد «الكاميرا كبيرة» ← لون testsrc2 في معظم الصورة
            string png2 = Path.Combine(work, "final.png");
            r = Run(() => ScreenRecorder.RunAsync(ff, $"-hide_banner -v error -y -ss 1 -i \"{final}\" -frames:v 1 \"{png2}\"", 30_000));
            Check(r.Code == 0 && File.Exists(png2), "final frame: " + r.Err);
            SaveSmokeShotFile(png2, "record-final");
            // لو الفقاعة كانت مستخبية: «الشاشة + الكاميرا صغيرة» بتتركّب دايرة من ملف الكاميرا (بنفس الـ encoder اللي على الجهاز)
            string circ = Path.Combine(work, "circle.mp4"), png3 = Path.Combine(work, "circle.png");
            Run(async () => { await RecordingEdit.ComposeAsync(ff, LastRecordingScreen!, LastRecordingCamera, RecordingEdit.Scene.ScreenWithCam, RecordingEdit.Corner.TopLeft, 0.28, Array.Empty<(string, double)>(), RecordingEdit.EncoderArgs(enc), circ, camInScreen: false); return 0; });
            r = Run(() => ScreenRecorder.RunAsync(ff, $"-hide_banner -v error -y -ss 1 -i \"{circ}\" -frames:v 1 \"{png3}\"", 30_000));
            Check(r.Code == 0 && File.Exists(png3), "circle camera compose: " + r.Err);
            SaveSmokeShotFile(png3, "record-circle");
            Console.WriteLine($"SMOKE record: ok in {sw.Elapsed.TotalSeconds:0}s");
        }
        finally
        {
            if (Recording) try { Run(() => StopRecordingAsync(save: false)); } catch { }
            _screenInk?.Close();
            SmokeRecordEdit = null; SmokeKeepRecording = false; SmokeCameraInput = null;
            foreach (var c in _library.Courses.Where(c => c.Name == "تسجيلاتي").ToList()) _library.Courses.Remove(c);
            if (final != null) { try { File.Delete(final); } catch { } try { File.Delete(Path.ChangeExtension(final, null) + " — كاميرا.mp4"); } catch { } }
            if (work != null) try { Directory.Delete(work, true); } catch { }
            RebuildTree();
        }
    }

    /// <summary>صورة جاهزة (فريم من الفيديو) في smoke-shots وفي السجل — مصغّرة.</summary>
    private static void SaveSmokeShotFile(string png, string name)
    {
        try
        {
            string dir = Path.Combine(Environment.CurrentDirectory, "smoke-shots");
            Directory.CreateDirectory(dir);
            string outFile = Path.Combine(dir, name + ".png");
            using (var src = new Bitmap(png))
            {
                double k = Math.Min(1.0, 960.0 / src.Width);
                using var small = new Bitmap(src, new Size(Math.Max(1, (int)(src.Width * k)), Math.Max(1, (int)(src.Height * k))));
                small.Save(outFile, System.Drawing.Imaging.ImageFormat.Png);
            }
            string b64 = Convert.ToBase64String(File.ReadAllBytes(outFile));
            for (int i = 0, n = 0; i < b64.Length; i += 3000, n++)
                Console.WriteLine($"SMOKESHOT {name}.png {n} {b64.Substring(i, Math.Min(3000, b64.Length - i))}");
        }
        catch (Exception ex) { Console.WriteLine("SMOKE shot " + name + " failed: " + ex.Message); }
    }
}
