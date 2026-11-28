using System.Text.Json;
using LecturePlayer.Data;

namespace LecturePlayer;

internal partial class MainForm
{
    private const string SmokeArduBad = "void setup() {\n  pinMode(13, OUTPUT)\n  digitalWrite(13, HIGH);\n}\nvoid loop() {\n  dlay(100);\n}\n";
    private const string SmokeArduGood = """
        // وميض + صدى للشاشة التسلسلية
        int n = 0;
        void setup() {
          pinMode(13, OUTPUT);
          Serial.begin(9600);
          Serial.println("بدأ البرنامج");
        }
        void loop() {
          digitalWrite(13, !digitalRead(13));
          Serial.print("tick ");
          Serial.println(n++);
          while (Serial.available()) { char c = Serial.read(); if (c >= 'a' && c <= 'z') c -= 32; Serial.write(c); }
          delay(200);
        }
        """;

    /// <summary>
    /// محاكي أردوينو (المرحلة الأولى): الأدوات جنب البرنامج ← ترجمة كود فيه أخطاء (أرقام السطور + شرح عربي) ←
    /// ترجمة كود سليم ← المحاكي نفسه (لمبة 13 + الشاشة التسلسلية بالعربي) ← التشغيل من النافذة + إرسال نص للوحة.
    /// كله من غير نت.
    /// </summary>
    private void RunArduinoChecks(Func<string, string> Js, Func<Func<bool>, int, bool> Until)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("arduino: " + what); }
        Check(Arduino.ToolsDir() != null, "toolchain next to the app");
        Check(Arduino.Explain("'dlay' was not declared in this scope", out bool known)?.Contains("dlay") == true && known, "explain");

        Js("window.__ard=null;masar.sciTest().Sci.ardu.open({code:" + JsonSerializer.Serialize(SmokeArduGood) + "}).then(()=>window.__ard='ok',e=>window.__ard=String(e.message));'ok'");
        Check(Until(() => Js("String(window.__ard)") != "null", 15_000), "window opened");
        Check(Js("String(window.__ard)+':'+!!document.querySelector('.ard-win wokwi-arduino-uno')+':'+(document.querySelectorAll('.ard-gut div').length===document.querySelector('.ard-ta').value.split('\\n').length)") == "ok:true:true", "window: " + Js("String(window.__ard)"));

        // كود فيه أخطاء
        Js("window.__arc=null;masar.sciTest().Sci.ardu.compile(" + JsonSerializer.Serialize(SmokeArduBad) + ").then(r=>window.__arc=r);'ok'");
        Check(Until(() => Js("String(!!window.__arc)") == "true", 300_000), "compile (errors) finished");
        string bad = Js("(()=>{const r=window.__arc;const L=r.problems.filter(p=>p.kind==='error').map(p=>p.line);return r.ok+'|'+(L.includes(6)&&(L.includes(2)||L.includes(3)))+'|'+r.problems.every(p=>/[ء-ي]/.test(p.msg))})()");
        Console.WriteLine("SMOKE arduino errors: " + Js("JSON.stringify(window.__arc.problems.map(p=>p.line+':'+p.orig))") + " in " + Js("String(window.__arc.ms)") + " ms");
        Check(bad == "false|true|true", "errors: " + bad + " " + Js("JSON.stringify(window.__arc)"));

        // كود سليم ← hex ← المحاكي مباشرة (1.6 ثانية من زمن اللوحة)
        Js("window.__arc=null;masar.sciTest().Sci.ardu.compile(" + JsonSerializer.Serialize(SmokeArduGood) + ").then(r=>window.__arc=r);'ok'");
        Check(Until(() => Js("String(!!window.__arc)") == "true", 300_000), "compile (good) finished");
        Check(Js("(()=>{const r=window.__arc;return r.ok+'|'+(r.hex.length>500)+'|'+(r.flash>500&&r.flashMax===32256)})()") == "true|true|true", "compile: " + Js("JSON.stringify(window.__arc).slice(0,400)"));
        Console.WriteLine("SMOKE arduino compile: " + Js("String(window.__arc.ms)") + " ms, " + Js("String(window.__arc.flash)") + " bytes");
        string sim = Js("""
            (()=>{const A=masar.sciTest().Sci.ardu,u=new A.Uno(window.__arc.hex);let t=0,b=[];u.watchers.add(p=>{if(p===13)t++});u.onSerial=x=>b.push(x);
            const t0=performance.now();u.runFor(1600);const ms=performance.now()-t0;const s=new TextDecoder().decode(new Uint8Array(b));
            window.__arsim={t,s,ms:Math.round(ms)};return (t>=6)+'|'+s.includes('بدأ البرنامج')+'|'+s.includes('tick 5')})()
            """);
        Console.WriteLine("SMOKE arduino sim: " + Js("JSON.stringify(window.__arsim)"));
        Check(sim == "true|true|true", "simulator: " + sim);

        // التشغيل من النافذة: زر «تشغيل» ← اللوحة شغالة ← الشاشة التسلسلية ← إرسال نص
        Js("document.querySelector('.ard-win .sci-win-act .pri').click();'ok'");
        Check(Until(() => Js("String(document.querySelector('.ard-ser').textContent.includes('tick 3'))") == "true", 120_000), "serial monitor shows output: " + Js("document.querySelector('.ard-ser').textContent.slice(0,200)"));
        Js("(()=>{const i=document.querySelector('.ard-sin');i.value='masar';i.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter'}));return 'ok'})()");
        Check(Until(() => Js("String(document.querySelector('.ard-ser').textContent.includes('MASAR'))") == "true", 10_000), "serial input echoed");
        Check(Js("String(masar.sciTest().Sci.ardu.toggles>=4)") == "true", "LED 13 toggles in the window");
        Check(Js("String(document.querySelector('.ard-ser').textContent.includes('بدأ البرنامج'))") == "true", "arabic serial");
        Console.WriteLine("SMOKE arduino speed: " + Js("String(masar.sciTest().Sci.ardu.uno.speed)"));
        SaveSmokeShot(this, "arduino-sim");

        // عرض الأخطاء في النافذة
        Js("(()=>{const A=masar.sciTest().Sci.ardu;A.stop(true);A.ui.ta.value=" + JsonSerializer.Serialize(SmokeArduBad) + ";A.ui.ta.dispatchEvent(new Event('input'));A.run();return 'ok'})()");
        Check(Until(() => Js("String(document.querySelectorAll('.ard-prob.err').length>=2&&document.querySelectorAll('.ard-gut div.err').length>=2)") == "true", 120_000), "errors shown in window");
        SaveSmokeShot(this, "arduino-errors");
        Js("masar.sciTest().Sci.apps.close();'ok'");
        Check(Until(() => Js("String(!document.querySelector('.ard-win'))") == "true", 3000), "window closed");
        Console.WriteLine("SMOKE arduino: ok");
    }
}
