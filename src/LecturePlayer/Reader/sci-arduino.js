// محاكي أردوينو جوه «مسار» (من غير نت ومن غير أي موقع):
//   الكود ← البرنامج يترجمه بـ arduino-cli المتثبت جنبه ← ملف hex ← avr8js بيشغّله هنا
//   اللوحة (wokwi-elements) بتنوّر لمبة 13 والطاقة والإرسال، والشاشة التسلسلية بتعرض وتبعت نص.
// المرحلة دي: المحرك + الشاشة التسلسلية. محرر الدوائر والكتل جايين في المراحل الجاية.
import { Apps } from "./sci-apps.js";
import { Uno, parseHex } from "./ardu-sim.js";

const h = (tag, attrs = {}, ...kids) => {
  const e = document.createElement(tag);
  for (const k in attrs) {
    const v = attrs[k];
    if (v == null || v === false) continue;
    if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (k === "cls") e.className = v;
    else if (k === "style" && typeof v === "object") Object.assign(e.style, v);
    else e.setAttribute(k, v === true ? "" : v);
  }
  for (const c of kids.flat(Infinity)) if (c != null && c !== false) e.append(c.nodeType ? c : document.createTextNode(String(c)));
  return e;
};
const loadScript = (src, global) => new Promise((res, rej) => {
  if (window[global]) return res(window[global]);
  const s = document.createElement("script");
  s.src = src; s.onload = () => res(window[global]); s.onerror = () => rej(new Error("تعذّر تحميل رسوم القطع"));
  document.head.appendChild(s);
});
const AR_DIG = "٠١٢٣٤٥٦٧٨٩";
const arNum = n => String(n).replace(/\d/g, d => AR_DIG[d]);
const store = {
  get(k) { try { return localStorage.getItem("masar.ardu." + k); } catch { return null; } },
  set(k, v) { try { localStorage.setItem("masar.ardu." + k, v); } catch { } },
};

export const BLINK = `// وميض اللمبة المدمجة في اللوحة (الطرف 13)
void setup() {
  pinMode(13, OUTPUT);
  Serial.begin(9600);
  Serial.println("بدأ البرنامج");
}

void loop() {
  digitalWrite(13, HIGH);   // تشغيل
  delay(500);
  digitalWrite(13, LOW);    // إطفاء
  delay(500);
  Serial.println("tick");
}
`;

// ---------------------------------------------------------------- تلوين الكود (عرض فقط تحت مربع الكتابة)
const KW = new Set("void int long float double char bool boolean byte word short unsigned signed const static volatile if else for while do switch case default break continue return true false struct class new delete sizeof String uint8_t uint16_t uint32_t int8_t int16_t int32_t".split(" "));
const CONST = new Set("HIGH LOW INPUT OUTPUT INPUT_PULLUP LED_BUILTIN A0 A1 A2 A3 A4 A5 PI".split(" "));
const FN = new Set("setup loop pinMode digitalWrite digitalRead analogWrite analogRead delay delayMicroseconds millis micros tone noTone map constrain random randomSeed abs min max pow sqrt sin cos pulseIn shiftOut attach write read print println begin available setCursor clear".split(" "));
const esc = s => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
export function highlight(code) {
  const rx = /(\/\/[^\n]*|\/\*[\s\S]*?(?:\*\/|$))|("(?:[^"\\\n]|\\.)*"?|'(?:[^'\\\n]|\\.)*'?)|(^[ \t]*#\w+[^\n]*)|(\b\d+(?:\.\d+)?\b|\b0x[0-9a-fA-F]+\b)|([A-Za-z_]\w*)/gm;
  let out = "", last = 0, m;
  while ((m = rx.exec(code))) {
    out += esc(code.slice(last, m.index)); last = rx.lastIndex;
    const t = esc(m[0]);
    if (m[1]) out += `<i class="c">${t}</i>`;
    else if (m[2]) out += `<i class="s">${t}</i>`;
    else if (m[3]) out += `<i class="p">${t}</i>`;
    else if (m[4]) out += `<i class="n">${t}</i>`;
    else out += KW.has(m[0]) ? `<i class="k">${t}</i>` : CONST.has(m[0]) ? `<i class="n">${t}</i>` : FN.has(m[0]) ? `<i class="f">${t}</i>` : t;
  }
  return out + esc(code.slice(last)) + "\n";
}

export const Ardu = {
  sci: null, pending: new Map(), seq: 0, uno: null, ui: null, hex: null,
  Uno, parseHex,
  init(sci) { this.sci = sci; },

  /** ترجمة الكود في البرنامج (من غير نت) ← { ok, hex, problems[{line,col,kind,msg,orig}], flash, flashMax, ram, ramMax, error, ms } */
  compile(code) {
    const id = "a" + (++this.seq);
    return new Promise(res => {
      this.pending.set(id, res);
      if (!window.chrome?.webview && !window.__ARDU_FAKE) { this.pending.delete(id); res({ ok: false, problems: [], error: "المحاكي يعمل داخل «مسار» فقط." }); return; }
      this.sci.post({ t: "ardu", id, code });
    });
  },
  /** البرنامج بيرد هنا (masar.arduResult) */
  result(id, r) { const f = this.pending.get(id); this.pending.delete(id); f?.(r); },

  isOpen() { return !!this.ui && Apps.win === this.ui.win; },

  async open(state = {}) {
    await loadScript("lib/sci/wokwi/wokwi-elements.bundle.min.js", "elements");
    const code0 = state.code ?? store.get("code") ?? BLINK;
    const ta = h("textarea", { cls: "ard-ta", dir: "ltr", spellcheck: "false", wrap: "off", "aria-label": "كود أردوينو", autocomplete: "off", autocapitalize: "off" });
    ta.value = code0;
    const hl = h("pre", { cls: "ard-hl", dir: "ltr", "aria-hidden": "true" });
    const gut = h("div", { cls: "ard-gut", dir: "ltr", "aria-hidden": "true" });
    const probs = h("div", { cls: "ard-probs", "aria-live": "polite" });
    const board = h("wokwi-arduino-uno", { cls: "ard-uno" });
    const stat = h("div", { cls: "ard-stat" });
    const ser = h("pre", { cls: "ard-ser", "aria-label": "الشاشة التسلسلية", tabindex: "0" });
    const sin = h("input", { cls: "ard-sin", dir: "auto", placeholder: "نص يُرسل إلى اللوحة…", "aria-label": "نص يُرسل إلى اللوحة",
      onkeydown: e => { if (e.key === "Enter") { e.preventDefault(); send(); } } });
    const auto = h("input", { type: "checkbox", checked: true });
    const baud = h("span", { cls: "ard-baud" });
    const ed = h("div", { cls: "ard-ed" }, gut, h("div", { cls: "ard-code" }, hl, ta));

    const btnRun = h("button", { cls: "pri", title: "ترجمة وتشغيل (Ctrl+Enter)", onclick: () => this.run() }, "▶ تشغيل");
    const btnStop = h("button", { title: "إيقاف المحاكاة", onclick: () => this.stop(), disabled: true }, "⏹ إيقاف");
    const btnReset = h("button", { title: "إعادة تشغيل اللوحة من البداية", onclick: () => this.reset(), disabled: true }, "↺ إعادة التشغيل");

    const send = () => { if (!this.uno || !sin.value) return; this.uno.serialWrite(sin.value + "\n"); this.rxAt = performance.now(); sin.value = ""; };
    const body = h("div", { cls: "ard-wrap" },
      h("div", { cls: "ard-left" }, ed, probs),
      h("div", { cls: "ard-right" },
        h("div", { cls: "ard-board" }, board),
        stat,
        h("div", { cls: "ard-serh" }, h("b", {}, "الشاشة التسلسلية"), baud,
          h("label", {}, auto, " تمرير تلقائي"),
          h("button", { onclick: () => { ser.textContent = ""; this.serText = ""; } }, "مسح")),
        ser,
        h("div", { cls: "ard-sinrow" }, sin, h("button", { onclick: send }, "إرسال"))));
    const win = Apps.frame("محاكي أردوينو", "💻", [btnRun, btnStop, btnReset], body,
      "المحاكاة: avr8js (MIT) — رسوم اللوحة: wokwi-elements (MIT) — الترجمة بدون إنترنت: arduino-cli و avr-gcc (GPL) ونواة أردوينو (LGPL)");
    win.classList.add("ard-win");

    const ui = this.ui = { win, ta, hl, gut, probs, board, stat, ser, auto, baud, btnRun, btnStop, btnReset, marks: new Map() };
    const sync = () => {
      hl.innerHTML = highlight(ta.value);
      const n = ta.value.split("\n").length;
      if (gut.childElementCount !== n) { gut.textContent = ""; for (let i = 1; i <= n; i++) gut.append(h("div", { "data-l": i }, i)); }
      for (const [l, k] of ui.marks) gut.children[l - 1]?.classList.add(k);
      store.set("code", ta.value);
    };
    const scroll = () => { hl.scrollTop = ta.scrollTop; hl.scrollLeft = ta.scrollLeft; gut.scrollTop = ta.scrollTop; };
    ta.addEventListener("input", () => { if (ui.marks.size) { ui.marks.clear(); for (const d of gut.children) d.className = ""; } sync(); });
    ta.addEventListener("scroll", scroll);
    ta.addEventListener("keydown", e => {
      if (e.key === "Enter" && e.ctrlKey) { e.preventDefault(); this.run(); return; }
      if (e.key === "Tab" && !e.ctrlKey && !e.altKey) {
        e.preventDefault();
        const s = ta.selectionStart, v = ta.value;
        if (e.shiftKey) { const ls = v.lastIndexOf("\n", s - 1) + 1; if (v.startsWith("  ", ls)) { ta.setRangeText("", ls, ls + 2, "preserve"); } }
        else ta.setRangeText("  ", s, ta.selectionEnd, "end");
        ta.dispatchEvent(new Event("input"));
      } else if (e.key === "Enter" && !e.shiftKey) {
        // سطر جديد بنفس المسافة البادئة (+2 بعد «{»)
        e.preventDefault();
        const s = ta.selectionStart, v = ta.value, ls = v.lastIndexOf("\n", s - 1) + 1;
        let ind = /^[ \t]*/.exec(v.slice(ls))[0];
        if (/\{\s*$/.test(v.slice(ls, s))) ind += "  ";
        ta.setRangeText("\n" + ind, s, ta.selectionEnd, "end");
        ta.dispatchEvent(new Event("input"));
      }
    });
    sync();
    Apps.cleanup = () => { this.stop(true); this.ui = null; };
    this.say("اكتب الكود ثم اضغط «تشغيل». الترجمة تتم على هذا الجهاز بدون إنترنت.");
    setTimeout(() => ta.focus(), 0);
    return win;
  },

  say(t, kind) { Apps.say?.(t, kind); },

  showProblems(list) {
    const ui = this.ui; if (!ui) return;
    ui.probs.textContent = ""; ui.marks.clear();
    for (const d of ui.gut.children) d.className = "";
    for (const p of list) {
      if (p.line > 0) { const k = p.kind === "warning" ? "warn" : "err"; if (ui.marks.get(p.line) !== "err") ui.marks.set(p.line, k); ui.gut.children[p.line - 1]?.classList.add(k); }
      ui.probs.append(h("button", { cls: "ard-prob " + (p.kind === "warning" ? "warn" : "err"), title: p.orig || "",
        onclick: () => this.goLine(p.line, p.col) },
        h("b", {}, p.line > 0 ? "السطر " + arNum(p.line) : p.kind === "warning" ? "تنبيه" : "خطأ"),
        h("span", {}, p.msg),
        p.orig && p.orig !== p.msg ? h("small", { dir: "ltr" }, p.orig) : null));
    }
  },
  goLine(line, col = 1) {
    const ta = this.ui?.ta; if (!ta || !(line > 0)) return;
    const ls = ta.value.split("\n"); let pos = 0;
    for (let i = 0; i < line - 1 && i < ls.length; i++) pos += ls[i].length + 1;
    const end = pos + (ls[line - 1]?.length ?? 0);
    ta.focus(); ta.setSelectionRange(pos + Math.max(0, Math.min(col - 1, end - pos)), end);
    ta.scrollTop = Math.max(0, (line - 4) * parseFloat(getComputedStyle(ta).lineHeight || "20"));
  },

  async run() {
    const ui = this.ui; if (!ui || this.busy) return;
    this.busy = true; ui.btnRun.disabled = true;
    this.stop(true);
    this.say("جارٍ ترجمة البرنامج…");
    ui.probs.textContent = "";
    try {
      const r = await this.compile(ui.ta.value);
      this.last = r;
      if (!this.ui) return;
      this.showProblems(r.problems || []);
      if (!r.ok) { this.say(r.error || "توجد أخطاء في الكود. اضغط على الخطأ للانتقال إلى سطره.", "err"); return; }
      const pct = r.flashMax ? Math.round(r.flash * 100 / r.flashMax) : 0;
      this.info = `حجم البرنامج ${arNum(r.flash)} بايت (${arNum(pct)}٪) — زمن الترجمة ${arNum((r.ms / 1000).toFixed(1))} ث`;
      this.start(r.hex);
    } catch (e) { this.say(e.message || String(e), "err"); }
    finally { this.busy = false; if (this.ui) ui.btnRun.disabled = false; }
  },

  /** تشغيل ملف hex في اللوحة */
  start(hex) {
    const ui = this.ui;
    this.stop(true);
    this.hex = hex;
    this.uno = new Uno(hex);
    this.serText = ""; ui.ser.textContent = "";
    const dec = new TextDecoder("utf-8");
    this.toggles = 0;
    this.uno.watchers.add(p => { if (p === 13) this.toggles++; });
    this.uno.onSerial = b => {
      this.serText += dec.decode(new Uint8Array([b]), { stream: true });
      if (this.serText.length > 30000) this.serText = this.serText.slice(-20000);
      this.serDirty = true;
    };
    this.uno.start();
    ui.btnStop.disabled = false; ui.btnReset.disabled = false;
    ui.board.ledPower = true;
    this.say("اللوحة تعمل. " + (this.info || ""), "ok");
    const frame = () => {
      if (!this.uno?.running || !this.ui) return;
      const s = this.uno.sample(), now = performance.now();
      ui.board.led13 = s[13].duty > 0.4;
      ui.board.ledTX = now - this.uno.txAt < 60;
      ui.board.ledRX = now - (this.rxAt || 0) < 60;
      if (this.serDirty) {
        this.serDirty = false; ui.ser.textContent = this.serText;
        if (ui.auto.checked) ui.ser.scrollTop = ui.ser.scrollHeight;
      }
      ui.baud.textContent = arNum(this.uno.baud) + " baud";
      const t = Math.floor(this.uno.simMs / 1000);
      ui.stat.textContent = `الزمن داخل اللوحة ${arNum(String(Math.floor(t / 60)).padStart(2, "0"))}:${arNum(String(t % 60).padStart(2, "0"))}` +
        (this.uno.speed < 0.95 ? ` — سرعة المحاكاة ${arNum(Math.round(this.uno.speed * 100))}٪ من الحقيقية` : "");
      this.raf = requestAnimationFrame(frame);
    };
    this.raf = requestAnimationFrame(frame);
  },
  stop(quiet) {
    cancelAnimationFrame(this.raf);
    if (this.uno) { this.uno.stop(); }
    const ui = this.ui;
    if (ui) {
      ui.board.ledPower = false; ui.board.led13 = false; ui.board.ledTX = false; ui.board.ledRX = false;
      ui.btnStop.disabled = true;
      if (!quiet) this.say("توقفت اللوحة.");
    }
  },
  reset() { if (this.hex) this.start(this.hex); },
};
