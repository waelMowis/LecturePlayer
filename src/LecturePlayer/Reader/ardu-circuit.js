// محرر الدوائر لمحاكي أردوينو: قطع على لوحة (تحريك/تدوير/حذف)، أسلاك ملوّنة من طرف لطرف،
// لوحة توصيل بشبكاتها الداخلية، وحلّ الدائرة كل إطار وهي شغالة (ليد، أزرار، مقاومات، حساسات، شاشات…).
import { PARTS, PALETTE, PITCH, BB, bbPins, bbShort, bbSvg, pinLabel, wireColor, WIRE_COLORS } from "./ardu-parts.js";
import { FREQ } from "./ardu-sim.js";

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
const SVGNS = "http://www.w3.org/2000/svg";
const sv = (tag, attrs = {}) => { const e = document.createElementNS(SVGNS, tag); for (const k in attrs) e.setAttribute(k, attrs[k]); return e; };
const AR_DIG = "٠١٢٣٤٥٦٧٨٩";
const arNum = n => String(n).replace(/\d/g, d => AR_DIG[d]);
const UNO_PINS = [...Array.from({ length: 14 }, (_, i) => String(i)), "A0", "A1", "A2", "A3", "A4", "A5"];
const unoIndex = name => { const i = UNO_PINS.indexOf(name); return i >= 0 ? i : name === "A4.2" ? 18 : name === "A5.2" ? 19 : -1; };
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const num = id => arNum(id.replace(/\D/g, ""));

// ---------------------------------------------------------------- رسوم القطع (للقائمة وللالتقاط على السبورة)
/** SVG القطعة بحالتها الحالية (من جوه عنصر wokwi) كنص */
export function elementSvg(el) {
  const svg = el.shadowRoot?.querySelector("svg"); if (!svg) return "";
  const c = svg.cloneNode(true);
  c.setAttribute("xmlns", SVGNS);
  let css = "";
  for (const sh of el.shadowRoot.adoptedStyleSheets || []) for (const r of sh.cssRules) if (!/^:host/.test(r.cssText)) css += r.cssText + "\n";
  for (const st of el.shadowRoot.querySelectorAll("style")) css += st.textContent;
  if (css) { const st = document.createElementNS(SVGNS, "style"); st.textContent = css; c.insertBefore(st, c.firstChild); }
  return new XMLSerializer().serializeToString(c);
}
const thumbs = new Map();
export async function partThumb(type) {
  if (thumbs.has(type)) return thumbs.get(type);
  let url;
  if (type === "breadboard") url = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(bbSvg());
  else {
    const host = h("div", { style: { position: "fixed", left: "-2000px", top: "0" } });
    document.body.append(host);
    const el = document.createElement(PARTS[type].tag);
    host.append(el);
    await el.updateComplete;
    url = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(elementSvg(el));
    host.remove();
  }
  thumbs.set(type, url);
  return url;
}

// ---------------------------------------------------------------- وحدة تحكم شاشة LCD (HD44780) — وضع 4 و 8 بت
class HD44780 {
  constructor() { this.reset(); }
  reset() { this.ddram = new Uint8Array(128).fill(32); this.addr = 0; this.cg = false; this.inc = 1; this.shiftOn = false; this.on = false; this.cursor = false; this.blink = false; this.shift = 0; this.bits8 = true; this.half = null; this.lines = 2; }
  /** نبضة E: rs + بيانات (8 بت في وضع 8، أو الأربعة العليا في وضع 4) */
  strobe(rs, d8, d4) {
    if (this.bits8) return this.exec(rs, d8);
    if (this.half == null) { this.half = d4; return; }
    const v = (this.half << 4) | d4; this.half = null;
    this.exec(rs, v);
  }
  exec(rs, v) {
    if (rs) {
      if (this.cg) { this.addr = (this.addr + 1) & 63; return; }
      this.ddram[this.addr & 127] = v; this.move();
      if (this.shiftOn) this.shift += this.inc;
      return;
    }
    if (v & 0x80) { this.cg = false; this.addr = v & 0x7f; }
    else if (v & 0x40) { this.cg = true; this.addr = v & 0x3f; }
    else if (v & 0x20) { if (!(v & 0x10)) { this.bits8 = false; this.half = null; } this.lines = v & 0x08 ? 2 : 1; }
    else if (v & 0x10) { if (v & 0x08) this.shift += v & 0x04 ? -1 : 1; else this.addr = (this.addr + (v & 0x04 ? 1 : -1)) & 0x7f; }
    else if (v & 0x08) { this.on = !!(v & 4); this.cursor = !!(v & 2); this.blink = !!(v & 1); }
    else if (v & 0x04) { this.inc = v & 2 ? 1 : -1; this.shiftOn = !!(v & 1); }
    else if (v & 0x02) { this.addr = 0; this.shift = 0; this.cg = false; }
    else if (v & 0x01) { this.ddram.fill(32); this.addr = 0; this.shift = 0; this.inc = 1; this.cg = false; }
  }
  move() {
    let a = this.addr + this.inc;
    if (this.addr < 0x40 && a > 0x27) a = 0x40; else if (this.addr >= 0x40 && a > 0x67) a = 0;
    else if (a < 0) a = 0x67; else if (this.addr >= 0x40 && a < 0x40) a = 0x27;
    this.addr = a;
  }
  /** الحروف الظاهرة (32) */
  chars() {
    const out = new Uint8Array(32).fill(32);
    if (!this.on) return out;
    for (let r = 0; r < 2; r++) for (let c = 0; c < 16; c++) {
      const i = (((c + this.shift) % 40) + 40) % 40;
      out[r * 16 + c] = this.ddram[r * 0x40 + i];
    }
    return out;
  }
}

// ---------------------------------------------------------------- اتحاد مجموعات (الشبكات)
class UF {
  constructor(n) { this.p = new Int32Array(n); for (let i = 0; i < n; i++) this.p[i] = i; }
  find(i) { const p = this.p; while (p[i] !== i) { p[i] = p[p[i]]; i = p[i]; } return i; }
  join(a, b) { a = this.find(a); b = this.find(b); if (a !== b) this.p[a] = b; }
  clone() { const u = new UF(0); u.p = this.p.slice(); return u; }
}

export class Circuit {
  constructor(opts = {}) {
    this.opts = opts;
    this.parts = new Map(); this.wires = []; this.seq = 0;
    this.view = { x: 40, y: 30, k: 1 };
    this.sel = null; this.running = false; this.warn = [];
    this.history = []; this.future = [];
    this.root = h("div", { cls: "ard-stage", tabindex: "0", "aria-label": "لوحة الدائرة" });
    this.world = h("div", { cls: "ard-world" });
    this.wsvg = sv("svg", { class: "ard-wires" });
    this.pinsLayer = h("div", { cls: "ard-pins" });
    this.tip = h("div", { cls: "ard-tip", hidden: true });
    this.world.append(this.wsvg, this.pinsLayer);
    this.root.append(this.world, this.tip);
    this.bindStage();
  }

  // ------------------------------------------------------------ النموذج
  id(type) { return (type === "uno" ? "uno" : type.replace(/[^a-z0-9]/g, "").slice(0, 6)) + (type === "uno" ? "" : ++this.seq); }
  async load(json) {
    for (const p of this.parts.values()) p.el.remove();
    this.parts.clear(); this.wires = []; this.sel = null;
    const j = json && json.parts ? json : { parts: [{ id: "uno", type: "uno", x: 0, y: 0 }], wires: [] };
    if (!j.parts.some(p => p.type === "uno")) j.parts.unshift({ id: "uno", type: "uno", x: 0, y: 0 });
    for (const p of j.parts) { await this.addPart(p.type, p, true); const n = +(/\d+$/.exec(p.id)?.[0] || 0); if (n > this.seq) this.seq = n; }
    for (const w of j.wires || []) {
      const [ap, apin] = splitEnd(w.a), [bp, bpin] = splitEnd(w.b);
      if (this.parts.has(ap) && this.parts.has(bp)) this.wires.push({ id: "w" + (++this.seq), a: { p: ap, pin: apin }, b: { p: bp, pin: bpin }, pts: (w.pts || []).map(q => [+q[0], +q[1]]), color: w.color || "#2f9e44" });
    }
    this.history = []; this.future = [];
    this.changed(false);
    if (j.view) this.view = { ...j.view }; else this.fit();
    this.applyView();
  }
  toJSON() {
    return {
      v: 1,
      parts: [...this.parts.values()].map(p => ({ id: p.id, type: p.type, x: Math.round(p.x * 10) / 10, y: Math.round(p.y * 10) / 10, rot: p.rot || 0, props: { ...p.props } })),
      wires: this.wires.map(w => ({ a: w.a.p + ":" + w.a.pin, b: w.b.p + ":" + w.b.pin, pts: w.pts.map(q => [Math.round(q[0]), Math.round(q[1])]), color: w.color })),
    };
  }

  async addPart(type, at = {}, silent) {
    const def = PARTS[type]; if (!def) return null;
    if (type === "uno" && this.parts.has("uno") && !silent) return this.parts.get("uno");
    if (!silent) this.snapshot();
    const p = { id: at.id || this.id(type), type, x: at.x ?? 0, y: at.y ?? 0, rot: at.rot || 0, props: { ...(def.props || {}), ...(at.props || {}) }, st: {} };
    if (def.ctl) p.st[def.ctl.k] = p.props[def.ctl.k] ?? def.ctl.def;
    const wrap = h("div", { cls: "ard-part", "data-id": p.id, "data-type": type });
    if (type === "breadboard") {
      wrap.innerHTML = bbSvg(); p.w = null; p.local = bbPins(); p.size = [BB.w, BB.h];
    } else {
      const el = document.createElement(def.tag);
      wrap.append(el); p.w = el;
      this.world.insertBefore(wrap, this.wsvg);
      await el.updateComplete;
      p.local = (el.pinInfo || []).map(q => ({ name: q.name, x: q.x, y: q.y }));
      p.size = [el.offsetWidth, el.offsetHeight];
      this.bindElement(p);
    }
    if (!wrap.parentNode) this.world.insertBefore(wrap, this.wsvg);
    if (type === "breadboard") this.world.insertBefore(wrap, this.world.firstChild);   // لوحة التوصيل تحت كل القطع
    p.el = wrap;
    this.parts.set(p.id, p);
    this.applyProps(p);
    this.place(p);
    if (!silent) { this.select({ part: p.id }); this.changed(false); }
    return p;
  }
  removePart(id) {
    const p = this.parts.get(id); if (!p || PARTS[p.type].fixed) return;
    p.el.remove(); this.parts.delete(id);
    this.wires = this.wires.filter(w => w.a.p !== id && w.b.p !== id);
    this.sel = null; this.changed(false);
  }
  rotate(id) { const p = this.parts.get(id); if (!p || p.type === "uno" || p.type === "breadboard") return; this.snapshot(); p.rot = ((p.rot || 0) + 90) % 360; this.place(p); this.changed(false); this.drawWires(); }

  applyProps(p) {
    const el = p.w; if (!el) return;
    const pr = p.props;
    if (p.type === "led" || p.type === "pushbutton") el.color = pr.color || "red";
    if (p.type === "resistor") el.value = String(pr.value || "220");
    if (p.type === "7segment") el.color = pr.color || "red";
    if (p.type === "potentiometer") el.value = p.st.value ?? (p.st.value = 512);
  }

  /** موضع القطعة: أول طرف فيها على شبكة 0.1 بوصة، والدوران حوالين أول طرف */
  place(p, snap = true) {
    const p0 = p.local[0] || { x: 0, y: 0 };
    if (snap) { p.x = Math.round((p.x + p0.x) / PITCH) * PITCH - p0.x; p.y = Math.round((p.y + p0.y) / PITCH) * PITCH - p0.y; }
    Object.assign(p.el.style, { left: p.x + "px", top: p.y + "px", transformOrigin: `${p0.x}px ${p0.y}px`, transform: p.rot ? `rotate(${p.rot}deg)` : "" });
  }
  /** مكان الطرف على اللوحة */
  pinPos(p, q) {
    const p0 = p.local[0] || { x: 0, y: 0 }, a = (p.rot || 0) * Math.PI / 180, dx = q.x - p0.x, dy = q.y - p0.y;
    return [p.x + p0.x + dx * Math.cos(a) - dy * Math.sin(a), p.y + p0.y + dx * Math.sin(a) + dy * Math.cos(a)];
  }
  /** حرّك القطعة بحيث طرفها يقع على نقطة (مثلًا ثقب في لوحة التوصيل) */
  moveTo(id, pin, x, y) {
    const p = this.parts.get(id), q = p?.local.find(z => z.name === pin); if (!q) return;
    const [px, py] = this.pinPos(p, q); p.x += x - px; p.y += y - py; this.place(p, false);
  }
  /** مكان ثقب/طرف: holeAt("breadb1", "c5") */
  holeAt(id, pin) { const p = this.parts.get(id), q = p?.local.find(z => z.name === pin); return q ? this.pinPos(p, q) : null; }
  endPos(e) { const p = this.parts.get(e.p), q = p?.local.find(x => x.name === e.pin); return q ? this.pinPos(p, q) : null; }

  // ------------------------------------------------------------ الشبكات (بتتحسب لما الدائرة تتغير)
  topology() {
    const idx = new Map(), names = [];
    for (const p of this.parts.values()) for (const q of p.local) { idx.set(p.id + ":" + q.name, names.length); names.push(p.id + ":" + q.name); }
    const uf = new UF(names.length);
    const J = (a, b) => { const i = idx.get(a), j = idx.get(b); if (i != null && j != null) uf.join(i, j); };
    // جوه القطع
    for (const p of this.parts.values()) {
      const def = PARTS[p.type];
      const groups = p.type === "breadboard" ? bbShort() : def.short || [];
      for (const g of groups) for (let k = 1; k < g.length; k++) J(p.id + ":" + g[0], p.id + ":" + g[k]);
    }
    // الأسلاك
    for (const w of this.wires) J(w.a.p + ":" + w.a.pin, w.b.p + ":" + w.b.pin);
    // الأطراف اللي فوق بعض (رجل قطعة في ثقب لوحة التوصيل، أو في رأس الأردوينو مباشرة)
    const grid = new Map(), plugged = new Set();
    for (const p of this.parts.values()) for (const q of p.local) {
      const [x, y] = this.pinPos(p, q), key = Math.round(x / PITCH) + "," + Math.round(y / PITCH);
      (grid.get(key) || grid.set(key, []).get(key)).push({ id: p.id, n: p.id + ":" + q.name, x, y });
    }
    for (const list of grid.values()) for (let i = 0; i < list.length; i++) for (let j = i + 1; j < list.length; j++) {
      const a = list[i], b = list[j];
      if (a.id !== b.id && Math.hypot(a.x - b.x, a.y - b.y) < 4.5) { uf.join(idx.get(a.n), idx.get(b.n)); plugged.add(a.n); plugged.add(b.n); }
    }
    this.topo = { idx, names, uf, plugged };
    return this.topo;
  }
  node(id, pin) { return this.topo.idx.get(id + ":" + pin); }

  // ------------------------------------------------------------ التشغيل
  /** ربط الدائرة بلوحة شغالة (الشاشات والحساسات السريعة بتسمع أطراف الأردوينو مباشرة) */
  attach(uno) {
    this.uno = uno; this.running = true; this.root.classList.add("run");
    this.select(null);
    const t = this.topology(), U = this.parts.get("uno");
    const unoPinOf = (id, pin) => {   // طرف الأردوينو المتوصل بالطرف ده (لو فيه)
      const n = this.node(id, pin); if (n == null) return -1;
      const r = t.uf.find(n);
      for (const q of U.local) { const i = unoIndex(q.name); if (i >= 0 && t.uf.find(this.node("uno", q.name)) === r) return i; }
      return -1;
    };
    this.fast = [];
    for (const p of this.parts.values()) {
      p.st.lcd = null; p.st.angle = p.st.angle ?? 90;
      if (p.type === "lcd1602") {
        const pins = { rs: unoPinOf(p.id, "RS"), e: unoPinOf(p.id, "E"), d: [0, 1, 2, 3, 4, 5, 6, 7].map(i => unoPinOf(p.id, "D" + i)) };
        const lcd = p.st.lcd = new HD44780();
        if (pins.e >= 0) uno.watchers.add((pin, lv) => {
          if (pin !== pins.e || lv) return;   // الحافة الهابطة لـ E
          const rd = i => pins.d[i] >= 0 ? uno.read(pins.d[i]) : 0;
          const d8 = [0, 1, 2, 3, 4, 5, 6, 7].reduce((a, i) => a | (rd(i) << i), 0);
          lcd.strobe(pins.rs >= 0 ? uno.read(pins.rs) : 0, d8, d8 >> 4);
        });
      }
      if (p.type === "hc-sr04") {
        const trig = unoPinOf(p.id, "TRIG"), echo = unoPinOf(p.id, "ECHO");
        if (trig >= 0 && echo >= 0) {
          let rise = 0;
          uno.watchers.add((pin, lv, c) => {
            if (pin !== trig) return;
            if (lv) { rise = c; return; }
            if (c - rise < 8 * FREQ / 1e6 || !p.st.powered) return;   // نبضة TRIG لازم ≥ 10 ميكروثانية
            const cm = clamp(+p.st.cm || 100, 2, 400), width = cm * 58 * FREQ / 1e6;
            uno.cpu.addClockEvent(() => {
              uno.setDigital(echo, 1);
              uno.cpu.addClockEvent(() => uno.setDigital(echo, 0), Math.round(width));
            }, Math.round(450 * FREQ / 1e6));
          });
          this.fast.push(echo);
        }
      }
    }
    this.lastIn = new Map();
  }
  detach() {
    this.running = false; this.uno = null; this.root.classList.remove("run");
    this.sound(0);
    for (const p of this.parts.values()) this.render(p, null);
    this.setWarn([]);
  }

  /** حل الدائرة (كل إطار): قيم الشبكات ← أطراف الأردوينو الداخلة ← القطع */
  frame(sample, now) {
    const t = this.topo, uno = this.uno; if (!t || !uno) return;
    const uf = t.uf.clone(), N = t.names.length;
    // التوصيلات المتغيرة (زر مضغوط، مفتاح)
    for (const p of this.parts.values()) {
      const sw = PARTS[p.type].sw?.(p.st) || [];
      for (const [a, b] of sw) { const i = this.node(p.id, a), j = this.node(p.id, b); if (i != null && j != null) uf.join(i, j); }
    }
    const R = i => uf.find(i);
    const strong = new Map(), warn = [];
    const add = (node, v, who) => { if (node == null) return; const r = R(node); (strong.get(r) || strong.set(r, []).get(r)).push({ v, who }); };
    // مصادر الأردوينو: الطاقة + الأطراف الخارجة
    const U = this.parts.get("uno");
    add(this.node("uno", "5V"), 1, "5V"); add(this.node("uno", "3.3V"), 0.66, "3.3V");
    for (const g of ["GND.1", "GND.2", "GND.3"]) add(this.node("uno", g), 0, "GND");
    const outPin = new Map();   // شبكة ← طرف الأردوينو اللي بيغذيها
    for (let i = 0; i < 20; i++) if (sample[i].mode === "out") {
      const n = this.node("uno", UNO_PINS[i]); add(n, sample[i].duty, "D" + i); outPin.set(R(n), i);
    }
    const val = r => { const s = strong.get(r); return s ? s[0].v : null; };
    const V = (p, pin) => { const n = this.node(p.id, pin); return n == null ? null : val(R(n)); };
    // القطع اللي بتطلع جهد (بعد ما نعرف VCC/GND)
    for (const p of this.parts.values()) {
      const def = PARTS[p.type];
      if (def.power) p.st.powered = (V(p, "VCC") ?? 0) > 0.6 && V(p, "GND") === 0;
      if (!def.out || (def.power && !p.st.powered)) continue;
      const o = def.out(p.st, { VCC: V(p, "VCC"), GND: V(p, "GND") }, now);
      for (const k in o) add(this.node(p.id, k), o[k], p.id);
    }
    // تعارض (قصر): 5V مع GND أو طرف خارج HIGH مع GND…
    for (const [r, list] of strong) {
      if (list.length < 2) continue;
      const mx = Math.max(...list.map(x => x.v)), mn = Math.min(...list.map(x => x.v));
      if (mx - mn > 0.5) warn.push(`دائرة قصر: ${[...new Set(list.map(x => x.who))].join(" و ")} متصلان مباشرة.`);
    }
    // المقاومات: قيمة «ضعيفة» للشبكات اللي مفيهاش مصدر (سحب لأعلى/لأسفل، مقسّم جهد)
    const edges = [];
    for (const p of this.parts.values()) for (const [a, b, ohm] of PARTS[p.type].res?.(p.props) || []) {
      const i = this.node(p.id, a), j = this.node(p.id, b); if (i != null && j != null) edges.push([R(i), R(j), ohm]);
    }
    const weak = new Map();
    for (let it = 0; it < 6 && edges.length; it++) {
      const acc = new Map();
      for (const [a, b, ohm] of edges) for (const [x, y] of [[a, b], [b, a]]) {
        if (strong.has(x)) continue;
        const vy = strong.has(y) ? val(y) : weak.get(y);
        if (vy == null) continue;
        const s = acc.get(x) || acc.set(x, [0, 0]).get(x); s[0] += vy / ohm; s[1] += 1 / ohm;
      }
      for (const [x, [num, den]] of acc) weak.set(x, num / den);
    }
    const valN = n => { if (n == null) return null; const r = R(n); return strong.has(r) ? val(r) : weak.has(r) ? weak.get(r) : null; };
    const isStrong = n => n != null && strong.has(R(n));
    const VP = (p, pin) => valN(this.node(p.id, pin));
    // أطراف الأردوينو الداخلة
    for (let i = 0; i < 20; i++) {
      if (sample[i].mode === "out" || this.fast?.includes(i)) continue;
      const v = valN(this.node("uno", UNO_PINS[i]));
      const key = v == null ? "f" : v > 0.5 ? "1" : "0";
      if (this.lastIn.get(i) !== key) { this.lastIn.set(i, key); uno.setDigital(i, v == null ? null : v > 0.5); }
      if (i >= 14) uno.setAnalog(i - 14, v == null ? 0 : v * 5);
    }
    // القطع
    let hz = 0;
    for (const p of this.parts.values()) {
      const s = { on: true };
      if (p.type === "led") {
        const a = VP(p, "A"), c = VP(p, "C"), b = a != null && c != null ? clamp(a - c, 0, 1) : 0;
        s.b = b > 0.04 ? b : 0;
        if (s.b && isStrong(this.node(p.id, "A")) && isStrong(this.node(p.id, "C"))) warn.push(`الليد ${num(p.id)} متصل بدون مقاومة. في الدائرة الحقيقية يجب وضع مقاومة (مثل 220 Ω) حتى لا يتلف.`);
        if (a != null && c != null && c - a > 0.5) warn.push(`الليد ${num(p.id)} مقلوب: الرجل الطويلة (A) يجب أن تتصل بالجهد الأعلى.`);
      } else if (p.type === "rgb-led") {
        const com = VP(p, "COM"), an = p.props.common === "anode";
        s.c = ["R", "G", "B"].map(k => { const v = VP(p, k); return v == null || com == null ? 0 : clamp(an ? com - v : v - com, 0, 1); });
      } else if (p.type === "7segment") {
        const com = VP(p, "COM.1"), an = p.props.common === "anode";
        s.seg = ["A", "B", "C", "D", "E", "F", "G", "DP"].map(k => { const v = VP(p, k); return v == null || com == null ? 0 : (an ? com - v : v - com) > 0.3 ? 1 : 0; });
      } else if (p.type === "servo") {
        const pw = V(p, "V+"), g = V(p, "GND"), n = this.node(p.id, "PWM"), pin = n == null ? undefined : outPin.get(R(n));
        if (pw > 0.6 && g === 0 && pin != null && sample[pin].pulse > 300) p.st.angle = clamp((sample[pin].pulse - 544) / (2400 - 544) * 180, 0, 180);
        s.angle = p.st.angle;
      } else if (p.type === "buzzer") {
        const n1 = this.node(p.id, "1"), n2 = this.node(p.id, "2"), a = n2 == null ? undefined : outPin.get(R(n2)), g = valN(n1);
        s.hz = a != null && g === 0 ? sample[a].hz : 0;
        if (s.hz > 20 && s.hz < 20000) hz = s.hz;
      } else if (p.type === "lcd1602") {
        s.power = V(p, "VSS") === 0 && (V(p, "VDD") ?? 0) > 0.6;
      } else if (p.type === "photoresistor-sensor") {
        s.power = p.st.powered; s.d = p.st.powered && (VP(p, "DO") ?? 0) > 0.5;
      }
      this.render(p, s);
    }
    this.sound(hz);
    this.setWarn(warn);
  }

  /** عرض حالة القطعة (s = null ← مطفية) */
  render(p, s) {
    const el = p.w; if (!el) return;
    switch (p.type) {
      case "led": el.value = !!s?.b; el.brightness = s?.b ? 0.25 + 0.75 * s.b : 0; break;
      case "rgb-led": [el.ledRed, el.ledGreen, el.ledBlue] = s?.c || [0, 0, 0]; break;
      case "7segment": el.values = s?.seg || [0, 0, 0, 0, 0, 0, 0, 0]; break;
      case "servo": if (s) el.angle = s.angle; break;
      case "buzzer": el.hasSignal = !!(s?.hz > 20); break;
      case "photoresistor-sensor": el.ledPower = !!s?.power; el.ledDO = !!s?.d; break;
      case "lcd1602": {
        const lcd = p.st.lcd;
        el.backlight = !!s?.power;
        el.characters = s?.power && lcd ? lcd.chars() : new Uint8Array(32).fill(32);
        el.cursor = !!(s?.power && lcd?.on && lcd.cursor); el.blink = !!(s?.power && lcd?.on && lcd.blink);
        if (lcd) { el.cursorX = (lcd.addr & 0x3f) - lcd.shift; el.cursorY = lcd.addr >= 0x40 ? 1 : 0; }
        break;
      }
      case "uno": if (!s) { el.led13 = false; } break;
    }
  }

  sound(hz) {
    if (!hz) { if (this.osc) { this.osc.stop(); this.osc.disconnect(); this.osc = null; } return; }
    try {
      this.ac ||= new AudioContext();
      if (!this.osc) {
        this.osc = this.ac.createOscillator(); this.osc.type = "square";
        const g = this.ac.createGain(); g.gain.value = 0.04;
        this.osc.connect(g).connect(this.ac.destination); this.osc.start();
      }
      this.osc.frequency.setTargetAtTime(hz, this.ac.currentTime, 0.005);
    } catch { }
  }
  setWarn(list) {
    const k = [...new Set(list)].slice(0, 6);
    const s = k.join("\n");
    if (s === this._warnKey) return;
    this._warnKey = s; this.warn = k;
    this.opts.onWarn?.(k);
  }

  // ------------------------------------------------------------ الرسم والتفاعل
  bindElement(p) {
    const el = p.w;
    if (p.type === "pushbutton") {
      el.addEventListener("button-press", () => { p.st.pressed = true; });
      el.addEventListener("button-release", () => { p.st.pressed = false; });
    }
    if (p.type === "slide-switch") el.addEventListener("input", () => { p.st.value = el.value; });
    if (p.type === "potentiometer") el.addEventListener("input", () => { p.st.value = el.value; });
    if (p.type === "uno") el.addEventListener("button-press", () => this.opts.onReset?.());
  }

  bindStage() {
    const st = this.root;
    const toWorld = e => { const r = st.getBoundingClientRect(); return [(e.clientX - r.left - this.view.x) / this.view.k, (e.clientY - r.top - this.view.y) / this.view.k]; };
    this.toWorld = toWorld;
    let drag = null;
    // أثناء التعديل: الضغط على قطعة بيحركها (مش بيضغط الزر/يلف المقاومة)
    st.addEventListener("pointerdown", e => {
      if (e.button !== 0 && e.button !== 1) return;
      st.focus({ preventScroll: true });
      const pinEl = e.target.closest?.(".ard-pin");
      const partEl = e.target.closest?.(".ard-part");
      const wireEl = e.target.closest?.(".ard-wire");
      const [wx, wy] = toWorld(e);
      if (this.wiring) {
        e.preventDefault(); e.stopPropagation();
        if (pinEl) this.finishWire(pinEl.dataset.p, pinEl.dataset.pin);
        else this.wiring.pts.push(this.snapPt(wx, wy));
        return;
      }
      if (this.running) {
        if (partEl || pinEl) return;   // القطعة بتتعامل مع الماوس بنفسها (زر، مقاومة متغيرة…)
      } else if (pinEl && e.button === 0) {
        e.preventDefault(); e.stopPropagation();
        this.startWire(pinEl.dataset.p, pinEl.dataset.pin, wx, wy);
        return;
      } else if (wireEl && e.button === 0) {
        e.preventDefault(); e.stopPropagation();
        this.select({ wire: wireEl.dataset.id });
        return;
      } else if (partEl && e.button === 0) {
        e.preventDefault(); e.stopPropagation();
        const p = this.parts.get(partEl.dataset.id);
        this.select({ part: p.id });
        drag = { kind: "part", p, ox: wx - p.x, oy: wy - p.y, moved: false, before: JSON.stringify(this.toJSON()) };
        try { st.setPointerCapture(e.pointerId); } catch { }
        return;
      }
      if (!partEl) {
        if (!this.running) this.select(null);
        drag = { kind: "pan", sx: e.clientX, sy: e.clientY, vx: this.view.x, vy: this.view.y };
        try { st.setPointerCapture(e.pointerId); } catch { }
      }
    }, true);
    st.addEventListener("pointermove", e => {
      const [wx, wy] = toWorld(e);
      if (this.wiring) { this.wiring.mouse = [wx, wy]; this.drawWires(); }
      const pinEl = e.target.closest?.(".ard-pin");
      if (pinEl) this.showTip(pinEl, e); else this.tip.hidden = true;
      if (!drag) return;
      if (drag.kind === "pan") { this.view.x = drag.vx + e.clientX - drag.sx; this.view.y = drag.vy + e.clientY - drag.sy; this.applyView(); }
      else if (drag.kind === "part") {
        drag.p.x = wx - drag.ox; drag.p.y = wy - drag.oy; drag.moved = true;
        this.place(drag.p); this.drawWires(); this.drawPins();
      }
    });
    const end = () => {
      if (drag?.kind === "part" && drag.moved) { this.history.push(drag.before); this.future = []; this.changed(false); }
      drag = null;
    };
    st.addEventListener("pointerup", end);
    st.addEventListener("pointercancel", end);
    st.addEventListener("wheel", e => {
      e.preventDefault();
      const r = st.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top;
      const k2 = clamp(this.view.k * Math.pow(1.0015, -e.deltaY), 0.3, 4);
      this.view.x = mx - (mx - this.view.x) * k2 / this.view.k; this.view.y = my - (my - this.view.y) * k2 / this.view.k; this.view.k = k2;
      this.applyView();
    }, { passive: false });
    st.addEventListener("keydown", e => {
      if (e.target !== st) return;
      if (e.key === "Escape" && this.wiring) { e.preventDefault(); e.stopPropagation(); this.wiring = null; this.drawWires(); this.opts.onSel?.(this.sel); return; }
      if (this.running) return;
      if ((e.key === "Delete" || e.key === "Backspace") && this.sel) { e.preventDefault(); this.deleteSel(); }
      else if ((e.key === "r" || e.key === "R" || e.key === "ر") && this.sel?.part) { e.preventDefault(); this.rotate(this.sel.part); }
      else if (e.ctrlKey && (e.key === "z" || e.key === "Z")) { e.preventDefault(); this.undo(); }
      else if (e.ctrlKey && (e.key === "y" || e.key === "Y")) { e.preventDefault(); this.redo(); }
    });
  }
  snapPt(x, y) { return [Math.round(x / (PITCH / 2)) * (PITCH / 2), Math.round(y / (PITCH / 2)) * (PITCH / 2)]; }
  showTip(pinEl, e) {
    const p = this.parts.get(pinEl.dataset.p);
    this.tip.textContent = (p ? PARTS[p.type].name + " — " : "") + pinLabel(p?.type, pinEl.dataset.pin);
    const r = this.root.getBoundingClientRect();
    Object.assign(this.tip.style, { left: (e.clientX - r.left + 12) + "px", top: (e.clientY - r.top + 14) + "px" });
    this.tip.hidden = false;
  }

  startWire(p, pin, x, y) {
    this.wiring = { a: { p, pin }, pts: [], mouse: [x, y] };
    this.select(null);
    this.opts.onSel?.({ wiring: true });
    this.drawWires();
  }
  finishWire(p, pin) {
    const w = this.wiring; this.wiring = null;
    if (!w || (w.a.p === p && w.a.pin === pin)) { this.drawWires(); this.opts.onSel?.(null); return; }
    this.snapshot();
    const color = wireColor(w.a.pin) || wireColor(pin) || WIRE_COLORS[this.wires.length % 3][0];
    const wire = { id: "w" + (++this.seq), a: w.a, b: { p, pin }, pts: w.pts, color };
    this.wires.push(wire);
    this.select({ wire: wire.id });
    this.changed(false);
  }
  deleteSel() {
    if (!this.sel) return;
    this.snapshot();
    if (this.sel.wire) { this.wires = this.wires.filter(w => w.id !== this.sel.wire); this.sel = null; this.changed(false); this.opts.onSel?.(null); }
    else if (this.sel.part) { const p = this.parts.get(this.sel.part); if (p && !PARTS[p.type].fixed) { this.removePart(p.id); this.opts.onSel?.(null); } else this.history.pop(); }
  }
  setProp(id, k, v) {
    const p = this.parts.get(id); if (!p) return;
    this.snapshot(); p.props[k] = v; this.applyProps(p); this.changed(false);
  }
  setWireColor(id, c) { const w = this.wires.find(x => x.id === id); if (!w) return; this.snapshot(); w.color = c; this.changed(false); }

  select(s) {
    this.sel = s;
    for (const p of this.parts.values()) p.el.classList.toggle("sel", s?.part === p.id);
    this.drawWires();
    this.opts.onSel?.(s);
  }

  snapshot() { this.history.push(JSON.stringify(this.toJSON())); if (this.history.length > 80) this.history.shift(); this.future = []; }
  async undo() { const j = this.history.pop(); if (!j) return; this.future.push(JSON.stringify(this.toJSON())); await this.restore(j); }
  async redo() { const j = this.future.pop(); if (!j) return; this.history.push(JSON.stringify(this.toJSON())); await this.restore(j); }
  async restore(j) { const h0 = this.history, f0 = this.future, v = { ...this.view }; await this.load(JSON.parse(j)); this.history = h0; this.future = f0; this.view = v; this.applyView(); this.changed(false); }

  /** بعد أي تعديل: الشبكات + الرسم + الحفظ */
  changed(snap = true) {
    if (snap) this.snapshot();
    this.topology();
    this.drawWires(); this.drawPins();
    this.opts.onChange?.(this.toJSON());
  }

  drawPins() {
    const L = this.pinsLayer; L.textContent = "";
    const t = this.topo;
    for (const p of this.parts.values()) {
      if (p.type === "breadboard") continue;   // ثقوب لوحة التوصيل مرسومة فيها؛ بنضيفها تحت بشكل خفيف
      for (const q of p.local) {
        const [x, y] = this.pinPos(p, q), n = p.id + ":" + q.name;
        L.append(h("div", { cls: "ard-pin" + (t?.plugged.has(n) ? " in" : ""), "data-p": p.id, "data-pin": q.name, style: { left: x + "px", top: y + "px" } }));
      }
    }
    for (const p of this.parts.values()) if (p.type === "breadboard") for (const q of p.local) {
      const [x, y] = this.pinPos(p, q);
      L.append(h("div", { cls: "ard-pin bb", "data-p": p.id, "data-pin": q.name, style: { left: x + "px", top: y + "px" } }));
    }
  }
  drawWires() {
    const g = this.wsvg; g.textContent = "";
    const line = (pts, color, id, sel) => {
      const d = pts.map((q, i) => (i ? "L" : "M") + q[0].toFixed(1) + " " + q[1].toFixed(1)).join(" ");
      if (id) g.append(sv("path", { d, class: "ard-wire", "data-id": id, stroke: "transparent", "stroke-width": 9, fill: "none" }));
      if (sel) g.append(sv("path", { d, stroke: "#4dabf7", "stroke-width": 6, fill: "none", "stroke-linecap": "round", "stroke-linejoin": "round", "pointer-events": "none" }));
      g.append(sv("path", { d, stroke: "#0006", "stroke-width": 3.6, fill: "none", "stroke-linecap": "round", "stroke-linejoin": "round", "pointer-events": "none" }));
      g.append(sv("path", { d, stroke: color, "stroke-width": 2.4, fill: "none", "stroke-linecap": "round", "stroke-linejoin": "round", "pointer-events": "none" }));
    };
    for (const w of this.wires) {
      const a = this.endPos(w.a), b = this.endPos(w.b); if (!a || !b) continue;
      line([a, ...w.pts, b], w.color, w.id, this.sel?.wire === w.id);
    }
    if (this.wiring) {
      const a = this.endPos(this.wiring.a);
      if (a) line([a, ...this.wiring.pts, this.wiring.mouse], wireColor(this.wiring.a.pin) || "#2f9e44", null, false);
    }
  }

  /** الدائرة كصورة SVG (القطع بحالتها الحالية + الأسلاك) ← { svg, w, h } */
  toSVG(pad = 12) {
    const [x0, y0, x1, y1] = this.bounds(), w = x1 - x0 + pad * 2, hh = y1 - y0 + pad * 2;
    let s = `<g transform="translate(${(pad - x0).toFixed(1)} ${(pad - y0).toFixed(1)})">`;
    const order = [...this.parts.values()].sort((a, b) => (a.type === "breadboard" ? -1 : 0) - (b.type === "breadboard" ? -1 : 0));
    for (const p of order) {
      const p0 = p.local[0] || { x: 0, y: 0 };
      // كل قطعة صورة مستقلة: تنسيقات عناصر wokwi (CSS) ماتأثرش على باقي الرسم
      const inner = p.type === "breadboard" ? bbSvg()
        : `<image width="${p.size[0]}" height="${p.size[1]}" href="data:image/svg+xml;charset=utf-8,${encodeURIComponent(elementSvg(p.w))}"/>`;
      s += `<g transform="translate(${p.x.toFixed(1)} ${p.y.toFixed(1)})${p.rot ? ` rotate(${p.rot} ${p0.x} ${p0.y})` : ""}">${inner}</g>`;
    }
    for (const wr of this.wires) {
      const a = this.endPos(wr.a), b = this.endPos(wr.b); if (!a || !b) continue;
      const d = [a, ...wr.pts, b].map((q, i) => (i ? "L" : "M") + q[0].toFixed(1) + " " + q[1].toFixed(1)).join(" ");
      s += `<path d="${d}" fill="none" stroke="#0006" stroke-width="3.6" stroke-linecap="round" stroke-linejoin="round"/><path d="${d}" fill="none" stroke="${wr.color}" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/>`;
    }
    return { svg: s + "</g>", w, h: hh };
  }

  applyView() { this.world.style.transform = `translate(${this.view.x}px, ${this.view.y}px) scale(${this.view.k})`; }
  bounds() {
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const p of this.parts.values()) {
      const pts = [[p.x, p.y], [p.x + p.size[0], p.y + p.size[1]]];
      if (p.rot) for (const q of p.local) pts.push(this.pinPos(p, q));
      for (const [x, y] of pts) { x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); }
    }
    for (const w of this.wires) for (const [x, y] of w.pts) { x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); }
    return isFinite(x0) ? [x0, y0, x1, y1] : [0, 0, 300, 200];
  }
  fit() {
    const r = this.root.getBoundingClientRect(); if (!r.width) return;
    const [x0, y0, x1, y1] = this.bounds(), pad = 24;
    const k = clamp(Math.min((r.width - pad * 2) / (x1 - x0 || 1), (r.height - pad * 2) / (y1 - y0 || 1)), 0.3, 2.2);
    this.view = { k, x: (r.width - (x1 - x0) * k) / 2 - x0 * k, y: (r.height - (y1 - y0) * k) / 2 - y0 * k };
    this.applyView();
  }
  zoom(f) { const r = this.root.getBoundingClientRect(), mx = r.width / 2, my = r.height / 2, k2 = clamp(this.view.k * f, 0.3, 4); this.view.x = mx - (mx - this.view.x) * k2 / this.view.k; this.view.y = my - (my - this.view.y) * k2 / this.view.k; this.view.k = k2; this.applyView(); }
  /** مكان فاضي قريب من نص المساحة الظاهرة لقطعة جديدة */
  freeSpot() {
    const r = this.root.getBoundingClientRect();
    const cx = (r.width / 2 - this.view.x) / this.view.k, cy = (r.height / 2 - this.view.y) / this.view.k;
    const n = this.parts.size;
    return [cx - 20 + (n % 4) * 14, cy - 20 + (n % 3) * 14];
  }

  /** قيم أدوات التحكم (حساسات) */
  setCtl(id, v) { const p = this.parts.get(id); if (!p) return; const c = PARTS[p.type].ctl; p.st[c.k] = v; p.props[c.k] = v; this.opts.onChange?.(this.toJSON()); }
  motion(id) { const p = this.parts.get(id); if (!p) return; p.st.until = (this.opts.now?.() ?? 0) + 4000; }
}

function splitEnd(s) { const i = String(s).indexOf(":"); return [s.slice(0, i), s.slice(i + 1)]; }
export { arNum };
