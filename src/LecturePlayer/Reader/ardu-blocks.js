// البرمجة بالكتل لأردوينو (Blockly، Apache-2.0): كتل عربية ← كود أردوينو (C++) بيتعرض جنبها ويتشغّل في المحاكي.
// كل حاجة محلية (lib/sci/blockly): المكتبة + الرسائل العربية + الصور. مفيش أي اتصال بالنت.
const BASE = "lib/sci/blockly/";
const loadScript = src => new Promise((res, rej) => {
  const s = document.createElement("script");
  s.src = src; s.onload = res; s.onerror = () => rej(new Error("تعذّر تحميل البرمجة بالكتل"));
  document.head.appendChild(s);
});
let ready = null;
/** تحميل Blockly مرة واحدة + تعريف الكتل والمولّد */
export function loadBlockly() {
  return ready ||= (async () => {
    if (!window.Blockly) await loadScript(BASE + "blockly_compressed.js");
    await loadScript(BASE + "blocks_compressed.js");
    await loadScript(BASE + "ar.js");
    define(window.Blockly);
    return window.Blockly;
  })().catch(e => { ready = null; throw e; });
}

const PINS = [...Array.from({ length: 14 }, (_, i) => [String(i), String(i)]), ...["A0", "A1", "A2", "A3", "A4", "A5"].map(a => [a, a])];
const APINS = ["A0", "A1", "A2", "A3", "A4", "A5"].map(a => [a, a]);
const PWM = ["3", "5", "6", "9", "10", "11"].map(a => [a, a]);
const C = { io: 200, time: 120, serial: 160, dev: 20, prog: 45 };

// ---------------------------------------------------------------- الكتل
function define(B) {
  if (B.Blocks.ard_program) return;
  B.common.defineBlocks(B.common.createBlockDefinitionsFromJsonArray([
    { type: "ard_program", message0: "عند بدء التشغيل %1 %2", args0: [{ type: "input_dummy" }, { type: "input_statement", name: "SETUP" }],
      message1: "كرّر دائمًا %1 %2", args1: [{ type: "input_dummy" }, { type: "input_statement", name: "LOOP" }],
      colour: C.prog, tooltip: "أوامر «عند بدء التشغيل» تُنفَّذ مرة واحدة (setup)، وأوامر «كرّر دائمًا» تتكرر بلا توقف (loop)." },
    { type: "ard_pin_mode", message0: "اجعل الطرف %1 %2", args0: [{ type: "field_dropdown", name: "PIN", options: PINS },
      { type: "field_dropdown", name: "MODE", options: [["مخرجًا", "OUTPUT"], ["مدخلًا", "INPUT"], ["مدخلًا بمقاومة سحب داخلية", "INPUT_PULLUP"]] }],
      previousStatement: null, nextStatement: null, colour: C.io, tooltip: "pinMode: تحديد اتجاه الطرف" },
    { type: "ard_digital_write", message0: "اكتب على الطرف %1 القيمة %2", args0: [{ type: "field_dropdown", name: "PIN", options: PINS },
      { type: "field_dropdown", name: "VAL", options: [["مرتفع (HIGH)", "HIGH"], ["منخفض (LOW)", "LOW"]] }],
      previousStatement: null, nextStatement: null, colour: C.io, tooltip: "digitalWrite: تشغيل أو إطفاء طرف رقمي" },
    { type: "ard_digital_write_v", message0: "اكتب على الطرف %1 القيمة %2", args0: [{ type: "field_dropdown", name: "PIN", options: PINS }, { type: "input_value", name: "VAL" }],
      inputsInline: true, previousStatement: null, nextStatement: null, colour: C.io, tooltip: "digitalWrite بقيمة محسوبة (0 أو 1)" },
    { type: "ard_digital_read", message0: "قراءة الطرف الرقمي %1", args0: [{ type: "field_dropdown", name: "PIN", options: PINS }], output: null, colour: C.io, tooltip: "digitalRead: يعطي 1 (مرتفع) أو 0 (منخفض)" },
    { type: "ard_analog_read", message0: "قراءة الطرف التناظري %1", args0: [{ type: "field_dropdown", name: "PIN", options: APINS }], output: "Number", colour: C.io, tooltip: "analogRead: قيمة من 0 إلى 1023" },
    { type: "ard_analog_write", message0: "اكتب PWM على الطرف %1 القيمة %2", args0: [{ type: "field_dropdown", name: "PIN", options: PWM }, { type: "input_value", name: "VAL", check: "Number" }],
      inputsInline: true, previousStatement: null, nextStatement: null, colour: C.io, tooltip: "analogWrite: قيمة من 0 إلى 255 (الأطراف 3 و 5 و 6 و 9 و 10 و 11)" },
    { type: "ard_highlow", message0: "%1", args0: [{ type: "field_dropdown", name: "VAL", options: [["مرتفع", "HIGH"], ["منخفض", "LOW"]] }], output: null, colour: C.io },
    { type: "ard_tone", message0: "شغّل نغمة على الطرف %1 بتردد %2 هرتز لمدة %3 مللي ثانية", args0: [{ type: "field_dropdown", name: "PIN", options: PINS }, { type: "input_value", name: "HZ", check: "Number" }, { type: "input_value", name: "MS", check: "Number" }],
      inputsInline: true, previousStatement: null, nextStatement: null, colour: C.io, tooltip: "tone: صوت على جرس (بازر)" },
    { type: "ard_notone", message0: "أوقف النغمة على الطرف %1", args0: [{ type: "field_dropdown", name: "PIN", options: PINS }], previousStatement: null, nextStatement: null, colour: C.io },
    { type: "ard_delay", message0: "انتظر %1 مللي ثانية", args0: [{ type: "input_value", name: "MS", check: "Number" }], inputsInline: true, previousStatement: null, nextStatement: null, colour: C.time, tooltip: "delay: 1000 مللي ثانية = ثانية واحدة" },
    { type: "ard_millis", message0: "الزمن منذ التشغيل (مللي ثانية)", output: "Number", colour: C.time, tooltip: "millis" },
    { type: "ard_serial_begin", message0: "ابدأ الشاشة التسلسلية بسرعة %1", args0: [{ type: "field_dropdown", name: "BAUD", options: [["9600", "9600"], ["115200", "115200"]] }], previousStatement: null, nextStatement: null, colour: C.serial },
    { type: "ard_serial_print", message0: "اطبع على الشاشة التسلسلية %1 %2", args0: [{ type: "input_value", name: "V" }, { type: "field_dropdown", name: "NL", options: [["ثم سطر جديد", "println"], ["على نفس السطر", "print"]] }],
      inputsInline: true, previousStatement: null, nextStatement: null, colour: C.serial, tooltip: "Serial.println / Serial.print" },
    { type: "ard_serial_available", message0: "وصل نص من الشاشة التسلسلية", output: "Boolean", colour: C.serial, tooltip: "Serial.available() > 0" },
    { type: "ard_serial_read", message0: "اقرأ رقمًا من الشاشة التسلسلية", output: "Number", colour: C.serial, tooltip: "Serial.parseInt()" },
    { type: "ard_map", message0: "حوّل %1 من %2 – %3 إلى %4 – %5", args0: ["V", "A", "B", "C", "D"].map(n => ({ type: "input_value", name: n, check: "Number" })), inputsInline: true, output: "Number", colour: 230, tooltip: "map: تحويل قيمة من مدى إلى آخر" },
    { type: "ard_servo", message0: "حرّك السيرفو على الطرف %1 إلى الزاوية %2", args0: [{ type: "field_dropdown", name: "PIN", options: PINS }, { type: "input_value", name: "A", check: "Number" }],
      inputsInline: true, previousStatement: null, nextStatement: null, colour: C.dev, tooltip: "مكتبة Servo: زاوية من 0 إلى 180" },
    { type: "ard_lcd_print", message0: "اكتب على شاشة LCD في العمود %1 السطر %2 : %3", args0: [{ type: "field_number", name: "COL", value: 0, min: 0, max: 15, precision: 1 }, { type: "field_dropdown", name: "ROW", options: [["الأول", "0"], ["الثاني", "1"]] }, { type: "input_value", name: "V" }],
      inputsInline: true, previousStatement: null, nextStatement: null, colour: C.dev, tooltip: "شاشة LCD موصّلة على الأطراف RS=12 و E=11 و D4..D7 = 5 و 4 و 3 و 2" },
    { type: "ard_lcd_clear", message0: "امسح شاشة LCD", previousStatement: null, nextStatement: null, colour: C.dev },
    { type: "ard_ultrasonic", message0: "المسافة بالسنتيمتر من حساس المسافة: TRIG %1 ECHO %2", args0: [{ type: "field_dropdown", name: "T", options: PINS }, { type: "field_dropdown", name: "E", options: PINS }], output: "Number", colour: C.dev, tooltip: "حساس المسافة HC-SR04" },
  ]));
}

// ---------------------------------------------------------------- المولّد: كتل ← كود أردوينو
const O = { ATOMIC: 0, CALL: 2, UNARY: 3, MUL: 5, ADD: 6, REL: 8, EQ: 9, AND: 13, OR: 14, NONE: 99 };
const TR = { ا: "a", أ: "a", إ: "i", آ: "a", ب: "b", ت: "t", ث: "th", ج: "j", ح: "h", خ: "kh", د: "d", ذ: "dh", ر: "r", ز: "z", س: "s", ش: "sh", ص: "s", ض: "d", ط: "t", ظ: "z", ع: "a", غ: "gh", ف: "f", ق: "q", ك: "k", ل: "l", م: "m", ن: "n", ه: "h", ة: "a", و: "w", ؤ: "w", ي: "y", ى: "a", ئ: "y", ء: "a" };
const RESERVED = new Set("setup loop int long float char bool byte void if else for while do return break continue switch case default true false HIGH LOW INPUT OUTPUT INPUT_PULLUP delay millis Serial String random map min max abs".split(" "));

export function makeGenerator(B) {
  const G = new B.CodeGenerator("Arduino");
  G.INDENT = "  ";
  const str = s => '"' + String(s).replace(/\\/g, "\\\\").replace(/"/g, '\\"').replace(/\n/g, "\\n") + '"';
  G.init = ws => {
    G.defs = new Map(); G.setups = new Map(); G.vars = new Map(); G.loopN = 0; G.prog = null;
    G.hasBegin = ws.getBlocksByType("ard_serial_begin", false).length > 0;
    const used = new Set();
    for (const v of ws.getVariableMap().getAllVariables()) {
      let n = [...v.getName()].map(ch => TR[ch] ?? (/[A-Za-z0-9_]/.test(ch) ? ch : "_")).join("").replace(/_+/g, "_").replace(/^_|_$/g, "") || "v";
      if (/^\d/.test(n) || RESERVED.has(n)) n = "v_" + n;
      let k = n, i = 2; while (used.has(k)) k = n + i++;
      used.add(k); G.vars.set(v.getId(), k);
    }
  };
  G.vname = (block, field = "VAR") => G.vars.get(block.getFieldValue(field)) || "v";
  G.scrub_ = (block, code, thisOnly) => {
    const next = block.nextConnection?.targetBlock();
    return code + (next && !thisOnly ? G.blockToCode(next) : "");
  };
  G.need = (key, def, setup) => { if (def && !G.defs.has(key)) G.defs.set(key, def); if (setup && !G.setups.has(key)) G.setups.set(key, setup); };
  G.finish = code => {
    const prog = G.prog || { setup: "", loop: "" };
    const lines = [];
    const incs = [...G.defs.entries()].filter(([k]) => k.startsWith("#")).map(([, v]) => v);
    const defs = [...G.defs.entries()].filter(([k]) => !k.startsWith("#")).map(([, v]) => v);
    if (incs.length) lines.push(...incs, "");
    const vars = [...new Set(G.vars.values())];
    if (vars.length) lines.push(...vars.map(v => `int ${v} = 0;`), "");
    if (defs.length) lines.push(...defs, "");
    const setupExtra = [...G.setups.values()].map(s => G.INDENT + s + "\n").join("");
    lines.push("void setup() {\n" + setupExtra + prog.setup + "}", "", "void loop() {\n" + prog.loop + "}");
    return "// كود مولَّد من الكتل\n" + lines.join("\n") + "\n";
  };
  const V = (b, n, o = O.NONE, def = "0") => G.valueToCode(b, n, o) || def;
  const F = G.forBlock;
  F.ard_program = b => { G.prog = { setup: G.statementToCode(b, "SETUP"), loop: G.statementToCode(b, "LOOP") }; return ""; };
  F.ard_pin_mode = b => `pinMode(${b.getFieldValue("PIN")}, ${b.getFieldValue("MODE")});\n`;
  F.ard_digital_write = b => `digitalWrite(${b.getFieldValue("PIN")}, ${b.getFieldValue("VAL")});\n`;
  F.ard_digital_write_v = b => `digitalWrite(${b.getFieldValue("PIN")}, ${V(b, "VAL", O.NONE, "LOW")});\n`;
  F.ard_digital_read = b => [`digitalRead(${b.getFieldValue("PIN")})`, O.CALL];
  F.ard_analog_read = b => [`analogRead(${b.getFieldValue("PIN")})`, O.CALL];
  F.ard_analog_write = b => `analogWrite(${b.getFieldValue("PIN")}, ${V(b, "VAL")});\n`;
  F.ard_highlow = b => [b.getFieldValue("VAL"), O.ATOMIC];
  F.ard_tone = b => `tone(${b.getFieldValue("PIN")}, ${V(b, "HZ", O.NONE, "440")}, ${V(b, "MS", O.NONE, "200")});\n`;
  F.ard_notone = b => `noTone(${b.getFieldValue("PIN")});\n`;
  F.ard_delay = b => `delay(${V(b, "MS", O.NONE, "1000")});\n`;
  F.ard_millis = () => ["millis()", O.CALL];
  F.ard_serial_begin = b => `Serial.begin(${b.getFieldValue("BAUD")});\n`;
  F.ard_serial_print = b => { if (!G.hasBegin) G.need("serial", null, "Serial.begin(9600);"); return `Serial.${b.getFieldValue("NL")}(${V(b, "V", O.NONE, '""')});\n`; };
  F.ard_serial_available = () => ["Serial.available() > 0", O.REL];
  F.ard_serial_read = () => ["Serial.parseInt()", O.CALL];
  F.ard_map = b => [`map(${["V", "A", "B", "C", "D"].map(n => V(b, n)).join(", ")})`, O.CALL];
  F.ard_servo = b => {
    const p = b.getFieldValue("PIN"), n = "servo" + p.replace(/\W/g, "");
    G.need("#servo", "#include <Servo.h>"); G.need("servo" + p, `Servo ${n};`, `${n}.attach(${p});`);
    return `${n}.write(${V(b, "A", O.NONE, "90")});\n`;
  };
  const lcd = () => { G.need("#lcd", "#include <LiquidCrystal.h>"); G.need("lcd", "LiquidCrystal lcd(12, 11, 5, 4, 3, 2);", "lcd.begin(16, 2);"); };
  F.ard_lcd_print = b => { lcd(); return `lcd.setCursor(${b.getFieldValue("COL") | 0}, ${b.getFieldValue("ROW")});\nlcd.print(${V(b, "V", O.NONE, '""')});\n`; };
  F.ard_lcd_clear = () => { lcd(); return "lcd.clear();\n"; };
  F.ard_ultrasonic = b => {
    const t = b.getFieldValue("T"), e = b.getFieldValue("E");
    G.need("distance", "long distanceCm(int trig, int echo) {\n  digitalWrite(trig, LOW); delayMicroseconds(2);\n  digitalWrite(trig, HIGH); delayMicroseconds(10);\n  digitalWrite(trig, LOW);\n  return pulseIn(echo, HIGH, 30000) / 58;\n}");
    G.need("us" + t + "_" + e, null, `pinMode(${t}, OUTPUT); pinMode(${e}, INPUT);`);
    return [`distanceCm(${t}, ${e})`, O.CALL];
  };
  // الكتل الجاهزة في Blockly
  F.controls_if = b => {
    let n = 0, code = "";
    do {
      code += (n ? " else if (" : "if (") + V(b, "IF" + n, O.NONE, "false") + ") {\n" + G.statementToCode(b, "DO" + n) + "}";
      n++;
    } while (b.getInput("IF" + n));
    if (b.getInput("ELSE")) code += " else {\n" + G.statementToCode(b, "ELSE") + "}";
    return code + "\n";
  };
  F.logic_compare = b => {
    const op = { EQ: "==", NEQ: "!=", LT: "<", LTE: "<=", GT: ">", GTE: ">=" }[b.getFieldValue("OP")], o = op === "==" || op === "!=" ? O.EQ : O.REL;
    return [`${V(b, "A", o)} ${op} ${V(b, "B", o)}`, o];
  };
  F.logic_operation = b => { const and = b.getFieldValue("OP") === "AND", o = and ? O.AND : O.OR; return [`${V(b, "A", o, "false")} ${and ? "&&" : "||"} ${V(b, "B", o, "false")}`, o]; };
  F.logic_negate = b => ["!" + V(b, "BOOL", O.UNARY, "true"), O.UNARY];
  F.logic_boolean = b => [b.getFieldValue("BOOL") === "TRUE" ? "true" : "false", O.ATOMIC];
  F.controls_repeat_ext = b => { const i = "i" + (++G.loopN); return `for (int ${i} = 0; ${i} < ${V(b, "TIMES", O.REL, "10")}; ${i}++) {\n${G.statementToCode(b, "DO")}}\n`; };
  F.controls_repeat = b => { const i = "i" + (++G.loopN); return `for (int ${i} = 0; ${i} < ${b.getFieldValue("TIMES")}; ${i}++) {\n${G.statementToCode(b, "DO")}}\n`; };
  F.controls_whileUntil = b => { let c = V(b, "BOOL", O.NONE, "false"); if (b.getFieldValue("MODE") === "UNTIL") c = "!(" + c + ")"; return `while (${c}) {\n${G.statementToCode(b, "DO")}}\n`; };
  F.controls_for = b => { const v = G.vname(b); return `for (${v} = ${V(b, "FROM")}; ${v} <= ${V(b, "TO", O.REL)}; ${v} += ${V(b, "BY", O.ADD, "1")}) {\n${G.statementToCode(b, "DO")}}\n`; };
  F.controls_flow_statements = b => b.getFieldValue("FLOW") === "BREAK" ? "break;\n" : "continue;\n";
  F.math_number = b => { const n = Number(b.getFieldValue("NUM")); return [String(n), n < 0 ? O.UNARY : O.ATOMIC]; };
  F.math_arithmetic = b => {
    const op = b.getFieldValue("OP");
    if (op === "POWER") return [`pow(${V(b, "A")}, ${V(b, "B")})`, O.CALL];
    const [s, o] = { ADD: ["+", O.ADD], MINUS: ["-", O.ADD], MULTIPLY: ["*", O.MUL], DIVIDE: ["/", O.MUL] }[op];
    return [`${V(b, "A", o)} ${s} ${V(b, "B", o + 1)}`, o];
  };
  F.math_modulo = b => [`${V(b, "DIVIDEND", O.MUL)} % ${V(b, "DIVISOR", O.MUL + 1, "1")}`, O.MUL];
  F.math_random_int = b => [`random(${V(b, "FROM")}, ${V(b, "TO", O.ADD)} + 1)`, O.CALL];
  F.text = b => [str(b.getFieldValue("TEXT")), O.ATOMIC];
  F.variables_get = b => [G.vname(b), O.ATOMIC];
  F.variables_set = b => `${G.vname(b)} = ${V(b, "VALUE")};\n`;
  F.math_change = b => `${G.vname(b)} += ${V(b, "DELTA", O.ADD, "1")};\n`;
  return G;
}

// ---------------------------------------------------------------- قائمة الكتل
const num = n => ({ type: "math_number", fields: { NUM: n } });
const sh = (name, b) => ({ [name]: { shadow: b } });
export const TOOLBOX = {
  kind: "categoryToolbox",
  contents: [
    { kind: "category", name: "المداخل والمخارج", colour: C.io, contents: [
      { kind: "block", type: "ard_pin_mode" }, { kind: "block", type: "ard_digital_write" }, { kind: "block", type: "ard_digital_write_v", inputs: sh("VAL", { type: "ard_highlow" }) },
      { kind: "block", type: "ard_digital_read" }, { kind: "block", type: "ard_analog_read" },
      { kind: "block", type: "ard_analog_write", inputs: sh("VAL", num(128)) }, { kind: "block", type: "ard_highlow" },
      { kind: "block", type: "ard_tone", inputs: { ...sh("HZ", num(440)), ...sh("MS", num(200)) } }, { kind: "block", type: "ard_notone" }] },
    { kind: "category", name: "الوقت", colour: C.time, contents: [{ kind: "block", type: "ard_delay", inputs: sh("MS", num(1000)) }, { kind: "block", type: "ard_millis" }] },
    { kind: "category", name: "الشاشة التسلسلية", colour: C.serial, contents: [
      { kind: "block", type: "ard_serial_begin" }, { kind: "block", type: "ard_serial_print", inputs: sh("V", { type: "text", fields: { TEXT: "مرحبًا" } }) },
      { kind: "block", type: "ard_serial_available" }, { kind: "block", type: "ard_serial_read" }] },
    { kind: "category", name: "المنطق", categorystyle: "logic_category", contents: [
      { kind: "block", type: "controls_if" }, { kind: "block", type: "controls_if", extraState: { hasElse: true } },
      { kind: "block", type: "logic_compare" }, { kind: "block", type: "logic_operation" }, { kind: "block", type: "logic_negate" }, { kind: "block", type: "logic_boolean" }] },
    { kind: "category", name: "التكرار", categorystyle: "loop_category", contents: [
      { kind: "block", type: "controls_repeat_ext", inputs: sh("TIMES", num(10)) }, { kind: "block", type: "controls_whileUntil" },
      { kind: "block", type: "controls_for", inputs: { ...sh("FROM", num(1)), ...sh("TO", num(10)), ...sh("BY", num(1)) } }, { kind: "block", type: "controls_flow_statements" }] },
    { kind: "category", name: "الرياضيات", categorystyle: "math_category", contents: [
      { kind: "block", type: "math_number" }, { kind: "block", type: "math_arithmetic", inputs: { ...sh("A", num(1)), ...sh("B", num(1)) } },
      { kind: "block", type: "math_modulo", inputs: { ...sh("DIVIDEND", num(10)), ...sh("DIVISOR", num(3)) } },
      { kind: "block", type: "math_random_int", inputs: { ...sh("FROM", num(1)), ...sh("TO", num(100)) } },
      { kind: "block", type: "ard_map", inputs: { ...sh("V", { type: "ard_analog_read" }), ...sh("A", num(0)), ...sh("B", num(1023)), ...sh("C", num(0)), ...sh("D", num(255)) } }] },
    { kind: "category", name: "النصوص", categorystyle: "text_category", contents: [{ kind: "block", type: "text" }] },
    { kind: "category", name: "المتغيرات", categorystyle: "variable_category", custom: "VARIABLE" },
    { kind: "category", name: "المحركات والشاشات", colour: C.dev, contents: [
      { kind: "block", type: "ard_servo", inputs: sh("A", num(90)) },
      { kind: "block", type: "ard_lcd_print", inputs: sh("V", { type: "text", fields: { TEXT: "Hello" } }) }, { kind: "block", type: "ard_lcd_clear" },
      { kind: "block", type: "ard_ultrasonic" }] },
  ],
};

/** البرنامج الافتراضي: وميض لمبة الطرف 13 */
export const DEFAULT_BLOCKS = {
  blocks: { languageVersion: 0, blocks: [{
    type: "ard_program", x: 20, y: 20, deletable: false,
    inputs: {
      SETUP: { block: { type: "ard_pin_mode", fields: { PIN: "13", MODE: "OUTPUT" } } },
      LOOP: { block: { type: "ard_digital_write", fields: { PIN: "13", VAL: "HIGH" }, next: { block: { type: "ard_delay", inputs: sh("MS", num(500)),
        next: { block: { type: "ard_digital_write", fields: { PIN: "13", VAL: "LOW" }, next: { block: { type: "ard_delay", inputs: sh("MS", num(500)) } } } } } } } },
    },
  }] },
};

/** مساحة الكتل جوه عنصر: { ws, code(), save(), load(json) } */
export async function createBlocks(host, state, onChange) {
  const B = await loadBlockly();
  const G = makeGenerator(B);
  const ws = B.inject(host, {
    toolbox: TOOLBOX, rtl: true, renderer: "zelos", media: BASE + "media/", sounds: false, trashcan: true,
    zoom: { controls: true, wheel: true, startScale: 0.72, maxScale: 2, minScale: 0.4 },
    move: { scrollbars: true, drag: true, wheel: false }, grid: { spacing: 24, length: 2, colour: "#e3e6ea", snap: true },
  });
  try { B.serialization.workspaces.load(state && state.blocks ? state : DEFAULT_BLOCKS, ws); }
  catch { B.serialization.workspaces.load(DEFAULT_BLOCKS, ws); }
  if (!ws.getBlocksByType("ard_program").length) B.serialization.blocks.append({ type: "ard_program", x: 20, y: 20, deletable: false }, ws);
  const api = {
    ws,
    code: () => G.workspaceToCode(ws),
    save: () => B.serialization.workspaces.save(ws),
    resize: () => B.svgResize(ws),
    dispose: () => ws.dispose(),
  };
  ws.addChangeListener(e => { if (!e.isUiEvent && e.type !== B.Events.FINISHED_LOADING) onChange?.(api); });
  return api;
}
