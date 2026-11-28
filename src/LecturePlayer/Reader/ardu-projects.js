// عشرة مشاريع أردوينو جاهزة للمحاكي (الكود + الدائرة). القطع الصغيرة بتتركّب على لوحة التوصيل
// بـ plug: [قطعة، طرف، ثقب] (بتتحسب أماكنها عند الفتح)، والقطع الكبيرة بأسلاك لأطراف الأردوينو مباشرة.
const W = (a, b, color = "#1971c2") => ({ a, b, color });
const BLK = "#212529", RED = "#e03131", GRN = "#2f9e44", ORG = "#f08c00";
const UNO = { id: "uno", type: "uno", x: 0, y: 0 };
const BB = { id: "bb1", type: "breadboard", x: -24, y: 240 };
/** ليد بمقاومة على لوحة التوصيل: المقاومة من العمود c إلى c+6، والليد (+) في العمود c+6 و(−) في c+5 */
const ledOn = (n, col, color, pin) => ({
  parts: [{ id: "r" + n, type: "resistor", props: { value: "220" } }, { id: "led" + n, type: "led", props: { color } }],
  plug: [["r" + n, "1", "c" + col], ["led" + n, "A", "e" + (col + 6)]],
  wires: [W("uno:" + pin, "bb1:a" + col, ORG), W("bb1:a" + (col + 5), "bb1:tn." + (col + 5), BLK)],
});
const merge = (...xs) => ({ parts: xs.flatMap(x => x.parts || []), plug: xs.flatMap(x => x.plug || []), wires: xs.flatMap(x => x.wires || []) });
const GND_RAIL = { wires: [W("uno:GND.2", "bb1:tn.1", BLK)] };

export const PROJECTS = [
  {
    id: "blink", icon: "💡", name: "وميض ليد", sub: "أول برنامج: تشغيل ليد وإطفاؤه كل نصف ثانية",
    code: `// وميض ليد على الطرف 13 (ومعه اللمبة المدمجة في اللوحة)
void setup() {
  pinMode(13, OUTPUT);
}

void loop() {
  digitalWrite(13, HIGH);  // تشغيل
  delay(500);              // انتظر نصف ثانية
  digitalWrite(13, LOW);   // إطفاء
  delay(500);
}
`,
    c: merge(GND_RAIL, ledOn(1, 8, "red", 13)),
  },
  {
    id: "traffic", icon: "🚦", name: "إشارة المرور", sub: "ثلاثة ليدات: أحمر ثم أصفر ثم أخضر",
    code: `// إشارة مرور: أحمر (10) ← أخضر (8) ← أصفر (9)
const int RED = 10, YELLOW = 9, GREEN = 8;

void setup() {
  pinMode(RED, OUTPUT);
  pinMode(YELLOW, OUTPUT);
  pinMode(GREEN, OUTPUT);
  Serial.begin(9600);
}

void light(int pin, int ms, const char* name) {
  digitalWrite(RED, pin == RED);
  digitalWrite(YELLOW, pin == YELLOW);
  digitalWrite(GREEN, pin == GREEN);
  Serial.println(name);
  delay(ms);
}

void loop() {
  light(RED, 3000, "قف");
  light(GREEN, 3000, "سِر");
  light(YELLOW, 1000, "استعد");
}
`,
    c: merge(GND_RAIL, ledOn(1, 2, "red", 10), ledOn(2, 11, "yellow", 9), ledOn(3, 20, "green", 8)),
  },
  {
    id: "button", icon: "🔘", name: "زر يتحكم في ليد", sub: "اضغط الزر فيضيء الليد (مقاومة سحب داخلية)",
    code: `// الزر على الطرف 2 (مع مقاومة سحب داخلية)، والليد على الطرف 12
void setup() {
  pinMode(2, INPUT_PULLUP);
  pinMode(12, OUTPUT);
  Serial.begin(9600);
}

void loop() {
  bool pressed = digitalRead(2) == LOW;  // مضغوط = متصل بالأرضي
  digitalWrite(12, pressed);
  if (pressed) Serial.println("الزر مضغوط");
  delay(50);
}
`,
    c: merge(GND_RAIL, ledOn(1, 20, "green", 12), {
      parts: [{ id: "btn1", type: "pushbutton", props: { color: "blue" } }],
      plug: [["btn1", "1.l", "e4"]],
      wires: [W("uno:2", "bb1:a4", "#1971c2"), W("bb1:j4", "bb1:bn.4", BLK), W("bb1:tn.30", "bb1:bn.30", BLK)],
    }),
  },
  {
    id: "pot", icon: "🎛", name: "قراءة مقاومة متغيرة", sub: "analogRead: القيمة من 0 إلى 1023 والجهد بالفولت",
    code: `// المقاومة المتغيرة على A0: القيمة والجهد على الشاشة التسلسلية
void setup() {
  Serial.begin(9600);
}

void loop() {
  int value = analogRead(A0);          // من 0 إلى 1023
  float volts = value * 5.0 / 1023.0;  // نحوّلها إلى فولت
  Serial.print("القيمة = ");
  Serial.print(value);
  Serial.print("   الجهد = ");
  Serial.print(volts);
  Serial.println(" فولت");
  delay(300);
}
`,
    c: { parts: [{ id: "pot1", type: "potentiometer", x: 330, y: 110 }], wires: [W("pot1:SIG", "uno:A0", GRN), W("pot1:VCC", "uno:5V", RED), W("pot1:GND", "uno:GND.2", BLK)] },
  },
  {
    id: "fade", icon: "🌗", name: "تلاشي الضوء (PWM)", sub: "analogWrite: الليد يضيء ويخفت تدريجيًا",
    code: `// تلاشي الضوء: analogWrite على الطرف 9 (من أطراف PWM)
void setup() {
  pinMode(9, OUTPUT);
}

void loop() {
  for (int b = 0; b <= 255; b += 5) {  // يزداد
    analogWrite(9, b);
    delay(20);
  }
  for (int b = 255; b >= 0; b -= 5) {  // يقل
    analogWrite(9, b);
    delay(20);
  }
}
`,
    c: merge(GND_RAIL, ledOn(1, 10, "blue", 9)),
  },
  {
    id: "servo", icon: "🦾", name: "محرك سيرفو بمقاومة متغيرة", sub: "map: لفّ المقاومة فيتحرك السيرفو من 0 إلى 180°",
    code: `// السيرفو على الطرف 9 يتبع المقاومة المتغيرة على A0
#include <Servo.h>
Servo motor;

void setup() {
  motor.attach(9);
}

void loop() {
  int value = analogRead(A0);
  int angle = map(value, 0, 1023, 0, 180);
  motor.write(angle);
  delay(15);
}
`,
    c: {
      parts: [{ id: "sv1", type: "servo", x: 340, y: 10 }, { id: "pot1", type: "potentiometer", x: 330, y: 150 }],
      wires: [W("sv1:PWM", "uno:9", ORG), W("sv1:V+", "uno:5V", RED), W("sv1:GND", "uno:GND.1", BLK),
        W("pot1:SIG", "uno:A0", GRN), W("pot1:VCC", "uno:5V", RED), W("pot1:GND", "uno:GND.2", BLK)],
    },
  },
  {
    id: "distance", icon: "📏", name: "حساس المسافة", sub: "HC-SR04: المسافة بالسنتيمتر وإنذار عند الاقتراب",
    code: `// حساس المسافة: TRIG على 12 و ECHO على 11، والإنذار على الليد 13
const int TRIG = 12, ECHO = 11;

void setup() {
  pinMode(TRIG, OUTPUT);
  pinMode(ECHO, INPUT);
  pinMode(13, OUTPUT);
  Serial.begin(9600);
}

void loop() {
  digitalWrite(TRIG, LOW);  delayMicroseconds(2);
  digitalWrite(TRIG, HIGH); delayMicroseconds(10);   // نبضة 10 ميكروثانية
  digitalWrite(TRIG, LOW);
  long us = pulseIn(ECHO, HIGH);                     // زمن رجوع الصدى
  int cm = us / 58;
  Serial.print("المسافة: ");
  Serial.print(cm);
  Serial.println(" سم");
  digitalWrite(13, cm < 20);                         // قريب جدًا؟
  delay(200);
}
`,
    c: { parts: [{ id: "us1", type: "hc-sr04", x: 10, y: -150, props: { cm: 50 } }], wires: [W("us1:VCC", "uno:5V", RED), W("us1:GND", "uno:GND.2", BLK), W("us1:TRIG", "uno:12", ORG), W("us1:ECHO", "uno:11", GRN)] },
  },
  {
    id: "lcd", icon: "📟", name: "شاشة LCD", sub: "LiquidCrystal: رسالة وعدّاد ثوانٍ",
    code: `// شاشة LCD 16×2: RS=12، E=11، D4..D7 = 5، 4، 3، 2
#include <LiquidCrystal.h>
LiquidCrystal lcd(12, 11, 5, 4, 3, 2);

void setup() {
  lcd.begin(16, 2);
  lcd.print("Hello, Masar!");
}

void loop() {
  lcd.setCursor(0, 1);       // العمود 0، السطر الثاني
  lcd.print("Time: ");
  lcd.print(millis() / 1000);
  lcd.print(" s");
  delay(200);
}
`,
    c: {
      parts: [{ id: "lcd1", type: "lcd1602", x: -20, y: -190 }],
      wires: [W("lcd1:VSS", "uno:GND.1", BLK), W("lcd1:VDD", "uno:5V", RED), W("lcd1:RW", "uno:GND.1", BLK), W("lcd1:K", "uno:GND.1", BLK), W("lcd1:A", "uno:5V", RED),
        W("lcd1:RS", "uno:12"), W("lcd1:E", "uno:11"), W("lcd1:D4", "uno:5", GRN), W("lcd1:D5", "uno:4", GRN), W("lcd1:D6", "uno:3", GRN), W("lcd1:D7", "uno:2", GRN)],
    },
  },
  {
    id: "buzzer", icon: "🔔", name: "الجرس ولحن قصير", sub: "tone: نغمات بترددات مختلفة",
    code: `// لحن قصير على الجرس (الطرف 8)
int notes[] = {262, 294, 330, 349, 392, 440, 494, 523};  // دو ري مي فا صول لا سي دو
const char* names[] = {"دو", "ري", "مي", "فا", "صول", "لا", "سي", "دو"};

void setup() {
  Serial.begin(9600);
}

void loop() {
  for (int i = 0; i < 8; i++) {
    Serial.println(names[i]);
    tone(8, notes[i], 300);
    delay(400);
  }
  delay(1000);
}
`,
    c: { parts: [{ id: "bz1", type: "buzzer", x: 340, y: 60 }], wires: [W("bz1:2", "uno:8", "#ae3ec9"), W("bz1:1", "uno:GND.1", BLK)] },
  },
  {
    id: "light", icon: "🌙", name: "حساس الضوء (ضوء ليلي)", sub: "يضيء الليد تلقائيًا عندما يقل الضوء",
    code: `// حساس الضوء على A0: كلما زاد الضوء قلّت القراءة
void setup() {
  pinMode(13, OUTPUT);
  Serial.begin(9600);
}

void loop() {
  int light = analogRead(A0);
  Serial.print("قراءة الضوء: ");
  Serial.println(light);
  if (light > 600) {
    digitalWrite(13, HIGH);   // مظلم ← شغّل الضوء الليلي
  } else {
    digitalWrite(13, LOW);
  }
  delay(200);
}
`,
    c: merge(GND_RAIL, ledOn(1, 8, "yellow", 13), {
      parts: [{ id: "ldr1", type: "photoresistor-sensor", x: 330, y: 40, props: { lux: 300 } }],
      wires: [W("ldr1:VCC", "uno:5V", RED), W("ldr1:GND", "uno:GND.3", BLK), W("ldr1:AO", "uno:A0", GRN)],
    }),
  },
];

/** المشروع ← حالة المحاكي (الدائرة بالأماكن النهائية تتحسب بعد التحميل عن طريق plug) */
export function projectState(p) {
  const usesBB = (p.c.plug || []).length > 0 || (p.c.wires || []).some(w => w.a.startsWith("bb1:") || w.b.startsWith("bb1:"));
  const parts = [UNO, ...(usesBB ? [BB] : []), ...(p.c.parts || []).map(x => ({ x: x.x ?? 0, y: x.y ?? 0, ...x }))];
  return { code: p.code, mode: "code", title: p.name, circuit: { parts, wires: p.c.wires || [] }, plug: p.c.plug || [] };
}
