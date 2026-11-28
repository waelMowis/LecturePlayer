// محاكي أردوينو جوه «مسار» (من غير نت ومن غير أي موقع):
//   الكود ← البرنامج يترجمه بـ arduino-cli المتثبت جنبه ← ملف hex ← avr8js بيشغّله هنا
//   اللوحة (wokwi-elements) بتنوّر لمبة 13 والطاقة والإرسال، والشاشة التسلسلية بتعرض وتبعت نص.
// المرحلة دي: المحرك + الشاشة التسلسلية. محرر الدوائر والكتل جايين في المراحل الجاية.
import { Apps } from "./sci-apps.js";
import { Uno, parseHex } from "./ardu-sim.js";
import { Circuit, partThumb } from "./ardu-circuit.js";
import { PARTS, PALETTE, WIRE_COLORS } from "./ardu-parts.js";
import { createBlocks } from "./ardu-blocks.js";
import { PROJECTS, projectState } from "./ardu-projects.js";
import { ToolStore, hash } from "./tool-store.js";

/** بصمة المشروع (للمقارنة: اتغيّر من ساعة الفتح ولا لأ) */
const projSig = s => hash(JSON.stringify([s.code ?? "", s.circuit ?? null, s.blocks ?? null, s.mode || "code"]));
const CAP_W = (w, hh) => Math.min(940, w * 0.85, 620 * w / hh);

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

/** الكود كـ SVG ملوّن (لصورة السبورة) */
const HL_FILL = { c: "#6a9955", s: "#ce9178", p: "#c586c0", n: "#b5cea8", k: "#569cd6", f: "#dcdcaa" };
function codeSvg(code, maxLines = 44) {
  let lines = code.replace(/\t/g, "  ").replace(/\s+$/, "").split("\n");
  if (lines.length > maxLines) lines = [...lines.slice(0, maxLines - 1), "…"];
  const LH = 17, CW = 7.3, w = Math.min(600, Math.max(260, Math.max(...lines.map(l => l.length)) * CW + 56)), hh = lines.length * LH + 20;
  let s = `<rect width="${w}" height="${hh}" rx="10" fill="#1e1f22"/>`;
  lines.forEach((l, i) => {
    const body = highlight(l).replace(/\n$/, "").replace(/<i class="(\w)">/g, (m, k) => `<tspan style="fill:${HL_FILL[k] || "#d4d4d4"}">`).replace(/<\/i>/g, "</tspan>");
    s += `<text x="34" y="${16 + i * LH}" style="font: 12.5px Consolas, 'Cascadia Mono', monospace; fill: #d4d4d4; white-space: pre" xml:space="preserve" direction="ltr">${body}</text>`;
    s += `<text x="26" y="${16 + i * LH}" style="font: 11px Consolas, monospace; fill: #6c727a" text-anchor="end">${i + 1}</text>`;
  });
  return { svg: s, w, h: hh };
}

export const Ardu = {
  sci: null, pending: new Map(), seq: 0, uno: null, ui: null, hex: null,
  Uno, parseHex, PROJECTS,
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

  /** مشروع جاهز (من تبويب «حاسب») */
  openProject(id) { const p = PROJECTS.find(x => x.id === id); return p ? this.open(projectState(p)) : null; },

  async open(state = {}, edit = null) {
    await loadScript("lib/sci/wokwi/wokwi-elements.bundle.min.js", "elements");
    // الحفظ التلقائي: لكل سبورة مفتاح، ولكل عنصر مفتوح من السبورة مفتاح (تعديلات لسه ماتحفظتش فيه)
    const docKey = "ardu:board:" + (this.sci?.docId || "none"), itemKey = edit ? "ardu:item:" + edit.id : null;
    const itemBase = edit ? projSig(state) : null;
    let restored = false;
    if (edit) {
      const r = await ToolStore.get(itemKey);
      if (r?.data && r.base === itemBase) { state = { ...state, ...r.data }; restored = true; }
    } else if (state.code == null && state.circuit == null) {
      const r = await ToolStore.get(docKey);
      if (r?.data) { state = { ...r.data, mode: state.mode || r.data.mode }; restored = true; }
    }
    clearTimeout(this._asave);
    const ctx = { key: null, base: null, openSig: null, savedSig: null };   // حالة الحفظ الخاصة بالنافذة دي
    const code0 = state.code ?? BLINK;
    const ta = h("textarea", { cls: "ard-ta", dir: "ltr", spellcheck: "false", wrap: "off", "aria-label": "كود أردوينو", autocomplete: "off", autocapitalize: "off" });
    ta.value = code0;
    const hl = h("pre", { cls: "ard-hl", dir: "ltr", "aria-hidden": "true" });
    const gut = h("div", { cls: "ard-gut", dir: "ltr", "aria-hidden": "true" });
    const probs = h("div", { cls: "ard-probs", "aria-live": "polite" });
    const circ = this.circuit = new Circuit({
      onChange: () => { this.autosave(); this.ctlBar(); },
      onSel: sel => this.selBar(sel),
      onWarn: list => this.showWarn(list),
      onReset: () => this.reset(),
      now: () => this.uno?.simMs ?? 0,
    });
    const bar = h("div", { cls: "ard-bar" });
    const pal = h("div", { cls: "ard-pal", role: "list", "aria-label": "القطع" });
    const ctl = h("div", { cls: "ard-ctl" });
    const warnBox = h("div", { cls: "ard-warn", "aria-live": "polite" });
    const stat = h("div", { cls: "ard-stat" });
    const ser = h("pre", { cls: "ard-ser", "aria-label": "الشاشة التسلسلية", tabindex: "0" });
    const sin = h("input", { cls: "ard-sin", dir: "auto", placeholder: "نص يُرسل إلى اللوحة…", "aria-label": "نص يُرسل إلى اللوحة",
      onkeydown: e => { if (e.key === "Enter") { e.preventDefault(); send(); } } });
    const auto = h("input", { type: "checkbox", checked: true });
    const baud = h("span", { cls: "ard-baud" });
    const ed = h("div", { cls: "ard-ed" }, gut, h("div", { cls: "ard-code" }, hl, ta));
    // الكتل: مساحة Blockly + الكود المولَّد جنبها (للقراءة)
    const blkHost = h("div", { cls: "ard-blk", dir: "ltr" });
    const blkCode = h("pre", { cls: "ard-blkcode", dir: "ltr", "aria-label": "الكود المولَّد من الكتل" });
    const blkWrap = h("div", { cls: "ard-blkwrap", hidden: true }, blkHost,
      h("div", { cls: "ard-blkside" }, h("div", { cls: "ard-blkh" }, h("b", {}, "الكود المولَّد"),
        h("button", { title: "انسخ الكود إلى المحرر لتعديله يدويًا (تبقى الكتل كما هي)", onclick: () => { ta.value = this.blocks?.code() || ta.value; ta.dispatchEvent(new Event("input")); this.setMode("code"); } }, "تعديله يدويًا")), blkCode));
    const tabCode = h("button", { cls: "ard-tab", role: "tab", onclick: () => this.setMode("code") }, "✎ الكود");
    const tabBlk = h("button", { cls: "ard-tab", role: "tab", onclick: () => this.setMode("blocks") }, "🧩 الكتل");

    const btnRun = h("button", { cls: "pri", title: "ترجمة وتشغيل (Ctrl+Enter)", onclick: () => this.run() }, "▶ تشغيل");
    const btnStop = h("button", { title: "إيقاف المحاكاة", onclick: () => this.stop(), disabled: true }, "⏹ إيقاف");
    const btnReset = h("button", { title: "إعادة تشغيل اللوحة من البداية", onclick: () => this.reset(), disabled: true }, "↺ إعادة التشغيل");
    const btnCap = h("button", { title: "ضع صورة الدائرة والكود على صفحة السبورة (انقر عليها مرتين لاحقًا لفتح المشروع من جديد)", onclick: () => this.capture() }, edit ? "⤓ حدّث الصورة على السبورة" : "⤓ التقط إلى السبورة");

    const send = () => { if (!this.uno || !sin.value) return; this.uno.serialWrite(sin.value + "\n"); this.rxAt = performance.now(); sin.value = ""; };
    const body = h("div", { cls: "ard-wrap" },
      h("div", { cls: "ard-left" }, h("div", { cls: "ard-tabs", role: "tablist" }, tabCode, tabBlk), ed, blkWrap, probs),
      h("div", { cls: "ard-right" },
        bar,
        h("div", { cls: "ard-mid" }, pal, circ.root),
        ctl, warnBox,
        stat,
        h("div", { cls: "ard-serh" }, h("b", {}, "الشاشة التسلسلية"), baud,
          h("label", {}, auto, " تمرير تلقائي"),
          h("button", { onclick: () => { ser.textContent = ""; this.serText = ""; } }, "مسح")),
        ser,
        h("div", { cls: "ard-sinrow" }, sin, h("button", { onclick: send }, "إرسال"))));
    const win = Apps.frame(state.title ? "محاكي أردوينو — " + state.title : "محاكي أردوينو", "💻", [btnRun, btnStop, btnReset, btnCap], body,
      "المحاكاة: avr8js (MIT) — رسوم اللوحة: wokwi-elements (MIT) — الترجمة بدون إنترنت: arduino-cli و avr-gcc (GPL) ونواة أردوينو (LGPL)");
    win.classList.add("ard-win");

    const ui = this.ui = { win, ta, hl, gut, ed, blkWrap, blkHost, blkCode, tabCode, tabBlk, probs, circ, bar, pal, ctl, warnBox, stat, ser, auto, baud, btnRun, btnStop, btnReset, marks: new Map() };
    Object.defineProperty(ui, "board", { get: () => circ.parts.get("uno")?.w });
    const sync = () => {
      hl.innerHTML = highlight(ta.value);
      const n = ta.value.split("\n").length;
      if (gut.childElementCount !== n) { gut.textContent = ""; for (let i = 1; i <= n; i++) gut.append(h("div", { "data-l": i }, i)); }
      for (const [l, k] of ui.marks) gut.children[l - 1]?.classList.add(k);
      this.autosave();
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
    // الدائرة: المحفوظة (أو أردوينو لوحده) + قائمة القطع
    const saved = state.circuit || null;
    await circ.load(saved);
    this.edit = edit; this.title = state.title || "";
    ctx.base = itemBase;
    // مشروع جاهز: القطع الصغيرة على ثقوب لوحة التوصيل
    if (state.plug?.length) {
      for (const [id, pin, hole] of state.plug) { const at = circ.holeAt("bb1", hole); if (at) circ.moveTo(id, pin, ...at); }
      circ.changed(false);
    }
    let fitted = false;
    const ro = new ResizeObserver(() => { if (!fitted && circ.root.clientWidth > 50) { fitted = true; if (!saved?.view) circ.fit(); } });
    ro.observe(circ.root);
    for (const t of PALETTE) {
      const b = h("button", { cls: "ard-pal-it", role: "listitem", title: PARTS[t].name, "data-type": t,
        onclick: async () => { if (this.uno?.running) { this.say("أوقف المحاكاة أولًا لتعديل الدائرة.", "warn"); return; } const [x, y] = circ.freeSpot(); await circ.addPart(t, { x, y }); circ.root.focus(); } },
        h("img", { alt: "" }), h("span", {}, PARTS[t].name));
      pal.append(b);
      partThumb(t).then(u => { b.querySelector("img").src = u; }).catch(() => { });
    }
    this.selBar(null); this.ctlBar();
    this.blockState = state.blocks ?? null;
    await this.setMode(state.mode || store.get("mode") || "code");
    ctx.openSig = projSig(this.snapshot());
    clearTimeout(this._asave); ctx.key = edit ? itemKey : docKey; this.ctx = ctx;
    const roB = new ResizeObserver(() => this.blocks?.resize()); roB.observe(blkHost);
    // الإغلاق (أو فتح نافذة جديدة فوقها، أو تغيير الملف المفتوح، أو قفل البرنامج): بننضّف النافذة دي بس.
    // لو مفتوحة من عنصر على السبورة واتعدّلت: الصورة والمشروع على السبورة يتحدّثوا لوحدهم.
    Apps.cleanup = () => {
      ro.disconnect(); roB.disconnect();
      let p = null;
      if (this.ui === ui) {
        clearTimeout(this._asave);
        const snap = this.snapshot(), sig = projSig(snap);
        if (edit && ctx.openSig && sig !== ctx.openSig && sig !== ctx.savedSig) {
          const { url, src } = this.renderCapture();
          p = Apps.store(src, url, edit, CAP_W, false)
            .then(it => it ? ToolStore.del(itemKey) : ToolStore.set(docKey, { data: snap }))
            .catch(() => ToolStore.set(itemKey, { base: itemBase, data: snap }));
        } else if (ctx.key && sig !== ctx.openSig && sig !== ctx.savedSig) p = this.writeSave(snap, ctx);
        this.stop(true); this.ui = null;
      }
      if (this.blocks && this.blocksOf === ui) { try { this.blocks.dispose(); } catch { } this.blocks = null; }
      if (this.circuit === circ) this.circuit = null;
      return p;
    };
    this.say(restored ? (edit ? "استُعيدت تعديلات لم تُحفظ في السبورة." : "استُعيد آخر عمل على هذه السبورة.") : "اكتب الكود ثم اضغط «تشغيل». الترجمة تتم على هذا الجهاز بدون إنترنت.", restored ? "ok" : "");
    setTimeout(() => ta.focus(), 0);
    return win;
  },

  say(t, kind) { Apps.say?.(t, kind); },

  /** «الكود» أو «الكتل» */
  async setMode(mode) {
    const ui = this.ui; if (!ui) return;
    this.mode = mode === "blocks" ? "blocks" : "code";
    store.set("mode", this.mode); this.autosave();
    ui.ed.hidden = this.mode === "blocks"; ui.blkWrap.hidden = this.mode !== "blocks";
    ui.win.classList.toggle("ard-blkmode", this.mode === "blocks");
    ui.tabCode.classList.toggle("on", this.mode === "code"); ui.tabBlk.classList.toggle("on", this.mode === "blocks");
    ui.tabCode.setAttribute("aria-selected", this.mode === "code"); ui.tabBlk.setAttribute("aria-selected", this.mode === "blocks");
    if (this.mode === "blocks" && !this.blocks) {
      try {
        this._centered = false;
        this.blocksOf = ui;
        this.blocks = await createBlocks(ui.blkHost, this.blockState, api => {
          ui.blkCode.innerHTML = highlight(api.code());
          this.autosave();
        });
        ui.blkCode.innerHTML = highlight(this.blocks.code());
      } catch (e) { this.say(e.message, "err"); this.mode = "code"; return this.setMode("code"); }
    }
    if (this.mode === "blocks") setTimeout(() => { this.blocks?.resize(); if (!this._centered && this.blocks) { this._centered = true; this.blocks.ws.scrollCenter(); } }, 0);
  },
  /** الكود اللي هيتشغّل: من المحرر أو من الكتل */
  source() { return this.mode === "blocks" && this.blocks ? this.blocks.code() : this.ui.ta.value; },

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

  /** شريط فوق الدائرة: خصائص القطعة/السلك المختار، أو التلميحات */
  selBar(sel) {
    const ui = this.ui; if (!ui) return;
    const c = ui.circ, bar = ui.bar; bar.textContent = "";
    const btn = (t, title, f, cls) => h("button", { title, onclick: f, cls }, t);
    const tools = h("span", { cls: "ard-bar-tools" },
      btn("↶", "تراجع (Ctrl+Z)", () => c.undo()), btn("↷", "إعادة (Ctrl+Y)", () => c.redo()),
      btn("−", "تصغير", () => c.zoom(1 / 1.25)), btn("+", "تكبير", () => c.zoom(1.25)), btn("⤢", "ملاءمة الدائرة للمساحة", () => c.fit()));
    if (c.running) { bar.append(h("span", { cls: "ard-hint" }, "اللوحة تعمل: اضغط الأزرار وحرّك المقاومة المتغيرة والحساسات. أوقف المحاكاة لتعديل الدائرة."), tools); return; }
    if (sel?.wiring) { bar.append(h("span", { cls: "ard-hint" }, "اضغط على الطرف الثاني لإكمال السلك، أو على مكان فارغ لإضافة انحناءة. Esc للإلغاء."), tools); return; }
    if (sel?.wire) {
      const w = c.wires.find(x => x.id === sel.wire);
      bar.append(h("b", {}, "سلك"), h("span", { cls: "ard-swatches" }, WIRE_COLORS.map(([col, name]) =>
        h("button", { cls: "ard-sw" + (w?.color === col ? " on" : ""), title: name, style: { background: col }, onclick: () => { c.setWireColor(sel.wire, col); this.selBar(sel); } }))),
        btn("🗑 حذف", "حذف السلك (Delete)", () => c.deleteSel()), tools);
      return;
    }
    const p = sel?.part && c.parts.get(sel.part);
    if (p) {
      const def = PARTS[p.type];
      bar.append(h("b", {}, def.name));
      for (const [k, [label, list]] of Object.entries(def.opts || {})) {
        const s = h("select", { title: label, "aria-label": label, onchange: () => c.setProp(p.id, k, s.value) }, list.map(([v, t]) => h("option", { value: v, selected: String(p.props[k]) === v }, t)));
        bar.append(h("label", { cls: "ard-opt" }, label + " ", s));
      }
      if (!def.fixed) bar.append(btn("⟳ تدوير", "تدوير 90° (R)", () => c.rotate(p.id)), btn("🗑 حذف", "حذف القطعة (Delete)", () => c.deleteSel()));
      bar.append(tools);
      return;
    }
    bar.append(h("span", { cls: "ard-hint" }, "أضف قطعة من القائمة واسحبها لمكانها. لتوصيل سلك: اضغط على طرف ثم على طرف آخر. ضع أرجل القطعة على ثقوب لوحة التوصيل لتتصل تلقائيًا."), tools);
  },
  /** أدوات التحكم في الحساسات */
  ctlBar() {
    const ui = this.ui; if (!ui) return;
    const list = [...ui.circ.parts.values()].filter(p => PARTS[p.type].ctl);
    const key = list.map(p => p.id).join(",");
    if (ui.ctl.dataset.key === key) return;
    ui.ctl.dataset.key = key; ui.ctl.textContent = "";
    for (const p of list) {
      const c = PARTS[p.type].ctl;
      if (c.button) { ui.ctl.append(h("div", { cls: "ard-ctl-row" }, h("b", {}, PARTS[p.type].name + " " + p.id.replace(/\D/g, "")), h("button", { onclick: () => ui.circ.motion(p.id) }, "🚶 " + c.label))); continue; }
      const toPos = v => c.log ? Math.round(Math.log10(v / c.min) / Math.log10(c.max / c.min) * 1000) : v;
      const fromPos = x => c.log ? Math.round(c.min * Math.pow(c.max / c.min, x / 1000)) : +x;
      const out = h("output", {});
      const show = v => { out.textContent = arNum(v) + " " + c.unit; };
      const r = h("input", { type: "range", min: c.log ? 0 : c.min, max: c.log ? 1000 : c.max, step: c.log ? 1 : c.step, value: toPos(p.st[c.k] ?? c.def), "aria-label": c.label,
        oninput: () => { const v = fromPos(r.value); ui.circ.setCtl(p.id, v); show(v); } });
      show(p.st[c.k] ?? c.def);
      ui.ctl.append(h("div", { cls: "ard-ctl-row" }, h("b", {}, PARTS[p.type].name + " " + p.id.replace(/\D/g, "")), h("span", {}, c.label), r, out));
    }
  },
  showWarn(list) {
    const ui = this.ui; if (!ui) return;
    ui.warnBox.textContent = "";
    for (const t of list) ui.warnBox.append(h("div", {}, "⚠ " + t));
  },

  /** حالة المشروع الحالية (الكود + الدائرة + الكتل + الوضع) */
  snapshot() {
    const ui = this.ui;
    return { code: ui.ta.value, circuit: ui.circ.toJSON(), blocks: this.blocks ? this.blocks.save() : (this.blockState ?? null), mode: this.mode, title: this.title };
  },
  /** حفظ تلقائي على الجهاز بعد أي تعديل (بعد ثانية من آخر تغيير) */
  autosave() {
    const ctx = this.ctx; if (!this.ui || !ctx?.key) return;
    clearTimeout(this._asave);
    this._asave = setTimeout(() => { if (!this.ui || this.ctx !== ctx) return; const snap = this.snapshot(); if (projSig(snap) !== ctx.openSig) this.writeSave(snap, ctx); }, 1000);
  },
  writeSave(snap, ctx) {
    return ToolStore.set(ctx.key, ctx.base ? { base: ctx.base, data: snap } : { data: snap });
  },
  /** صورة السبورة (الكود ملوّن + الدائرة بحالتها) + المشروع ← { url, src } */
  renderCapture() {
    const ui = this.ui;
    const code = this.source(), c = codeSvg(code), k = ui.circ.toSVG(), gap = 14;
    const W = c.w + gap + k.w, H = Math.max(c.h, k.h);
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">` +
      `<g>${c.svg}</g><rect x="${c.w + gap}" width="${k.w}" height="${k.h}" rx="10" fill="#f4f6f8"/><g transform="translate(${c.w + gap} 0)">${k.svg}</g></svg>`;
    const url = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg);
    const snap = this.snapshot();
    const src = { app: "arduino", title: this.title, mode: snap.mode, code: snap.code, circuit: snap.circuit };
    if (snap.blocks) src.blocks = snap.blocks;
    return { url, src };
  },
  /** صورة (الكود + الدائرة بحالتها الحالية) على صفحة السبورة، ومعاها المشروع كله لإعادة فتحه بالنقر المزدوج */
  async capture() {
    const ui = this.ui; if (!ui) return;
    const { url, src } = this.renderCapture(), sig = projSig(this.snapshot()), edit = this.edit, ctx = this.ctx;
    clearTimeout(this._asave);
    try {
      if (ctx) ctx.savedSig = sig;   // الإغلاق بعد الالتقاط مايحدّثش تاني
      const it = await Apps.insert(src, url, edit, CAP_W);
      if (!it) { if (ctx) ctx.savedSig = null; }
      else if (edit) ToolStore.del("ardu:item:" + edit.id);
      else if (ctx?.key) this.writeSave(this.snapshot(), ctx);
    } catch (e) { if (ctx) ctx.savedSig = null; this.say(e.message || String(e), "err"); }
  },

  async run() {
    const ui = this.ui; if (!ui || this.busy) return;
    this.busy = true; ui.btnRun.disabled = true;
    this.stop(true);
    this.say("جارٍ ترجمة البرنامج…");
    ui.probs.textContent = "";
    try {
      const r = await this.compile(this.source());
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
    ui.circ.attach(this.uno);
    this.selBar(null);
    ui.btnStop.disabled = false; ui.btnReset.disabled = false;
    ui.board.ledPower = true;
    this.say("اللوحة تعمل. " + (this.info || ""), "ok");
    const frame = () => {
      if (!this.uno?.running || !this.ui) return;
      const s = this.uno.sample(), now = performance.now();
      try { ui.circ.frame(s, this.uno.simMs); } catch (e) { console.error(e); }
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
      if (ui.circ.running) { ui.circ.detach(); this.selBar(null); }
      ui.board.ledPower = false; ui.board.led13 = false; ui.board.ledTX = false; ui.board.ledRX = false;
      ui.btnStop.disabled = true;
      if (!quiet) this.say("توقفت اللوحة.");
    }
  },
  reset() { if (this.hex) this.start(this.hex); },
};
