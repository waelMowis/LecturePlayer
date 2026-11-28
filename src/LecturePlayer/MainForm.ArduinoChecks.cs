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


    private const string SmokeArduLcd = """
        #include <LiquidCrystal.h>
        LiquidCrystal lcd(12, 11, 5, 4, 3, 2);
        void setup() { lcd.begin(16, 2); pinMode(7, INPUT_PULLUP); pinMode(9, OUTPUT); Serial.begin(9600); }
        void loop() {
          int v = analogRead(A0);
          analogWrite(9, v / 4);
          lcd.setCursor(0, 0); lcd.print("A0="); lcd.print(v); lcd.print("   ");
          lcd.setCursor(0, 1); lcd.print(digitalRead(7) ? "UP  " : "DOWN");
          Serial.println(v);
          delay(100);
        }
        """;
    private const string SmokeArduServo = """
        #include <Servo.h>
        Servo s;
        void setup() { s.attach(9); pinMode(12, OUTPUT); Serial.begin(9600); }
        void loop() {
          digitalWrite(12, LOW); delayMicroseconds(2); digitalWrite(12, HIGH); delayMicroseconds(10); digitalWrite(12, LOW);
          long cm = pulseIn(11, HIGH, 30000) / 58;
          s.write(constrain(cm, 0, 180));
          tone(8, 1000, 60);
          Serial.print("D="); Serial.println(cm);
          delay(150);
        }
        """;
    private const string SmokeArduSensors = """
        const float BETA = 3950;
        void setup() { pinMode(7, INPUT); pinMode(4, INPUT); Serial.begin(9600); }
        void loop() {
          int l = analogRead(A0), t = analogRead(A1);
          float c = 1 / (log(1 / (1023. / t - 1)) / BETA + 1.0 / 298.15) - 273.15;
          analogWrite(3, digitalRead(7) ? 255 : 0);
          analogWrite(5, digitalRead(4) ? 255 : 0);
          analogWrite(6, 0);
          Serial.print("L="); Serial.print(l); Serial.print(" T="); Serial.print(c, 0);
          Serial.print(" M="); Serial.print(digitalRead(7)); Serial.print(" S="); Serial.println(digitalRead(4));
          delay(100);
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
        RunArduinoCircuitChecks(Js, Until);
    }

    /// <summary>
    /// محاكي أردوينو (المرحلة الثانية): محرر الدوائر — لوحة توصيل بشبكاتها، ليد بمقاومة، زر، مقاومة متغيرة، شاشة LCD،
    /// سيرفو، جرس، حساس مسافة، حساسات الضوء والحرارة والحركة، مفتاح انزلاقي، ليد RGB — والتحذيرات.
    /// </summary>
    private void RunArduinoCircuitChecks(Func<string, string> Js, Func<Func<bool>, int, bool> Until)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("arduino circuit: " + what); }
        // كود + دائرة ← ترجمة ← تشغيل ← انتظار أول سطر على الشاشة التسلسلية
        void Run(string code, string circuitJs, string what)
        {
            Js("window.__arr=null;(async()=>{try{const A=masar.sciTest().Sci.ardu;A.stop(true);await A.open({code:" + JsonSerializer.Serialize(code) + ",circuit:{parts:[{id:'uno',type:'uno',x:0,y:0}],wires:[]}});" +
               "const C=A.circuit,W=(a,b)=>C.wires.push({id:'w'+(++C.seq),a:{p:a.split(':')[0],pin:a.split(':')[1]},b:{p:b.split(':')[0],pin:b.split(':')[1]},pts:[],color:'#1971c2'});" +
               circuitJs + ";C.changed(false);C.fit();await A.run();window.__arr=A.uno?.running?'ok':'not running: '+document.querySelector('.sci-win-msg').textContent}catch(e){window.__arr='err: '+e.message}})();'ok'");
            Check(Until(() => Js("String(window.__arr)") != "null", 240_000), what + ": started");
            Check(Js("String(window.__arr)") == "ok", what + ": " + Js("String(window.__arr)"));
            Check(Until(() => Js("String(document.querySelector('.ard-ser').textContent.split('\\n').length>2)") == "true", 60_000), what + ": serial output");
        }
        string Last() => Js("(()=>{const l=document.querySelector('.ard-ser').textContent.trim().split('\\n');return l[l.length-1].trim()})()");
        string Lcd() => Js("String.fromCharCode(...masar.sciTest().Sci.ardu.circuit.parts.get('lcd1').w.characters)");

        // 1) لوحة توصيل + ليد بمقاومة (PWM من المقاومة المتغيرة) + زر + LCD
        Run(SmokeArduLcd, """
            await C.addPart('breadboard',{id:'bb1',x:-20,y:250},true);await C.addPart('resistor',{id:'r1',x:0,y:0},true);await C.addPart('led',{id:'led1',x:0,y:0,props:{color:'green'}},true);
            await C.addPart('potentiometer',{id:'pot1',x:330,y:120},true);await C.addPart('pushbutton',{id:'btn1',x:330,y:20},true);await C.addPart('lcd1602',{id:'lcd1',x:-40,y:-190},true);
            C.topology();C.moveTo('r1','1',...C.holeAt('bb1','c5'));C.moveTo('led1','A',...C.holeAt('bb1','e11'));
            W('uno:9','bb1:a5');W('uno:GND.1','bb1:a10');
            W('pot1:SIG','uno:A0');W('pot1:VCC','uno:5V');W('pot1:GND','uno:GND.2');W('btn1:1.l','uno:7');W('btn1:2.r','uno:GND.3');
            W('lcd1:VSS','uno:GND.2');W('lcd1:VDD','uno:5V');W('lcd1:RW','uno:GND.2');W('lcd1:RS','uno:12');W('lcd1:E','uno:11');W('lcd1:D4','uno:5');W('lcd1:D5','uno:4');W('lcd1:D6','uno:3');W('lcd1:D7','uno:2')
            """, "lcd circuit");
        Check(Js("[...masar.sciTest().Sci.ardu.circuit.topo.plugged].filter(n=>/^(r1|led1):/.test(n)).sort().join(',')") == "led1:A,led1:C,r1:1,r1:2", "parts plugged into breadboard: " + Js("[...masar.sciTest().Sci.ardu.circuit.topo.plugged].join(',')"));
        Check(Until(() => Lcd().StartsWith("A0=512") && Lcd().Substring(16).StartsWith("UP"), 20_000), "lcd shows A0=512 / UP: [" + Lcd() + "]");
        string led = Js("String(masar.sciTest().Sci.ardu.circuit.parts.get('led1').w.brightness)");
        Check(double.TryParse(led, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double b1) && b1 > 0.4 && b1 < 0.85, "led dimmed by PWM: " + led);
        Check(Js("String(masar.sciTest().Sci.ardu.circuit.warn.length)") == "0", "no warnings: " + Js("JSON.stringify(masar.sciTest().Sci.ardu.circuit.warn)"));
        Js("masar.sciTest().Sci.ardu.circuit.parts.get('btn1').w.dispatchEvent(new Event('button-press'));'ok'");
        Check(Until(() => Lcd().Substring(16).StartsWith("DOWN"), 10_000), "button pressed → DOWN: [" + Lcd() + "]");
        Js("masar.sciTest().Sci.ardu.circuit.parts.get('btn1').w.dispatchEvent(new Event('button-release'));(()=>{const p=masar.sciTest().Sci.ardu.circuit.parts.get('pot1');p.w.value=1023;p.w.dispatchEvent(new InputEvent('input'));return 'ok'})()");
        Check(Until(() => Lcd().StartsWith("A0=1023") && Js("String(masar.sciTest().Sci.ardu.circuit.parts.get('led1').w.brightness>0.95)") == "true", 10_000), "pot → 1023: [" + Lcd() + "]");
        SaveSmokeShot(this, "arduino-circuit");

        // 2) سيرفو + جرس + حساس مسافة
        Run(SmokeArduServo, """
            await C.addPart('servo',{id:'sv1',x:330,y:20},true);await C.addPart('buzzer',{id:'bz1',x:330,y:150},true);await C.addPart('hc-sr04',{id:'us1',x:-20,y:-150},true);
            W('sv1:GND','uno:GND.1');W('sv1:V+','uno:5V');W('sv1:PWM','uno:9');W('bz1:2','uno:8');W('bz1:1','uno:GND.3');
            W('us1:VCC','uno:5V');W('us1:GND','uno:GND.2');W('us1:TRIG','uno:12');W('us1:ECHO','uno:11');C.setCtl('us1',120)
            """, "servo circuit");
        Check(Until(() => Last().StartsWith("D=11") || Last().StartsWith("D=12"), 20_000), "distance 120 cm: " + Last());
        Check(Until(() => Js("String(Math.abs(masar.sciTest().Sci.ardu.circuit.parts.get('sv1').w.angle-120)<4)") == "true", 10_000), "servo at 120°: " + Js("String(masar.sciTest().Sci.ardu.circuit.parts.get('sv1').w.angle)"));
        Check(Until(() => Js("String(masar.sciTest().Sci.ardu.circuit.parts.get('bz1').w.hasSignal)") == "true", 10_000), "buzzer sounds");
        Js("masar.sciTest().Sci.ardu.circuit.setCtl('us1',40);'ok'");
        Check(Until(() => Last() is "D=39" or "D=40" or "D=41", 10_000), "distance 40 cm: " + Last());

        // 3) حساسات + مفتاح + RGB (بدون مقاومة ← تحذير)
        Run(SmokeArduSensors, """
            await C.addPart('photoresistor-sensor',{id:'ldr1',x:-200,y:-90},true);await C.addPart('ntc-temperature-sensor',{id:'ntc1',x:-200,y:20},true);
            await C.addPart('pir-motion-sensor',{id:'pir1',x:330,y:-60},true);await C.addPart('slide-switch',{id:'sw1',x:330,y:80},true);await C.addPart('rgb-led',{id:'rgb1',x:330,y:170},true);
            W('ldr1:VCC','uno:5V');W('ldr1:GND','uno:GND.2');W('ldr1:AO','uno:A0');W('ntc1:VCC','uno:5V');W('ntc1:GND','uno:GND.2');W('ntc1:OUT','uno:A1');
            W('pir1:VCC','uno:5V');W('pir1:GND','uno:GND.1');W('pir1:OUT','uno:7');W('sw1:1','uno:GND.1');W('sw1:2','uno:4');W('sw1:3','uno:5V');
            W('rgb1:R','uno:3');W('rgb1:G','uno:5');W('rgb1:B','uno:6');W('rgb1:COM','uno:GND.3');C.setCtl('ntc1',40);C.setCtl('ldr1',20)
            """, "sensors circuit");
        Check(Until(() => Last().Contains("T=40") && Last().EndsWith("M=0 S=0"), 20_000), "temp 40 / no motion / switch off: " + Last());
        int L1 = int.Parse(System.Text.RegularExpressions.Regex.Match(Last(), @"L=(\d+)").Groups[1].Value);
        Js("(()=>{const C=masar.sciTest().Sci.ardu.circuit;C.setCtl('ldr1',800);C.motion('pir1');const s=C.parts.get('sw1');s.w.value=1;s.w.dispatchEvent(new InputEvent('input'));return 'ok'})()");
        Check(Until(() => Last().EndsWith("M=1 S=1"), 10_000), "motion + switch on: " + Last());
        int L2 = int.Parse(System.Text.RegularExpressions.Regex.Match(Last(), @"L=(\d+)").Groups[1].Value);
        Check(L2 < L1 - 200, $"more light → lower LDR reading ({L1} → {L2})");
        Check(Until(() => Js("(()=>{const w=masar.sciTest().Sci.ardu.circuit.parts.get('rgb1').w;return w.ledRed>0.9&&w.ledGreen>0.9&&w.ledBlue<0.1})()") == "true", 5000), "rgb red+green");
        SaveSmokeShot(this, "arduino-sensors");

        // التحذير: ليد بدون مقاومة
        Js("(async()=>{const A=masar.sciTest().Sci.ardu;A.stop(true);const C=A.circuit;await C.addPart('led',{id:'led9',x:0,y:0},true);C.topology();C.moveTo('led9','A',...C.holeAt('uno','5'));C.changed(false);A.start(A.hex)})();'ok'");
        Check(Until(() => Js("String(masar.sciTest().Sci.ardu.circuit.warn.some(w=>w.includes('بدون مقاومة')))") == "true", 10_000), "warning: LED without resistor: " + Js("JSON.stringify(masar.sciTest().Sci.ardu.circuit.warn)"));
        Js("masar.sciTest().Sci.ardu.stop(true);masar.sciTest().Sci.apps.close();'ok'");
        Console.WriteLine("SMOKE arduino circuit: ok");
        RunArduinoBlocksChecks(Js, Until);
    }

    /// <summary>
    /// محاكي أردوينو (المرحلة الثالثة): البرمجة بالكتل العربية — برنامج بمتغير وشرط وقراءة تناظرية وطباعة ←
    /// الكود المولَّد ← ترجمة حقيقية ← تشغيل مع مقاومة متغيرة.
    /// </summary>
    private void RunArduinoBlocksChecks(Func<string, string> Js, Func<Func<bool>, int, bool> Until)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("arduino blocks: " + what); }
        Js("""
            window.__abl=null;(async()=>{try{const A=masar.sciTest().Sci.ardu;
            await A.open({code:'// x',mode:'blocks',circuit:{parts:[{id:'uno',type:'uno',x:0,y:0},{id:'pot1',type:'potentiometer',x:330,y:120}],wires:[{a:'pot1:SIG',b:'uno:A0'},{a:'pot1:VCC',b:'uno:5V'},{a:'pot1:GND',b:'uno:GND.2'}]}});
            const B=window.Blockly,ws=A.blocks.ws;ws.clear();
            const v=ws.getVariableMap().createVariable('قراءة');const V={id:v.getId()};
            B.serialization.blocks.append({type:'ard_program',x:20,y:20,inputs:{
              SETUP:{block:{type:'ard_serial_begin',fields:{BAUD:'9600'},next:{block:{type:'ard_pin_mode',fields:{PIN:'13',MODE:'OUTPUT'}}}}},
              LOOP:{block:{type:'variables_set',fields:{VAR:V},inputs:{VALUE:{block:{type:'ard_analog_read',fields:{PIN:'A0'}}}},
                next:{block:{type:'controls_if',extraState:{hasElse:true},inputs:{
                  IF0:{block:{type:'logic_compare',fields:{OP:'GT'},inputs:{A:{block:{type:'variables_get',fields:{VAR:V}}},B:{block:{type:'math_number',fields:{NUM:500}}}}}},
                  DO0:{block:{type:'ard_digital_write',fields:{PIN:'13',VAL:'HIGH'}}},ELSE:{block:{type:'ard_digital_write',fields:{PIN:'13',VAL:'LOW'}}}},
                next:{block:{type:'ard_serial_print',fields:{NL:'println'},inputs:{V:{block:{type:'variables_get',fields:{VAR:V}}}},
                next:{block:{type:'ard_delay',inputs:{MS:{block:{type:'math_number',fields:{NUM:100}}}}}}}}}}}}}},ws);
            await new Promise(r=>setTimeout(r,300));
            const code=A.blocks.code();
            window.__abl={code,cats:document.querySelectorAll('.ard-blk .blocklyToolboxCategory, .ard-blk .blocklyTreeRow').length,preview:document.querySelector('.ard-blkcode').textContent.trim()===code.trim()};
            }catch(e){window.__abl={err:String(e&&e.message||e)}}})();'ok'
            """);
        Check(Until(() => Js("String(!!window.__abl)") == "true", 20_000), "blocks workspace ready");
        string code = Js("String(window.__abl.code||window.__abl.err)");
        Console.WriteLine("SMOKE arduino blocks code: " + code.Replace("\n", " | "));
        Check(code.Contains("Serial.begin(9600);") && code.Contains("= analogRead(A0);") && System.Text.RegularExpressions.Regex.IsMatch(code, @"if \(\w+ > 500\) \{") && code.Contains("digitalWrite(13, LOW);") && code.Contains("Serial.println("), "generated code: " + code);
        Check(Js("String(window.__abl.cats===9&&window.__abl.preview)") == "true", "toolbox + preview: " + Js("JSON.stringify(window.__abl)"));
        SaveSmokeShot(this, "arduino-blocks");
        Js("window.__abr=null;masar.sciTest().Sci.ardu.run().then(()=>window.__abr=String(masar.sciTest().Sci.ardu.uno?.running));'ok'");
        Check(Until(() => Js("String(window.__abr)") != "null", 240_000) && Js("String(window.__abr)") == "true", "blocks program compiled and running: " + Js("document.querySelector('.sci-win-msg').textContent"));
        string Last() => Js("(()=>{const l=document.querySelector('.ard-ser').textContent.trim().split('\\n');return l[l.length-1].trim()})()");
        Check(Until(() => Last() == "512" && Js("String(masar.sciTest().Sci.ardu.ui.board.led13)") == "true", 20_000), "pot 512 → LED on: " + Last());
        Js("(()=>{const p=masar.sciTest().Sci.ardu.circuit.parts.get('pot1');p.w.value=100;p.w.dispatchEvent(new InputEvent('input'));return 'ok'})()");
        Check(Until(() => Last() == "100" && Js("String(masar.sciTest().Sci.ardu.ui.board.led13)") == "false", 10_000), "pot 100 → LED off: " + Last());
        Js("masar.sciTest().Sci.ardu.stop(true);masar.sciTest().Sci.ardu.setMode('code');masar.sciTest().Sci.apps.close();'ok'");
        Console.WriteLine("SMOKE arduino blocks: ok");
        RunArduinoProjectChecks(Js, Until);
    }

    /// <summary>
    /// محاكي أردوينو (المرحلة الرابعة): تبويب «حاسب» في الأدوات العلمية + المشاريع العشرة (كلها بتترجم) ←
    /// إشارة المرور تشتغل ← «التقط إلى السبورة» (صورة + المشروع) ← النقر المزدوج يفتح المشروع نفسه.
    /// </summary>
    private void RunArduinoProjectChecks(Func<string, string> Js, Func<Func<bool>, int, bool> Until)
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("arduino projects: " + what); }
        Js("masar.sciTest().Sci.open('comp'); 'ok'");
        Check(Until(() => Js("document.querySelectorAll('.sci-panel .sci-tile[data-ardu]').length+':'+document.querySelectorAll('.sci-panel .sci-tile[data-project]').length") == "2:10", 5000), "comp tab tiles: " + Js("document.querySelectorAll('.sci-panel .sci-tile').length"));
        SaveSmokeShot(this, "arduino-tab");

        // كل المشاريع بتترجم
        Js("""
            window.__apc=null;(async()=>{const A=masar.sciTest().Sci.ardu,r=[];
            for(const p of A.PROJECTS){const c=await A.compile(p.code);r.push(p.id+':'+(c.ok?'ok':(c.error||c.problems.map(x=>x.line+' '+x.orig).join('; '))))}
            window.__apc=r.join(' | ')})();'ok'
            """);
        Check(Until(() => Js("String(window.__apc)") != "null", 300_000), "projects compiled");
        string all = Js("String(window.__apc)");
        Console.WriteLine("SMOKE arduino projects: " + all);
        Check(all.Split(" | ").All(x => x.EndsWith(":ok")), "all projects compile: " + all);

        // إشارة المرور: الدائرة على لوحة التوصيل + التشغيل
        Js("document.querySelector('.sci-tile[data-project=\"traffic\"]').click();'ok'");
        Check(Until(() => Js("String(!!document.querySelector('.ard-win')&&masar.sciTest().Sci.ardu.circuit?.parts.size===8)") == "true", 15_000), "traffic project opened");
        string nets = Js("""
            (()=>{const C=masar.sciTest().Sci.ardu.circuit,t=C.topo,F=(id,pin)=>t.uf.find(C.node(id,pin));
            return [1,2,3].map(i=>[F('r'+i,'2')===F('led'+i,'A'),F('led'+i,'C')===F('uno','GND.2'),F('r'+i,'1')===F('uno',['10','9','8'][i-1])].join(',')).join(' ')})()
            """);
        Check(nets == "true,true,true true,true,true true,true,true", "traffic nets: " + nets);
        Js("window.__apr=null;masar.sciTest().Sci.ardu.run().then(()=>window.__apr=String(masar.sciTest().Sci.ardu.uno?.running));'ok'");
        Check(Until(() => Js("String(window.__apr)") == "true", 240_000), "traffic running: " + Js("document.querySelector('.sci-win-msg').textContent"));
        Check(Until(() => Js("(()=>{const C=masar.sciTest().Sci.ardu.circuit;return [1,2,3].map(i=>C.parts.get('led'+i).w.value?1:0).join('')})()") == "100" && Js("String(document.querySelector('.ard-ser').textContent.includes('قف'))") == "true", 20_000), "red light first");
        Check(Until(() => Js("(()=>{const C=masar.sciTest().Sci.ardu.circuit;return [1,2,3].map(i=>C.parts.get('led'+i).w.value?1:0).join('')})()") == "001", 20_000), "then green");
        SaveSmokeShot(this, "arduino-traffic");

        // التقط إلى السبورة ← عنصر فيه الصورة والمشروع ← النقر المزدوج يفتحه
        Js("""
            window.__acp=null;(async()=>{const S=masar.sciTest().Sci,I=masar.inkTest().Ink,n0=I.count();
            await S.ardu.capture();await new Promise(r=>setTimeout(r,1500));
            const key=S.board.curPage().id,it=I.doc.items(key).filter(i=>i.sci&&i.sci.app==='arduino').at(-1);
            window.__sci.ids.push(it.id);
            window.__acp={n:I.count()-n0,closed:!document.querySelector('.ard-win'),svg:it.img.startsWith('data:image/svg+xml'),title:it.sci.title,parts:it.sci.circuit.parts.length,code:it.sci.code.includes('light(RED'),w:it.w>300};
            S.editItem({key},it);await new Promise(r=>setTimeout(r,1200));
            window.__acp.reopen=!!document.querySelector('.ard-win')&&S.ardu.circuit.parts.size===8&&S.ardu.ui.ta.value.includes('light(RED')&&document.querySelector('.ard-win .sci-win-act').textContent.includes('حدّث')})();'ok'
            """);
        Check(Until(() => Js("String(!!window.__acp&&'reopen' in window.__acp)") == "true", 20_000), "capture finished");
        Check(Js("JSON.stringify(window.__acp)") == "{\"n\":1,\"closed\":true,\"svg\":true,\"title\":\"إشارة المرور\",\"parts\":8,\"code\":true,\"w\":true,\"reopen\":true}", "capture/reopen: " + Js("JSON.stringify(window.__acp)"));
        SaveSmokeShot(this, "arduino-reopen");
        Js("masar.sciTest().Sci.apps.close();'ok'");
        Console.WriteLine("SMOKE arduino projects: ok");
    }
}
