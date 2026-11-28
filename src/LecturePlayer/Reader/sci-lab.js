// =====================================================================
//  أدوات المعمل الكيميائي (المرحلة الثانية): أوعية وأجهزة مرسومة بالكود،
//  السائل بمستوى ولون يتحددوا من الحقول، والتجهيزات الكاملة (تقطير، ترشيح، معايرة…)
//  مركّبة من نفس القطع. كل قطعة ← {s, w, h} من أعلى يسار (0,0).
// =====================================================================
import { text, line, path, circle, rect, poly, arrow } from "./sci-svg.js";

let uidN = 0;
const uid = () => "lq" + (++uidN) + Math.random().toString(36).slice(2, 6);
const G = { w: 2.6 };                                // خط الزجاج
const GLASS = "#e7f5ff";
export const LIQUIDS = [["#74c0fc", "ماء / محلول مائي"], ["#e7f5ff", "عديم اللون"], ["#1c7ed6", "أزرق (كبريتات النحاس)"], ["#862e9c", "بنفسجي (برمنجنات)"], ["#f783ac", "وردي (فينولفثالين في قاعدة)"],
  ["#e03131", "أحمر"], ["#ff922b", "برتقالي (ميثيل البرتقالي)"], ["#fcc419", "أصفر"], ["#40c057", "أخضر"], ["#8d6e63", "بني"], ["#f1f3f5", "عكر / حليبي"], ["#343a40", "داكن"]];

/** سائل جوه شكل داخلي: بنقص الشكل (clip) ونملا من المستوى لتحت. */
function liquid(clipD, yTop, yBot, o, x0 = 0, x1 = 400) {
  const lv = Math.max(0, Math.min(100, +o.level || 0));
  if (!lv) return "";
  const id = uid(), y = yBot - (yBot - yTop) * lv / 100;
  let s = `<clipPath id="${id}"><path d="${clipD}"/></clipPath><g clip-path="url(#${id})">`;
  s += `<rect x="${x0}" y="${y}" width="${x1 - x0}" height="${yBot - y + 4}" fill="${o.color || "#74c0fc"}" fill-opacity="0.75"/>`;
  s += line(x0, y, x1, y, { w: 1.6, stroke: shade(o.color || "#74c0fc") });
  if (o.ppt) s += `<rect x="${x0}" y="${yBot - (yBot - yTop) * 0.12}" width="${x1 - x0}" height="${(yBot - yTop) * 0.12 + 4}" fill="#f8f9fa" fill-opacity="0.95"/>` + [...Array(14)].map((_, i) => circle(x0 + 8 + ((i * 37) % (x1 - x0 - 10)), yBot - 4 - (i % 3) * 5, 2.4, { fill: "#adb5bd", w: 0 })).join("");
  if (o.bubbles) for (let i = 0; i < 9; i++) { const bx = x0 + (x1 - x0) * (0.2 + 0.6 * ((i * 0.37) % 1)), by = y + (yBot - y) * (0.15 + 0.75 * ((i * 0.53) % 1)); s += circle(bx, by, 2.5 + (i % 3) * 1.6, { w: 1.3, stroke: "#fff" }); }
  return s + "</g>";
}
function shade(hex) { const n = parseInt(hex.slice(1), 16); const f = v => Math.max(0, Math.round(v * 0.7)).toString(16).padStart(2, "0"); return "#" + f(n >> 16) + f((n >> 8) & 255) + f(n & 255); }
function grads(x, y1, y2, n, o = {}) { let s = ""; for (let i = 0; i <= n; i++) { const y = y1 + (y2 - y1) * i / n; s += line(x, y, x + (i % 5 === 0 ? 14 : 8), y, { w: 1.3 }); if (o.nums && i % 5 === 0 && i < n) s += text(x + 18, y, String(Math.round(o.nums[0] + (o.nums[1] - o.nums[0]) * i / n)), { size: 11, anchor: "start" }); } return s; }
const glass = d => path(d, { ...G, fill: GLASS, op: 0.35 });

// ---------------------------------------------------------------- الأوعية
const P = {
  beaker(o) {
    const d = "M 20 20 L 20 200 Q 20 214 34 214 L 146 214 Q 160 214 160 200 L 160 20", lip = "M 12 14 Q 20 14 20 22 M 160 22 Q 160 14 168 14";
    return { s: glass(d) + liquid(d + " Z", 30, 212, o, 20, 160) + path(d, G) + path(lip, G) + (o.grad ? grads(26, 60, 200, 14, {}) : ""), w: 180, h: 220 };
  },
  conical(o) {
    const d = "M 72 10 L 72 80 L 18 204 Q 14 214 26 214 L 154 214 Q 166 214 162 204 L 108 80 L 108 10";
    return { s: glass(d) + liquid(d + " Z", 80, 212, o, 10, 170) + path(d, G) + line(66, 10, 114, 10, G) + (o.grad ? grads(56, 150, 200, 5) : ""), w: 180, h: 220 };
  },
  round(o) {
    const d = "M 74 10 L 74 74 A 66 66 0 1 0 106 74 L 106 10";
    return { s: glass(d) + liquid(d + " Z", 74, 206, o, 10, 170) + path(d, G) + line(68, 10, 112, 10, G), w: 180, h: 216 };
  },
  volumetric(o) {
    const d = "M 80 10 L 80 120 C 80 132 20 150 20 190 Q 20 214 50 214 L 130 214 Q 160 214 160 190 C 160 150 100 132 100 120 L 100 10";
    return { s: glass(d) + liquid(d + " Z", 40, 212, o, 10, 170) + path(d, G) + line(74, 10, 106, 10, G) + line(78, 60, 102, 60, { w: 2 }), w: 180, h: 220 };
  },
  tube(o) {
    const d = "M 20 10 L 20 190 A 20 20 0 0 0 60 190 L 60 10";
    return { s: glass(d) + liquid(d + " Z", 20, 210, o, 10, 70) + path(d, G) + line(14, 10, 66, 10, G), w: 80, h: 216 };
  },
  cylinder(o) {
    const d = "M 40 14 L 40 236 L 80 236 L 80 14";
    return { s: glass(d) + liquid(d + " Z", 24, 236, o, 40, 80) + path(d, G) + line(34, 12, 86, 12, G) + path("M 18 236 L 102 236 L 102 250 L 18 250 Z", { w: 2.4, fill: "currentColor", op: 0.12 }) + (o.grad !== 0 ? grads(44, 36, 226, 20, { nums: [100, 0] }) : ""), w: 120, h: 256 };
  },
  burette(o) {
    const d = "M 30 10 L 30 270 L 50 270 L 50 10";
    let s = glass(d) + liquid(d + " Z", 14, 270, o, 30, 50) + path(d, G);
    s += grads(32, 24, 264, 24, {}) + path("M 30 270 L 36 290 L 44 290 L 50 270", G) + rect(26, 290, 28, 14, { w: 2.4, fill: "currentColor", op: 0.25, rx: 3 }) + line(14, 297, 66, 297, { w: 4 }) + path("M 37 304 L 38 330 L 42 330 L 43 304", G);
    return { s, w: 80, h: 336 };
  },
  pipette(o) {
    const d = "M 36 10 L 36 110 C 22 120 22 170 36 180 L 38 290 L 42 290 L 44 180 C 58 170 58 120 44 110 L 44 10";
    return { s: glass(d) + liquid(d + " Z", 10, 290, o, 10, 70) + path(d, G) + line(34, 70, 46, 70, { w: 2 }), w: 80, h: 296 };
  },
  funnel(o) {
    let s = path("M 10 14 L 150 14 L 92 104 L 92 190 L 68 190 L 68 104 Z", { ...G, fill: GLASS, op: 0.35 });
    if (o.filter) s += path("M 26 18 L 134 18 L 80 100 Z", { w: 1.6, fill: "#fff", op: 0.9, dash: "4 3" });
    s += liquid("M 10 14 L 150 14 L 92 104 L 68 104 Z", 14, 104, o, 10, 150);
    return { s, w: 160, h: 196 };
  },
  sepfunnel(o) {
    const d = "M 78 10 L 78 30 C 30 60 20 150 70 210 L 74 230 L 86 230 L 90 210 C 140 150 130 60 82 30 L 82 10";
    let s = glass(d + " Z");
    const id = uid();
    // طبقتين ما بيختلطوش: الماء تحت والزيت فوق
    s += `<clipPath id="${id}"><path d="${d} Z"/></clipPath><g clip-path="url(#${id})"><rect x="0" y="140" width="160" height="100" fill="${o.color || "#74c0fc"}" fill-opacity="0.75"/><rect x="0" y="80" width="160" height="60" fill="#fcc419" fill-opacity="0.6"/>${line(0, 140, 160, 140, { w: 1.4 })}</g>`;
    s += path(d, G) + rect(64, 230, 32, 12, { w: 2.2, fill: "currentColor", op: 0.25, rx: 3 }) + line(56, 236, 104, 236, { w: 4 }) + path("M 76 242 L 77 280 L 83 280 L 84 242", G) + rect(72, 0, 16, 12, { w: 2, fill: "currentColor", op: 0.25, rx: 2 });
    return { s, w: 160, h: 286 };
  },
  dish(o) {
    const d = "M 10 30 Q 90 150 170 30";
    return { s: glass(d) + liquid(d + " Z", 30, 92, o, 10, 170) + path(d, G) + line(4, 30, 16, 30, G), w: 180, h: 100 };
  },
  watch(o) { return { s: path("M 10 20 Q 90 70 170 20", { w: 3 }) + (o.level ? path("M 50 36 Q 90 52 130 36 Z", { w: 0, fill: o.color || "#74c0fc", op: 0.7 }) : ""), w: 180, h: 60 }; },
  crucible(o) {
    const d = "M 20 30 L 40 130 Q 42 140 52 140 L 108 140 Q 118 140 120 130 L 140 30";
    return { s: path(d + " Z", { w: 2.8, fill: "#f8f9fa" }) + liquid(d + " Z", 30, 140, o, 20, 140) + path(d, { w: 2.8 }), w: 160, h: 150 };
  },
  petri(o) {
    return { s: `<ellipse cx="100" cy="60" rx="88" ry="26" fill="${GLASS}" fill-opacity="0.4" stroke="currentColor" stroke-width="2.6"/>` + (o.level ? `<ellipse cx="100" cy="64" rx="78" ry="18" fill="${o.color || "#fcc419"}" fill-opacity="0.6"/>` : "") + path("M 12 60 L 12 76 A 88 26 0 0 0 188 76 L 188 60", G), w: 200, h: 110 };
  },
  thermometer(o) {
    const lv = Math.max(0, Math.min(100, +o.level || 40)), top = 20, bot = 230, y = bot - (bot - top) * lv / 100;
    let s = rect(28, 10, 24, 240, { w: 2.4, rx: 12, fill: GLASS, op: 0.4 }) + circle(40, 252, 18, { w: 2.4, fill: "#e03131" }) + rect(36, y, 8, 252 - y, { w: 0, fill: "#e03131" });
    s += grads(54, top, bot, 10, { nums: [100, 0] });
    return { s, w: 100, h: 276 };
  },
  dropper(o) {
    return { s: path("M 30 10 Q 20 10 20 30 L 20 70 L 40 70 L 40 30 Q 40 10 30 10 Z", { w: 2.4, fill: "#e03131", op: 0.6 }) + glass("M 22 70 L 24 170 L 30 190 L 36 170 L 38 70 Z") + liquid("M 22 70 L 24 170 L 30 190 L 36 170 L 38 70 Z", 70, 190, o, 20, 40) + (o.drop ? path("M 30 200 Q 24 212 30 216 Q 36 212 30 200 Z", { w: 0, fill: o.color || "#74c0fc" }) : ""), w: 60, h: 222 };
  },
  washbottle(o) {
    const d = "M 40 60 Q 30 60 30 74 L 30 210 Q 30 224 44 224 L 116 224 Q 130 224 130 210 L 130 74 Q 130 60 120 60 Z";
    return { s: path(d, { w: 2.6, fill: GLASS, op: 0.5 }) + liquid(d, 70, 224, o, 30, 130) + path(d, G) + rect(56, 44, 48, 16, { w: 2.4, fill: "currentColor", op: 0.25 }) + path("M 80 44 L 80 20 L 140 4", { w: 4 }), w: 160, h: 230 };
  },
  rod() { return { s: rect(10, 10, 14, 260, { w: 2.2, rx: 7, fill: GLASS, op: 0.5 }), w: 34, h: 280 }; },
  spatula() { return { s: path("M 10 30 Q 10 14 30 14 L 60 14 L 64 22 L 240 26 L 240 34 L 64 38 L 60 46 L 30 46 Q 10 46 10 30 Z", { w: 2.4, fill: "#ced4da" }), w: 250, h: 60 }; },
  mortar(o) {
    const d = "M 20 60 Q 20 170 100 170 Q 180 170 180 60 Z";
    return { s: path(d, { w: 2.8, fill: "#f1f3f5" }) + (o.level ? path("M 40 120 Q 100 150 160 120 Q 100 110 40 120", { w: 0, fill: o.color || "#adb5bd", op: 0.8 }) : "") + line(14, 60, 186, 60, { w: 3 }) + rect(80, 170, 40, 14, { w: 2.4, fill: "#f1f3f5" }) + path("M 120 110 L 176 6 Q 190 0 192 14 L 136 116 Q 128 126 120 110 Z", { w: 2.6, fill: "#dee2e6" }), w: 200, h: 190 };
  },
  utube(o) {
    const d = "M 20 10 L 20 170 A 50 50 0 0 0 120 170 L 120 10 L 96 10 L 96 170 A 26 26 0 0 1 44 170 L 44 10 Z";
    return { s: glass(d) + liquid(d, 20, 220, o, 10, 130) + path(d, G), w: 140, h: 230 };
  },
  testrack() {
    let s = rect(10, 100, 300, 18, { w: 2.4, fill: "#e9c46a", op: 0.8 }) + rect(10, 190, 300, 14, { w: 2.4, fill: "#e9c46a", op: 0.8 }) + line(16, 118, 16, 210, { w: 4 }) + line(304, 118, 304, 210, { w: 4 });
    [40, 100, 160, 220, 280].forEach((x, i) => { const c = ["#74c0fc", "#f783ac", "#40c057", "#fcc419", "#862e9c"][i]; s += path(`M ${x - 14} 30 L ${x - 14} 170 A 14 14 0 0 0 ${x + 14} 170 L ${x + 14} 30`, { w: 2.2, fill: GLASS, op: 0.4 }) + path(`M ${x - 13} ${110 + i * 8} L ${x - 13} 170 A 13 13 0 0 0 ${x + 13} 170 L ${x + 13} ${110 + i * 8} Z`, { w: 0, fill: c, op: 0.7 }); });
    return { s, w: 320, h: 214 };
  },
  holder() { return { s: path("M 10 40 L 200 40 L 200 54 L 10 54 Z", { w: 2.4, fill: "#e9c46a" }) + path("M 200 30 L 250 30 Q 266 47 250 64 L 200 64", { w: 3 }) + path("M 206 36 L 244 36 Q 254 47 244 58 L 206 58", { w: 2 }), w: 270, h: 90 }; },

  // ---------------------------------------------------------------- التسخين والحوامل
  bunsen(o) {
    let s = rect(40, 50, 24, 170, { w: 2.6, fill: "#adb5bd", op: 0.5 }) + path("M 10 220 L 94 220 L 94 236 L 10 236 Z", { w: 2.6, fill: "#868e96", op: 0.6 }) + rect(36, 150, 32, 18, { w: 2.2, fill: "#495057", op: 0.6 }) + path("M 64 214 L 110 214", { w: 6, stroke: "#f08c00" });
    if (o.flame !== 0) s += path("M 52 50 C 22 10 44 -20 52 -60 C 60 -20 82 10 52 50 Z", { w: 0, fill: "#4dabf7", op: 0.75 }) + path("M 52 46 C 38 20 48 0 52 -18 C 56 0 66 20 52 46 Z", { w: 0, fill: "#d0ebff", op: 0.9 });
    return { s: `<g transform="translate(0,64)">${s}</g>`, w: 120, h: 300 };
  },
  spirit(o) {
    let s = path("M 30 160 Q 10 160 14 130 Q 20 90 60 90 Q 100 90 106 130 Q 110 160 90 160 Z", { w: 2.6, fill: GLASS, op: 0.5 }) + path("M 22 140 Q 60 150 98 140 L 96 156 L 24 156 Z", { w: 0, fill: "#74c0fc", op: 0.6 }) + rect(50, 74, 20, 16, { w: 2.4, fill: "#868e96", op: 0.6 }) + line(60, 74, 60, 60, { w: 3 });
    if (o.flame !== 0) s += path("M 60 60 C 42 36 54 10 60 -10 C 66 10 78 36 60 60 Z", { w: 0, fill: "#ffa94d", op: 0.85 }) + path("M 60 56 C 52 40 57 28 60 16 C 63 28 68 40 60 56 Z", { w: 0, fill: "#fff3bf" });
    return { s: `<g transform="translate(0,20)">${s}</g>`, w: 120, h: 186 };
  },
  tripod() { return { s: rect(10, 10, 180, 8, { w: 2, fill: "#ced4da" }) + path("M 20 18 L 4 190 M 100 18 L 100 190 M 180 18 L 196 190", { w: 4 }) + [...Array(10)].map((_, i) => line(18 + i * 18, 10, 18 + i * 18, 18, { w: 1 })).join(""), w: 200, h: 196 }; },
  stand() {
    return { s: rect(10, 330, 160, 18, { w: 2.6, fill: "#868e96", op: 0.5, rx: 3 }) + rect(36, 10, 10, 322, { w: 2.4, fill: "#adb5bd", op: 0.6 }) + rect(30, 120, 22, 16, { w: 2.2, fill: "#495057", op: 0.6 }) + line(52, 128, 130, 128, { w: 5 }) + path("M 126 116 L 146 116 L 146 140 L 126 140", { w: 3 }), w: 180, h: 352, clamp: [136, 128] };
  },
  balance(o) {
    let s = path("M 10 120 L 230 120 L 220 170 L 20 170 Z", { w: 2.6, fill: "#dee2e6" }) + rect(60, 132, 120, 28, { w: 2.2, fill: "#212529", rx: 3 }) + text(120, 147, (o.reading || "0.00") + " g", { size: 18, fill: "#69db7c", bold: true }) + rect(50, 104, 140, 10, { w: 2.4, fill: "#adb5bd" }) + line(120, 114, 120, 120, { w: 4 });
    return { s, w: 240, h: 176 };
  },
  condenser(o) {
    let s = path("M 20 40 L 300 200 L 286 224 L 6 64 Z", { w: 2.6, fill: GLASS, op: 0.4 }) + line(0, 46, 316, 226, { w: 2 }) + line(-10, 54, 0, 46, { w: 2 });
    s += path("M 60 70 L 52 40 M 250 180 L 258 214", { w: 6, stroke: "#74c0fc" }) + text(40, 26, o.ar !== 0 ? "ماء خارج" : "water out", { size: 14 }) + text(270, 236, o.ar !== 0 ? "ماء داخل" : "water in", { size: 14 });
    return { s: `<g transform="translate(10,0)">${s}</g>`, w: 340, h: 250 };
  },
  electrolysis(o) {
    let s = P.beaker({ ...o, level: o.level || 75 }).s;
    s += rect(48, -20, 14, 200, { w: 2.2, fill: "#495057" }) + rect(118, -20, 14, 200, { w: 2.2, fill: "#495057" });
    s += path("M 55 -20 L 55 -60 L 125 -60 L 125 -20", { w: 2.2 }) + `<g transform="translate(60,-80)">${rect(0, 0, 60, 30, { w: 2.2, fill: "#fff" })}${line(22, 4, 22, 26, { w: 3 })}${line(34, 10, 34, 20, { w: 5 })}</g>`;
    s += text(55, -32, "−", { size: 20, bold: true }) + text(125, -32, "+", { size: 20, bold: true });
    if (o.bubbles) for (let i = 0; i < 6; i++) s += circle(44 + (i % 2) * 22, 150 - i * 18, 3, { w: 1.2 }) + circle(114 + (i % 2) * 22, 160 - i * 22, 3, { w: 1.2 });
    return { s: `<g transform="translate(0,90)">${s}</g>`, w: 180, h: 310 };
  }
};

// ---------------------------------------------------------------- التجهيزات المركّبة
const at = (part, x, y, sc = 1) => `<g transform="translate(${x},${y}) scale(${sc})">${part.s}</g>`;
const SETUPS = {
  filtration(o) {
    const st = P.stand(), f = P.funnel({ ...o, level: Math.min(o.level || 40, 60), filter: 1 }), b = P.beaker({ ...o, level: 25 });
    return { s: at(st, 0, 0) + at(f, 110, 40) + at(b, 100, 176, 0.85), w: 290, h: 360 };
  },
  titration(o) {
    const st = P.stand(), bu = P.burette({ level: o.level || 70, color: o.color2 || "#e7f5ff" }), fl = P.conical({ level: 30, color: o.color || "#f783ac" });
    return { s: at(st, 0, 0) + at(bu, 108, -30, 0.8) + at(fl, 96, 210, 0.66), w: 260, h: 360 };
  },
  heating(o) {
    const bn = P.bunsen({ flame: 1 }), tr = P.tripod(), bk = P.beaker(o);
    return { s: at(bn, 70, 64, 0.8) + at(tr, 10, 150, 1) + at(bk, 38, 4, 0.8) + (o.bubbles ? "" : ""), w: 220, h: 350 };
  },
  tubeheat(o) {
    const sp = P.spirit({ flame: 1 }), st = P.stand(), tb = P.tube(o);
    return { s: at(st, 0, 0) + `<g transform="translate(120,90) rotate(-30)">${tb.s}</g>` + at(sp, 150, 190), w: 300, h: 360 };
  },
  distill(o) {
    const fl = P.round(o), bn = P.bunsen({ flame: 1 }), cd = P.condenser(o), bk = P.beaker({ level: 30, color: o.color2 || "#e7f5ff" }), th = P.thermometer({ level: 60 });
    let s = at(bn, 40, 150, 0.8) + at(fl, 0, 40, 0.8) + at(cd, 100, 20, 0.8) + at(bk, 340, 200, 0.75) + at(th, 52, -110, 0.6);
    s += path("M 84 70 L 110 56", { w: 2.6 }) + path("M 84 78 L 112 63", { w: 2.6 });
    return { s: `<g transform="translate(0,110)">${s}</g>`, w: 480, h: 470 };
  },
  gas(o) {
    const fl = P.conical({ level: 35, color: o.color || "#74c0fc", bubbles: 1 });
    const trough = "M 120 130 L 120 270 L 420 270 L 420 130";
    const id = uid();
    let s = at(fl, 0, 90, 0.5);
    s += `<clipPath id="${id}"><path d="${trough} Z"/></clipPath><rect x="120" y="160" width="300" height="110" fill="#74c0fc" fill-opacity="0.6" clip-path="url(#${id})"/>` + path(trough, G);
    s += path("M 300 50 L 300 240 L 360 240 L 360 50 Z", { ...G, fill: GLASS, op: 0.3 }) + rect(302, 110, 56, 128, { w: 0, fill: "#74c0fc", op: 0.6 }) + path("M 300 50 L 360 50", { w: 4 });
    s += path("M 45 96 L 45 40 L 200 40 L 200 250 L 330 250 L 330 236", { w: 3 }) + rect(36, 92, 18, 10, { w: 2, fill: "currentColor", op: 0.3 }) + text(120, 26, o.ar !== 0 ? "الغاز الناتج" : "gas", { size: 15 });
    for (let i = 0; i < 5; i++) s += circle(320 + (i % 3) * 10, 220 - i * 22, 4, { w: 1.4, stroke: "#fff" });
    return { s, w: 430, h: 280 };
  }
};

/** [المعرّف، الاسم، الفئة، خصائص: l=سائل g=تدريج f=لهب b=فقاعات p=راسب] */
export const LAB = [
  ["beaker", "كأس زجاجية", "glass", "lgbp"], ["conical", "دورق مخروطي", "glass", "lgbp"], ["round", "دورق مستدير القاع", "glass", "lbp"], ["volumetric", "دورق قياسي", "glass", "l"],
  ["tube", "أنبوب اختبار", "glass", "lbp"], ["cylinder", "مخبار مدرج", "glass", "lg"], ["burette", "سحاحة", "glass", "l"], ["pipette", "ماصة", "glass", "l"],
  ["funnel", "قمع", "glass", "l"], ["sepfunnel", "قمع فصل", "glass", "l"], ["utube", "أنبوب على شكل U", "glass", "l"], ["dropper", "قطارة", "glass", "l"],
  ["dish", "طبق تبخير", "glass", "l"], ["watch", "زجاجة ساعة", "glass", "l"], ["petri", "طبق بتري", "glass", "l"], ["washbottle", "زجاجة غسيل", "glass", "l"],
  ["crucible", "بوتقة", "heat", "l"], ["bunsen", "موقد بنزن", "heat", "f"], ["spirit", "مصباح كحولي", "heat", "f"], ["tripod", "حامل ثلاثي وشبكة", "heat", ""],
  ["stand", "حامل وماسك", "tool", ""], ["thermometer", "ميزان حرارة", "tool", "l"], ["balance", "ميزان إلكتروني", "tool", ""], ["condenser", "مكثف ليبج", "tool", ""],
  ["rod", "ساق تحريك", "tool", ""], ["spatula", "ملعقة كيميائية", "tool", ""], ["mortar", "هاون ومدقة", "tool", "l"], ["testrack", "حامل أنابيب", "tool", ""],
  ["holder", "ماسك أنابيب", "tool", ""], ["electrolysis", "خلية تحليل كهربائي", "tool", "lb"],
  ["filtration", "تجهيزة الترشيح", "setup", "l"], ["titration", "تجهيزة المعايرة", "setup", "l"], ["heating", "تسخين كأس", "setup", "lb"], ["tubeheat", "تسخين أنبوب اختبار", "setup", "l"],
  ["distill", "تجهيزة التقطير", "setup", "l"], ["gas", "جمع غاز فوق الماء", "setup", ""]
];
export const LAB_GROUPS = [["glass", "الأوعية الزجاجية"], ["heat", "التسخين"], ["tool", "الأدوات والأجهزة"], ["setup", "تجهيزات كاملة"]];

export function drawLab(p) {
  const fn = P[p.type] || SETUPS[p.type] || P.beaker;
  const o = { level: +p.level || 0, color: p.liq || "#74c0fc", color2: p.liq2, grad: p.grad ? 1 : 0, bubbles: p.bubbles ? 1 : 0, ppt: p.ppt ? 1 : 0, flame: p.flame ?? 1, reading: p.reading, ar: 1 };
  const r = fn(o);
  let s = r.s, hh = r.h;
  if (p.label) { s += text(r.w / 2, r.h + 26, p.label, { size: 20, bold: true }); hh += 40; }
  return { inner: s, w: r.w, h: hh };
}
export const labHas = (type, flag) => (LAB.find(x => x[0] === type)?.[3] || "").includes(flag);
