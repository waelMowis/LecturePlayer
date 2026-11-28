// محاكي لوحة أردوينو أونو (ATmega328P بتردد 16 ميجاهرتز) فوق avr8js (MIT).
// بيشغّل ملف hex الناتج من المترجم ويقرأ/يكتب أطراف اللوحة:
//   الأطراف الرقمية 0–13 (المنفذ D ثم B)، والتناظرية A0–A5 = 14–19 (المنفذ C)،
//   الشاشة التسلسلية (USART0)، المحوّل التناظري (ADC)، والذاكرة الدائمة (EEPROM).
// لكل طرف بنقيس نسبة التشغيل (PWM) وعرض آخر نبضة (للمحرك المؤازر) وعدد التذبذبات (للجرس).
import {
  CPU, avrInstruction, AVRTimer, AVRIOPort, AVRUSART, AVRADC, AVREEPROM, EEPROMMemoryBackend, PinState,
  timer0Config, timer1Config, timer2Config, portBConfig, portCConfig, portDConfig, usart0Config, adcConfig, eepromConfig,
} from "./lib/sci/avr8js/avr8js.mjs";

export const FREQ = 16e6;
const FLASH = 0x8000;

/** Intel HEX ← ذاكرة البرنامج (بايتات). بيرفض الملف لو فيه سطر تالف. */
export function parseHex(text) {
  const out = new Uint8Array(FLASH);
  let base = 0, used = 0;
  for (const raw of String(text).split(/\r?\n/)) {
    const line = raw.trim();
    if (!line) continue;
    if (line[0] !== ":" || line.length < 11) throw new Error("ملف البرنامج تالف");
    const b = []; for (let i = 1; i < line.length; i += 2) b.push(parseInt(line.substr(i, 2), 16));
    const n = b[0], addr = (b[1] << 8) | b[2], type = b[3];
    if (b.length !== n + 5 || (b.reduce((a, x) => a + x, 0) & 0xff) !== 0) throw new Error("ملف البرنامج تالف");
    if (type === 0) {
      for (let i = 0; i < n; i++) { const a = base + addr + i; if (a < FLASH) { out[a] = b[4 + i]; used = Math.max(used, a + 1); } }
    } else if (type === 1) break;
    else if (type === 2) base = ((b[4] << 8) | b[5]) << 4;
    else if (type === 4) base = ((b[4] << 8) | b[5]) << 16;
  }
  if (!used) throw new Error("ملف البرنامج فارغ");
  return out;
}

/** رقم الطرف ← [المنفذ، رقم البت]: 0–7 = D، 8–13 = B، 14–19 (A0–A5) = C */
export const pinPort = p => p < 8 ? [0, p] : p < 14 ? [1, p - 8] : p < 20 ? [2, p - 14] : null;
export const pinName = p => p >= 14 ? "A" + (p - 14) : String(p);

export class Uno {
  constructor(hex) {
    this.program = new Uint16Array(FLASH / 2);
    new Uint8Array(this.program.buffer).set(parseHex(hex));
    const cpu = this.cpu = new CPU(this.program, 2048);
    this.timers = [new AVRTimer(cpu, timer0Config), new AVRTimer(cpu, timer1Config), new AVRTimer(cpu, timer2Config)];
    this.ports = [new AVRIOPort(cpu, portDConfig), new AVRIOPort(cpu, portBConfig), new AVRIOPort(cpu, portCConfig)];
    this.usart = new AVRUSART(cpu, usart0Config, FREQ);
    this.adc = new AVRADC(cpu, adcConfig);
    this.eeprom = new AVREEPROM(cpu, new EEPROMMemoryBackend(1024), eepromConfig);
    for (let i = 0; i < 8; i++) this.adc.channelValues[i] = 0;

    // حالة كل طرف: المستوى + قياسات النافذة الحالية
    this.pin = Array.from({ length: 20 }, () => ({ level: 0, mode: "in", since: 0, high: 0, rises: 0, rise: 0, pulse: 0, pulseAt: -1 }));
    this.win0 = 0;
    this.watchers = new Set();
    this.ports.forEach((port, pi) => port.addListener(() => this._scan(pi)));

    this.onSerial = null;          // (byte) ← بايت خارج من اللوحة
    this.rxQueue = [];
    this.usart.onByteTransmit = b => { this.txAt = performance.now(); this.onSerial?.(b); };
    this.baud = 9600;
    this.usart.onConfigurationChange = () => {
      // UBRR بيدّي سرعة تقريبية (9615 بدل 9600): نعرض أقرب سرعة قياسية
      const b = this.usart.baudRate, std = [300, 1200, 2400, 4800, 9600, 14400, 19200, 28800, 31250, 38400, 57600, 74880, 115200, 230400, 250000, 500000, 1000000, 2000000];
      const n = std.reduce((a, x) => Math.abs(x - b) < Math.abs(a - b) ? x : a, std[0]);
      this.baud = Math.abs(n - b) / n < 0.04 ? n : Math.round(b);
    };

    this.running = false;
    this.simMs = 0;                // الزمن داخل المحاكاة
    this.speed = 1;                // نسبة سرعة المحاكاة للزمن الحقيقي (آخر ثانية)
    this.txAt = 0;
    this._chan = new MessageChannel();
    this._chan.port2.onmessage = () => this._loop();
  }

  _scan(pi) {
    const port = this.ports[pi], c = this.cpu.cycles, base = [0, 8, 14][pi], n = pi === 0 ? 8 : 6;
    let fix = null;
    for (let b = 0; b < n; b++) {
      const st = port.pinState(b), p = this.pin[base + b];
      const mode = st === PinState.High || st === PinState.Low ? "out" : st === PinState.InputPullUp ? "pullup" : "in";
      // avr8js مابيطبّقش مقاومة السحب الداخلية على سجل PIN: الطرف الحر بنقرّاه 1 مع INPUT_PULLUP و 0 من غيرها
      if (mode !== p.mode && mode !== "out" && p.ext === undefined) (fix ||= []).push([b, mode === "pullup"]);
      p.mode = mode;
      const lv = st === PinState.High ? 1 : st === PinState.Low ? 0 : p.mode === "pullup" ? (p.ext ?? 1) : (p.ext ?? 0);
      if (lv === p.level) continue;
      if (p.level) { p.high += c - p.since; p.pulse = (c - p.rise) / (FREQ / 1e6); p.pulseAt = c; }
      else { p.rises++; p.rise = c; }
      p.level = lv; p.since = c;
      for (const w of this.watchers) w(base + b, lv, c);
    }
    if (fix) for (const [b, v] of fix) port.setPin(b, v);
  }

  /** مستوى الطرف الآن (0/1) */
  read(pin) { return this.pin[pin]?.level || 0; }
  mode(pin) { return this.pin[pin]?.mode || "in"; }

  /** دخل رقمي من قطعة خارجية (زر، حساس…). null = الطرف حر (يرجع لمقاومة السحب أو صفر). */
  setDigital(pin, v) {
    const pp = pinPort(pin); if (!pp) return;
    const p = this.pin[pin];
    p.ext = v == null ? undefined : v ? 1 : 0;
    this.ports[pp[0]].setPin(pp[1], v == null ? p.mode === "pullup" : !!v);
    this._scan(pp[0]);
  }
  /** دخل تناظري A0–A5 بالفولت (0–5). */
  setAnalog(ch, volts) { if (ch >= 0 && ch < 6) this.adc.channelValues[ch] = Math.max(0, Math.min(5, +volts || 0)); }

  /** نص للوحة عبر الشاشة التسلسلية (UTF-8) */
  serialWrite(text) { for (const b of new TextEncoder().encode(text)) this.rxQueue.push(b); }

  /** قياسات كل طرف منذ آخر قراءة: نسبة التشغيل (0–1)، التردد بالهرتز، عرض آخر نبضة بالميكروثانية. */
  sample() {
    const c = this.cpu.cycles, dt = c - this.win0, res = [];
    for (let i = 0; i < 20; i++) {
      const p = this.pin[i];
      const high = p.high + (p.level ? c - p.since : 0);
      res.push({ level: p.level, mode: p.mode, duty: dt > 0 ? high / dt : p.level, hz: dt > 0 ? p.rises * FREQ / dt : 0, pulse: c - p.pulseAt < FREQ * 0.05 ? p.pulse : 0 });
      p.high = 0; p.rises = 0; p.since = c;
    }
    this.win0 = c;
    return res;
  }

  start() {
    if (this.running) return;
    this.running = true;
    this._t0 = performance.now(); this._c0 = this.cpu.cycles;
    this._rt = this._t0; this._rc = this.cpu.cycles;
    this._loop();
  }
  stop() { this.running = false; clearTimeout(this._timer); }

  _loop() {
    if (!this.running) return;
    const cpu = this.cpu, now = performance.now(), deadline = now + 12;
    let target = this._c0 + (now - this._t0) * FREQ / 1000;
    // الجهاز أبطأ من اللوحة الحقيقية: مانحاولش نلحق أكتر من ربع ثانية (المحاكاة بتبطأ بدل ما الواجهة تقف)
    if (target - cpu.cycles > FREQ / 4) { this._c0 = cpu.cycles; this._t0 = now; target = cpu.cycles + FREQ / 60; }
    while (cpu.cycles < target && performance.now() < deadline) {
      const end = Math.min(target, cpu.cycles + 40000);
      while (cpu.cycles < end) { avrInstruction(cpu); cpu.tick(); }
      if (this.rxQueue.length && !this.usart.rxBusy) this.usart.writeByte(this.rxQueue.shift());
    }
    this.simMs = cpu.cycles / FREQ * 1000;
    if (now - this._rt >= 1000) { this.speed = Math.min(1, (cpu.cycles - this._rc) / FREQ / ((now - this._rt) / 1000)); this._rt = now; this._rc = cpu.cycles; }
    if (cpu.cycles < target) this._chan.port1.postMessage(0);   // متأخرين: نكمّل فورًا
    else this._timer = setTimeout(() => this._loop(), 4);
  }

  /** (للفحص) تشغيل بدون انتظار الزمن الحقيقي لعدد من الميلي ثانية */
  runFor(ms) {
    const cpu = this.cpu, end = cpu.cycles + ms * FREQ / 1000;
    while (cpu.cycles < end) {
      const stop = Math.min(end, cpu.cycles + 4000);
      while (cpu.cycles < stop) { avrInstruction(cpu); cpu.tick(); }
      if (this.rxQueue.length && !this.usart.rxBusy) this.usart.writeByte(this.rxQueue.shift());
    }
    this.simMs = cpu.cycles / FREQ * 1000;
  }
}
