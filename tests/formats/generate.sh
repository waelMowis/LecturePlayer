# بيولّد ملفات الاختبار (ثانية واحدة، مقاس صغير) لكل صيغة وكوديك — الفحص التلقائي بيشغّلها كلها بـ VLC بتاع البرنامج.
# التشغيل: cd tests/formats && bash generate.sh
set -u
V="-f lavfi -i testsrc2=size=96x72:rate=10 -f lavfi -i sine=frequency=440:sample_rate=44100 -t 1"
A="-f lavfi -i sine=frequency=440:sample_rate=22050 -t 1"
g(){ out=$1; shift; ffmpeg -hide_banner -loglevel error -y "$@" "$out" && printf "%-26s %7d\n" "$out" $(stat -c%s "$out") || echo "FAILED $out"; }
# ---- video ----
g h264-aac.mp4        $V -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 48k
g hevc-aac.mp4        $V -c:v libx265 -preset ultrafast -x265-params log-level=none -tag:v hvc1 -pix_fmt yuv420p -c:a aac -b:a 48k
g av1-opus.mkv        $V -c:v libsvtav1 -preset 12 -pix_fmt yuv420p -c:a libopus -b:a 32k
g av1-aac.mp4         $V -c:v libaom-av1 -cpu-used 8 -pix_fmt yuv420p -c:a aac -b:a 48k
g h264-ac3.mkv        $V -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a ac3 -b:a 64k
g vp9-opus.webm       $V -c:v libvpx-vp9 -deadline realtime -cpu-used 8 -b:v 60k -c:a libopus -b:a 32k
g vp8-vorbis.webm     $V -c:v libvpx -deadline realtime -b:v 60k -c:a libvorbis
g mpeg4-mp3.avi       $V -c:v mpeg4 -q:v 20 -c:a libmp3lame -b:a 64k
g xvid.divx           $V -c:v mpeg4 -vtag DIVX -q:v 20 -c:a libmp3lame -b:a 64k -f avi
g h264-aac.mov        $V -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 48k
g prores-pcm.mov      $V -t 0.3 -c:v prores -profile:v 0 -c:a pcm_s16le -ar 8000
g mjpeg-pcm.avi       $V -c:v mjpeg -q:v 20 -c:a pcm_u8 -ar 8000
g wmv2-wma.wmv        $V -c:v wmv2 -b:v 60k -c:a wmav2 -b:a 64k
g wmv2-wma.asf        $V -c:v wmv2 -b:v 60k -c:a wmav2 -b:a 64k
g flv1-mp3.flv        $V -c:v flv -b:v 60k -c:a libmp3lame -b:a 64k -ar 44100
g h264-aac.f4v        $V -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 48k -f mp4
g h264-aac.m4v        $V -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 48k -f mp4
g mpeg1-mp2.mpg       $V -r 25 -c:v mpeg1video -b:v 80k -c:a mp2 -b:a 64k
g mpeg2-mp2.mpeg      $V -r 25 -c:v mpeg2video -b:v 80k -c:a mp2 -b:a 64k -f mpeg
g mpeg2-ac3.vob       $V -r 25 -c:v mpeg2video -b:v 80k -c:a ac3 -b:a 64k -f vob
g mpeg2.m2v           -f lavfi -i testsrc2=size=96x72:rate=25 -t 1 -c:v mpeg2video -b:v 80k -f mpeg2video
g h264-aac.ts         $V -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 48k -f mpegts
g h264-ac3.m2ts       $V -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a ac3 -b:a 64k -f mpegts -mpegts_m2ts_mode 1
g h264-ac3.mts        $V -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a ac3 -b:a 64k -f mpegts -mpegts_m2ts_mode 1
g h264-aac.3gp        $V -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 32k -ar 22050 -f 3gp
g h263-aac.3g2        $V -s 128x96 -c:v h263 -c:a aac -b:a 32k -ar 22050 -f 3g2
g theora-vorbis.ogv   $V -c:v libtheora -q:v 5 -c:a libvorbis
g theora-vorbis.ogm   $V -c:v libtheora -q:v 5 -c:a libvorbis -f ogg
g dv-pcm.dv           -f lavfi -i testsrc2=size=720x576:rate=25 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 0.08 -c:v dvvideo -pix_fmt yuv420p -c:a pcm_s16le -ac 2 -f dv
g mpeg2-pcm.mxf       -f lavfi -i testsrc2=size=720x576:rate=25 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 0.2 -c:v mpeg2video -b:v 200k -c:a pcm_s16le -f mxf
g rv10-ac3.rm         $V -s 96x80 -c:v rv10 -b:v 60k -c:a ac3 -b:a 64k -f rm
g rv20-ac3.rmvb       $V -s 96x72 -c:v rv20 -b:v 60k -c:a ac3 -b:a 64k -f rm
g h264.h264           -f lavfi -i testsrc2=size=96x72:rate=10 -t 1 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -f h264
g hevc.hevc           -f lavfi -i testsrc2=size=96x72:rate=10 -t 1 -c:v libx265 -preset ultrafast -x265-params log-level=none -pix_fmt yuv420p -f hevc
g h264-aac.mkv        $V -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 48k
g h264-aac.mk3d       $V -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 48k -f matroska
g mpeg4-mp2.nut       $V -c:v mpeg4 -q:v 20 -c:a mp2 -f nut
g raw.y4m             -f lavfi -i testsrc2=size=96x72:rate=5 -t 0.4 -pix_fmt yuv420p -f yuv4mpegpipe
# ---- audio ----
g mp3.mp3             $A -c:a libmp3lame -b:a 64k
g aac.m4a             $A -c:a aac -b:a 48k
g alac.m4a            $A -c:a alac -f ipod
g aac-book.m4b        $A -c:a aac -b:a 48k -f ipod
g aac-ringtone.m4r    $A -c:a aac -b:a 48k -f ipod
g adts.aac            $A -c:a aac -b:a 48k -f adts
g pcm.wav             $A -c:a pcm_s16le -ar 8000
g flac.flac           $A -c:a flac
g vorbis.ogg          $A -c:a libvorbis
g vorbis.oga          $A -c:a libvorbis -f ogg
g opus.opus           $A -c:a libopus -b:a 32k
g opus.weba           $A -c:a libopus -b:a 32k -f webm
g wma.wma             $A -c:a wmav2 -b:a 64k -f asf
g opus.mka            $A -c:a libopus -b:a 32k -f matroska
g ac3.ac3             $A -c:a ac3 -b:a 64k
g eac3.eac3           $A -c:a eac3 -b:a 96k
g dts.dts             -f lavfi -i sine=frequency=440:sample_rate=48000 -t 0.5 -c:a dca -strict -2 -ac 2 -b:a 768k
g aiff.aiff           $A -c:a pcm_s16be -ar 8000
g aiff.aif            $A -c:a pcm_s16be -ar 8000 -f aiff
g mp2.mp2             $A -c:a mp2 -b:a 64k
g mp2.mpa             $A -c:a mp2 -b:a 64k -f mp2
g au.au               $A -ar 8000 -c:a pcm_s16be -ar 8000
g alac.caf            $A -c:a alac
g wavpack.wv          $A -c:a wavpack
g tta.tta             $A -c:a tta
g speex.spx           -f lavfi -i sine=frequency=440:sample_rate=16000 -t 2 -c:a libspeex -f ogg
g aac.3ga             $A -c:a aac -b:a 32k -f 3gp
g hevc10-aac.mkv      $V -c:v libx265 -preset ultrafast -x265-params log-level=none -pix_fmt yuv420p10le -c:a aac -b:a 32k
