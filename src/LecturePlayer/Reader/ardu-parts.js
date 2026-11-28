// قطع محاكي أردوينو: الاسم بالعربي + عنصر الرسم (wokwi-elements، MIT) + سلوكه الكهربي المبسّط.
// المحاكاة رقمية مبسّطة: كل شبكة (نقطة توصيل) قيمتها من 0 لـ 1 (نسبة من 5 فولت، أو نسبة تشغيل PWM)،
// والمقاومات بتوصّل القيمة «بضعف» (سحب لأعلى/لأسفل، مقسّم جهد). مفيش احتراق قطع ولا قياس تيار.
export const PITCH = 9.6;   // 0.1 بوصة بالبكسل (خطوة ثقوب لوحة التوصيل وأرجل القطع)

const sq = (n, x0, y0, dx, dy) => Array.from({ length: n }, (_, i) => [x0 + i * dx, y0 + i * dy]);

/**
 * لكل قطعة:
 *  tag: عنصر wokwi (أو null للوحة التوصيل المرسومة هنا) — name — props الافتراضية
 *  short: أطراف موصّلة جوه القطعة دايمًا — sw(st): أطراف بتتوصّل حسب الحالة (زر/مفتاح)
 *  res: مقاومات [طرف، طرف، أوم] — diodes: [موجب، سالب] — power: هل محتاجة VCC/GND
 *  out(st, V): مصادر القطعة {طرف: قيمة 0..1} بعد ما نعرف قيم VCC/GND
 */
export const PARTS = {
  uno: {
    tag: "wokwi-arduino-uno", name: "لوحة أردوينو أونو", fixed: true,
    short: [["GND.1", "GND.2", "GND.3"], ["A4", "A4.2"], ["A5", "A5.2"]],
  },
  breadboard: { tag: null, name: "لوحة توصيل (بريدبورد)" },
  led: {
    tag: "wokwi-led", name: "ليد (LED)", props: { color: "red" },
    opts: { color: ["اللون", [["red", "أحمر"], ["green", "أخضر"], ["blue", "أزرق"], ["yellow", "أصفر"], ["orange", "برتقالي"], ["white", "أبيض"]]] },
    diodes: [["A", "C"]], pinAr: { A: "الموجب (الرجل الطويلة)", C: "السالب (الرجل القصيرة)" },
  },
  "rgb-led": {
    tag: "wokwi-rgb-led", name: "ليد ثلاثي الألوان (RGB)", props: { common: "cathode" },
    opts: { common: ["الطرف المشترك", [["cathode", "سالب مشترك (كاثود)"], ["anode", "موجب مشترك (أنود)"]]] },
    pinAr: { R: "الأحمر", G: "الأخضر", B: "الأزرق", COM: "المشترك" },
  },
  resistor: {
    tag: "wokwi-resistor", name: "مقاومة", props: { value: "220" },
    opts: { value: ["القيمة", [["100", "100 Ω"], ["220", "220 Ω"], ["330", "330 Ω"], ["1000", "1 kΩ"], ["4700", "4.7 kΩ"], ["10000", "10 kΩ"], ["100000", "100 kΩ"]]] },
    res: st => [["1", "2", +st.value || 220]],
  },
  pushbutton: {
    tag: "wokwi-pushbutton", name: "زر ضغط", props: { color: "red" }, input: true,
    opts: { color: ["اللون", [["red", "أحمر"], ["green", "أخضر"], ["blue", "أزرق"], ["yellow", "أصفر"], ["black", "أسود"], ["white", "أبيض"]]] },
    short: [["1.l", "1.r"], ["2.l", "2.r"]], sw: st => st.pressed ? [["1.l", "2.l"]] : [],
  },
  "slide-switch": {
    tag: "wokwi-slide-switch", name: "مفتاح انزلاقي", props: {}, input: true,
    sw: st => st.value ? [["2", "3"]] : [["1", "2"]],
  },
  potentiometer: {
    tag: "wokwi-potentiometer", name: "مقاومة متغيرة (بوتنشيوميتر)", props: {}, input: true,
    out: (st, V) => V.GND != null && V.VCC != null ? { SIG: V.GND + (V.VCC - V.GND) * (st.value ?? 512) / 1023 } : {},
  },
  "photoresistor-sensor": {
    tag: "wokwi-photoresistor-sensor", name: "حساس ضوء (LDR)", props: {}, power: true,
    ctl: { k: "lux", label: "شدة الضوء", min: 1, max: 1000, step: 1, def: 300, unit: "لوكس", log: true },
    out: st => { const r = 50000 * Math.pow(10 / Math.max(0.1, st.lux ?? 300), 0.7), v = r / (r + 10000); return { AO: v, DO: v > 0.5 ? 1 : 0 }; },
  },
  "ntc-temperature-sensor": {
    tag: "wokwi-ntc-temperature-sensor", name: "حساس حرارة (NTC)", props: {}, power: true,
    ctl: { k: "temp", label: "درجة الحرارة", min: -20, max: 80, step: 1, def: 25, unit: "°س" },
    out: st => { const t = (st.temp ?? 25) + 273.15, r = 10000 * Math.exp(3950 * (1 / t - 1 / 298.15)); return { OUT: r / (r + 10000) }; },
  },
  buzzer: { tag: "wokwi-buzzer", name: "جرس (بازر)", props: {}, pinAr: { 1: "السالب", 2: "الموجب" } },
  servo: { tag: "wokwi-servo", name: "محرك مؤازر (سيرفو)", props: {}, pinAr: { "V+": "5V", PWM: "الإشارة" } },
  "7segment": {
    tag: "wokwi-7segment", name: "شاشة سبع قطع", props: { color: "red", common: "cathode" },
    opts: { common: ["الطرف المشترك", [["cathode", "سالب مشترك (كاثود)"], ["anode", "موجب مشترك (أنود)"]]],
      color: ["اللون", [["red", "أحمر"], ["green", "أخضر"], ["blue", "أزرق"], ["yellow", "أصفر"], ["white", "أبيض"]]] },
    short: [["COM.1", "COM.2"]],
  },
  lcd1602: { tag: "wokwi-lcd1602", name: "شاشة LCD ‏16×2", props: {} },
  "hc-sr04": {
    tag: "wokwi-hc-sr04", name: "حساس مسافة (HC-SR04)", props: {}, power: true,
    ctl: { k: "cm", label: "المسافة", min: 2, max: 400, step: 1, def: 100, unit: "سم" },
  },
  "pir-motion-sensor": {
    tag: "wokwi-pir-motion-sensor", name: "حساس حركة (PIR)", props: {}, power: true,
    ctl: { k: "motion", label: "حركة أمام الحساس", button: true },
    out: (st, V, now) => ({ OUT: now < (st.until || 0) ? 1 : 0 }),
  },
};
export const PALETTE = ["breadboard", "led", "resistor", "pushbutton", "slide-switch", "potentiometer", "rgb-led", "buzzer", "servo", "7segment", "lcd1602", "photoresistor-sensor", "ntc-temperature-sensor", "hc-sr04", "pir-motion-sensor"];

// ---------------------------------------------------------------- لوحة التوصيل (مرسومة هنا)
// 30 عمود؛ الصفوف a–e فوق و f–j تحت (كل عمود في كل نص متوصل)، وخطّين طاقة فوق وتحت (كل خط متوصل بطوله).
export const BB = { cols: 30, w: 33 * PITCH, h: 18 * PITCH };
const BB_ROWS = { a: 4, b: 5, c: 6, d: 7, e: 8, f: 10, g: 11, h: 12, i: 13, j: 14 };
const BB_RAILS = { "tn": 1, "tp": 2, "bn": 16, "bp": 17 };   // n = سالب (أزرق)، p = موجب (أحمر)
export function bbPins() {
  const pins = [];
  for (let c = 1; c <= BB.cols; c++) {
    const x = (c + 1) * PITCH;
    for (const [r, y] of Object.entries(BB_ROWS)) pins.push({ name: r + c, x, y: y * PITCH });
    for (const [r, y] of Object.entries(BB_RAILS)) pins.push({ name: r + "." + c, x, y: y * PITCH });
  }
  return pins;
}
/** الأطراف المتوصلة جوه لوحة التوصيل */
export function bbShort() {
  const g = [];
  for (let c = 1; c <= BB.cols; c++) { g.push("abcde".split("").map(r => r + c)); g.push("fghij".split("").map(r => r + c)); }
  for (const r of Object.keys(BB_RAILS)) g.push(Array.from({ length: BB.cols }, (_, i) => r + "." + (i + 1)));
  return g;
}
export function bbSvg() {
  const P = PITCH, W = BB.w, H = BB.h;
  let s = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">`;
  s += `<rect x="0.5" y="0.5" width="${W - 1}" height="${H - 1}" rx="6" fill="#f3f1ea" stroke="#d6d2c4"/>`;
  s += `<rect x="4" y="${9 * P - 3}" width="${W - 8}" height="6" rx="2" fill="#e3e0d5"/>`;
  for (const [r, y] of Object.entries(BB_RAILS)) {
    const c = r.endsWith("p") ? "#e03131" : "#1971c2", ly = y * P + (r[0] === "t" ? (r.endsWith("n") ? -6 : 6) : (r.endsWith("n") ? -6 : 6));
    s += `<line x1="${P * 1.4}" y1="${ly}" x2="${W - P * 1.4}" y2="${ly}" stroke="${c}" stroke-width="1.2"/>`;
    s += `<text x="${P * 0.7}" y="${y * P + 3}" font-size="9" font-family="Segoe UI,Arial" fill="${c}" text-anchor="middle">${r.endsWith("p") ? "+" : "−"}</text>`;
  }
  for (let c = 1; c <= BB.cols; c++) {
    const x = (c + 1) * P;
    if (c === 1 || c % 5 === 0) for (const y of [3 * P + 1, 15 * P + 2]) s += `<text x="${x}" y="${y}" font-size="6.5" font-family="Segoe UI,Arial" fill="#868e96" text-anchor="middle">${c}</text>`;
    for (const y of [...Object.values(BB_ROWS), ...Object.values(BB_RAILS)]) s += `<rect x="${x - 1.6}" y="${y * P - 1.6}" width="3.2" height="3.2" rx="0.6" fill="#3d3d3d"/>`;
  }
  for (const [r, y] of Object.entries(BB_ROWS)) s += `<text x="${P * 0.9}" y="${y * P + 2.5}" font-size="6.5" font-family="Segoe UI,Arial" fill="#868e96" text-anchor="middle">${r}</text>`;
  return s + "</svg>";
}

/** اسم الطرف بالعربي للتلميح */
export function pinLabel(type, pin) {
  const ar = PARTS[type]?.pinAr?.[pin];
  if (type === "breadboard") {
    const m = /^([tb])([np])\.(\d+)$/.exec(pin);
    return m ? `خط ${m[2] === "p" ? "الموجب (+)" : "السالب (−)"} ${m[1] === "t" ? "العلوي" : "السفلي"}` : `ثقب ${pin}`;
  }
  return ar ? `${pin} — ${ar}` : pin;
}

/** لون السلك الافتراضي حسب الطرف */
export function wireColor(pin) {
  if (/^GND|^VSS$|^K$|^COM$|[tb]n\.|^C$/.test(pin)) return "#212529";
  if (/^5V$|^VCC$|^VDD$|^V\+$|[tb]p\./.test(pin)) return "#e03131";
  if (/^3\.3V$/.test(pin)) return "#f08c00";
  return null;
}
export const WIRE_COLORS = [["#2f9e44", "أخضر"], ["#1971c2", "أزرق"], ["#f08c00", "برتقالي"], ["#e03131", "أحمر"], ["#212529", "أسود"], ["#f8f9fa", "أبيض"], ["#ae3ec9", "بنفسجي"], ["#fab005", "أصفر"]];
