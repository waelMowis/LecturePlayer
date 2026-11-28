// =====================================================================
//  الأشكال العلمية المرسومة بالكود (المرحلة الثانية): كل أداة = حقول + draw(params) ← {inner, w, h}.
//  الشكل بيتحط على السبورة صورة SVG فيكتور، والنقر المزدوج بيفتح نفس الحقول للتعديل.
// =====================================================================
import { drawLab, LAB, LIQUIDS, labHas } from "./sci-lab.js";
import { text, line, path, circle, rect, poly, arrow, arc, sector, rightMark, angOf, norm } from "./sci-svg.js";

const D2R = Math.PI / 180;
const num = (v, d) => { const x = parseFloat(String(v ?? "").replace(/[٠-٩]/g, c => "٠١٢٣٤٥٦٧٨٩".indexOf(c)).replace("−", "-")); return isFinite(x) ? x : d; };
const ACC = ["#e03131", "#1971c2", "#2f9e44", "#f08c00"];

// ---------------------------------------------------------------- رياضيات
/** نص الدالة ← تعبير JessieCode: 3x ← 3*x، س ← x، ln ← log، π ← PI. */
export function jsExpr(s) {
  let e = String(s || "").trim().replace(/^(?:y|ص|f\(x\)|د\(س\))\s*=\s*/i, "");
  e = e.replace(/[٠-٩]/g, c => "٠١٢٣٤٥٦٧٨٩".indexOf(c)).replace(/س/g, "x").replace(/[−–]/g, "-").replace(/×/g, "*").replace(/÷/g, "/").replace(/π|\bpi\b/gi, "PI").replace(/\be\b/g, "EULER").replace(/√/g, "sqrt").replace(/\bln\b/g, "log").replace(/\|([^|]+)\|/g, "abs($1)").replace(/²/g, "^2").replace(/³/g, "^3");
  e = e.replace(/(\d)\s*([a-zA-Z(])/g, "$1*$2").replace(/\)\s*([a-zA-Z0-9(])/g, ")*$1").replace(/\bx\s*\(/g, "x*(").replace(/\bx\s*x\b/g, "x*x");
  return e;
}

const SUP = { "0": "⁰", "1": "¹", "2": "²", "3": "³", "4": "⁴", "5": "⁵", "6": "⁶", "7": "⁷", "8": "⁸", "9": "⁹", "-": "⁻", "x": "ˣ", "n": "ⁿ" };
/** شكل مقروء للدالة في مفتاح الرسم: x^2 ← x²، 3*x ← 3x. */
export const pretty = f => String(f).replace(/\^(?:\((-?[0-9xn]+)\)|(-?[0-9]+|[xn])(?![\w(]))/g, (m, a, b) => [...(a || b)].map(c => SUP[c] || c).join("")).replace(/(\d)\s*\*\s*(?=[a-zA-Z(])/g, "$1").replace(/\*/g, "·").replace(/\bsqrt\b/g, "√").replace(/\bpi\b|PI/g, "π");

let jxg = null;
export function loadJSXGraph() {
  if (jxg) return jxg;
  jxg = new Promise((res, rej) => {
    const s = document.createElement("script");
    s.src = "lib/sci/jsxgraph/jsxgraphcore.js";
    s.onload = () => { window.JXG.Options.text.display = "internal"; res(window.JXG); };
    s.onerror = () => { jxg = null; rej(new Error("تعذّر تحميل أداة الرسم البياني")); };
    document.head.appendChild(s);
  });
  return jxg;
}

async function drawGraph(p, color) {
  const JXG = await loadJSXGraph();
  const W = 560, H = Math.round(560 * (p.ratio === "wide" ? 0.62 : 0.85));
  const host = document.createElement("div");
  host.style.cssText = `position:absolute;left:-10000px;top:0;width:${W}px;height:${H}px;direction:ltr`;
  document.body.appendChild(host);
  host.id = "sci-jxg-" + Math.random().toString(36).slice(2);
  const xmin = num(p.xmin, -10), xmax = num(p.xmax, 10), ymin = num(p.ymin, -8), ymax = num(p.ymax, 8);
  if (!(xmax > xmin && ymax > ymin)) { host.remove(); throw new Error("المدى غير صحيح: الحد الأكبر يجب أن يزيد على الأصغر"); }
  const brd = JXG.JSXGraph.initBoard(host.id, {
    boundingbox: [xmin, ymax, xmax, ymin], axis: true, grid: !!p.grid, showNavigation: false, showCopyright: false, keepAspectRatio: false,
    pan: { enabled: false }, zoom: { enabled: false },
    defaultAxes: { x: { strokeColor: "#555", ticks: { label: { fontSize: 13 }, strokeColor: "#999" } }, y: { strokeColor: "#555", ticks: { label: { fontSize: 13 }, strokeColor: "#999" } } }
  });
  try {
    const fs = [p.f1, p.f2, p.f3].map(x => (x || "").trim());
    fs.forEach((f, i) => {
      if (!f) return;
      let fn;
      try { fn = brd.jc.snippet(jsExpr(f), true, "x", true); fn(1); } catch (e) { throw new Error("الدالة «" + f + "» غير مفهومة"); }
      const c = i === 0 ? color : ACC[i];
      brd.create("functiongraph", [fn], { strokeColor: c, strokeWidth: 3, highlight: false });
      if (p.legend) brd.create("text", [xmin + (xmax - xmin) * 0.03, ymax - (ymax - ymin) * (0.07 + i * 0.08), (i === 0 ? "f(x) = " : i === 1 ? "g(x) = " : "h(x) = ") + pretty(f)], { fontSize: 16, strokeColor: c, highlight: false, parse: false, useMathJax: false, useKatex: false });
    });
    const pts = String(p.points || "").match(/\(\s*[-−\d.٠-٩]+\s*[,،]\s*[-−\d.٠-٩]+\s*\)/g) || [];
    pts.forEach((t, i) => {
      const [a, b] = t.replace(/[()]/g, "").split(/[,،]/).map(v => num(v, 0));
      brd.create("point", [a, b], { name: String.fromCharCode(65 + i), size: 4, fillColor: color, strokeColor: color, label: { fontSize: 16, offset: [8, 10] }, fixed: true, highlight: false });
    });
    const svg = brd.renderer.svgRoot.cloneNode(true);
    // نشيل العناصر المخفية وطبقات التفاعل
    svg.querySelectorAll("foreignObject").forEach(e => e.remove());
    const inner = `<rect x="0" y="0" width="${W}" height="${H}" fill="#fff" stroke="#ced4da" stroke-width="1"/>` + [...svg.childNodes].map(n => new XMLSerializer().serializeToString(n)).join("");
    return { inner: inner.replace(/ xmlns="[^"]*"/g, ""), w: W, h: H };
  } finally { JXG.JSXGraph.freeBoard(brd); host.remove(); }
}

function numberLine(p) {
  const a = Math.round(num(p.from, -5)), b = Math.round(num(p.to, 5)), step = Math.max(1, Math.round(num(p.step, 1)));
  if (!(b > a)) throw new Error("نهاية الخط يجب أن تكون أكبر من بدايته");
  const cnt = (b - a), W = Math.min(900, Math.max(360, cnt * 48)), x0 = 30, x1 = W - 30, y = 50;
  const X = v => x0 + (v - a) / (b - a) * (x1 - x0);
  let s = arrow(x0 - 18, y, x1 + 22, y, { w: 2.5, head: 12 }) + arrow(x1 + 18, y, x0 - 22, y, { w: 2.5, head: 12 }).replace(/<line[^>]*\/>/, "");
  for (let v = a; v <= b; v++) {
    const big = (v - a) % step === 0;
    s += line(X(v), y - (big ? 10 : 6), X(v), y + (big ? 10 : 6), { w: 2 });
    if (big) s += text(X(v), y + 30, p.ar ? String(v).replace(/\d/g, d => "٠١٢٣٤٥٦٧٨٩"[d]).replace("-", "−") : String(v).replace("-", "−"), { size: 18 });
  }
  // فترة: [أ، ب] بدايرة مقفولة/مفتوحة
  if (p.interval) {
    const ia = num(p.ia, a + 1), ib = num(p.ib, b - 1);
    s += line(X(ia), y, X(ib), y, { w: 7, stroke: "#e03131" });
    s += circle(X(ia), y, 8, { fill: p.iac ? "#e03131" : "#fff", stroke: "#e03131", w: 2.5 });
    s += circle(X(ib), y, 8, { fill: p.ibc ? "#e03131" : "#fff", stroke: "#e03131", w: 2.5 });
  }
  const marks = String(p.marks || "").split(/[,،\s]+/).filter(Boolean).map(v => num(v, NaN)).filter(isFinite);
  for (const v of marks) s += circle(X(v), y, 7, { fill: "currentColor", w: 0 });
  return { inner: s, w: W, h: 90 };
}

function polygon(p) {
  const k = Math.min(12, Math.max(3, Math.round(num(p.n, 6)))), R = 150, cx = 180, cy = 180;
  const pts = [...Array(k)].map((_, i) => { const a = (-90 + i * 360 / k) * D2R; return [cx + R * Math.cos(a), cy + R * Math.sin(a)]; });
  let s = poly(pts, { w: 3, fill: p.fill ? "currentColor" : "none", op: p.fill ? 0.12 : null });
  if (p.diag) for (let i = 0; i < k; i++) for (let j = i + 2; j < k; j++) if (!(i === 0 && j === k - 1)) s += line(...pts[i], ...pts[j], { w: 1.5, dash: "6 5" });
  if (p.center) { s += circle(cx, cy, 3.5, { fill: "currentColor", w: 0 }); s += line(cx, cy, ...pts[0], { w: 1.8, dash: "5 4" }); s += text(cx + 10, (cy + pts[0][1]) / 2, "r", { size: 20, anchor: "start", italic: true }); }
  if (p.labels) pts.forEach((q, i) => { const u = norm([q[0] - cx, q[1] - cy]); s += text(q[0] + u[0] * 22, q[1] + u[1] * 22, p.ar ? "أبجدهوزحطيكل"[i] : String.fromCharCode(65 + i), { size: 20, bold: true }); });
  if (p.side) { const m = [(pts[0][0] + pts[1][0]) / 2, (pts[0][1] + pts[1][1]) / 2], u = norm([m[0] - cx, m[1] - cy]); s += text(m[0] + u[0] * 20, m[1] + u[1] * 20, p.side, { size: 18 }); }
  return { inner: s, w: 360, h: 360 };
}

function circleParts(p) {
  const cx = 170, cy = 170, R = 140;
  let s = circle(cx, cy, R, { w: 3 });
  if (p.sector) s += sector(cx, cy, R, 20, 20 + num(p.angle, 60), { fill: "currentColor", op: 0.15, w: 2 });
  if (p.sector) s += arc(cx, cy, 34, 20, 20 + num(p.angle, 60), { w: 2 }) + text(cx + 52 * Math.cos(-(20 + num(p.angle, 60) / 2) * D2R), cy + 52 * Math.sin(-(20 + num(p.angle, 60) / 2) * D2R), (p.ar ? String(num(p.angle, 60)).replace(/\d/g, d => "٠١٢٣٤٥٦٧٨٩"[d]) : num(p.angle, 60)) + "°", { size: 16 });
  if (p.radius) { s += line(cx, cy, cx + R * Math.cos(-20 * D2R), cy + R * Math.sin(-20 * D2R), { w: 2.2 }); s += text(cx + R * 0.55 * Math.cos(-20 * D2R), cy + R * 0.55 * Math.sin(-20 * D2R) - 14, p.ar ? "نق" : "r", { size: 18, italic: !p.ar }); }
  if (p.diameter) { s += line(cx - R, cy, cx + R, cy, { w: 2.2, dash: p.radius ? "7 5" : null }); s += text(cx - R * 0.5, cy + 18, p.ar ? "ق" : "d", { size: 18, italic: !p.ar }); }
  if (p.chord) { const a1 = 200 * D2R, a2 = 300 * D2R; s += line(cx + R * Math.cos(a1), cy - R * Math.sin(a1), cx + R * Math.cos(a2), cy - R * Math.sin(a2), { w: 2.2 }); s += text(cx + R * 0.62 * Math.cos(250 * D2R), cy - R * 0.62 * Math.sin(250 * D2R) - 4, p.ar ? "وتر" : "chord", { size: 15 }); }
  if (p.tangent) { const a = 110 * D2R, tp = [cx + R * Math.cos(a), cy - R * Math.sin(a)], u = [Math.sin(a), Math.cos(a)]; s += line(tp[0] - u[0] * 100, tp[1] - u[1] * 100, tp[0] + u[0] * 95, tp[1] + u[1] * 95, { w: 2.2 }); s += rightMark(tp, [cx, cy], [tp[0] + u[0] * 50, tp[1] + u[1] * 50], 13) + line(cx, cy, ...tp, { w: 1.5, dash: "5 4" }); s += text(tp[0] + u[0] * 70, tp[1] + u[1] * 70 + 22, p.ar ? "مماس" : "tangent", { size: 15 }); }
  if (p.center) { s += circle(cx, cy, 4, { fill: "currentColor", w: 0 }); s += text(cx - 14, cy - 14, p.ar ? "م" : "O", { size: 18, bold: true }); }
  return { inner: `<g transform="translate(0,${p.tangent ? 14 : 0})">${s}</g>`, w: 340, h: 340 + (p.tangent ? 14 : 0) };
}

function triangle(p) {
  const t = p.type || "right";
  const P = { right: [[60, 300], [360, 300], [60, 80]], equi: [[50, 310], [370, 310], [210, 33]], iso: [[60, 310], [360, 310], [210, 50]], scalene: [[40, 300], [380, 300], [140, 80]], obtuse: [[40, 300], [260, 300], [380, 120]] }[t] || [[60, 300], [360, 300], [60, 80]];
  const [A, B, C] = P;
  let s = poly(P, { w: 3, fill: p.fill ? "currentColor" : "none", op: p.fill ? 0.12 : null });
  const names = p.ar ? ["أ", "ب", "ج"] : ["A", "B", "C"];
  const cen = [(A[0] + B[0] + C[0]) / 3, (A[1] + B[1] + C[1]) / 3];
  if (p.labels) P.forEach((q, i) => { const u = norm([q[0] - cen[0], q[1] - cen[1]]); s += text(q[0] + u[0] * 24, q[1] + u[1] * 24, names[i], { size: 22, bold: true }); });
  if (t === "right") s += rightMark(A, B, C, 18);
  if (p.angles) [[A, B, C], [B, C, A], [C, A, B]].forEach(([v, a, b], i) => {
    if (t === "right" && i === 0) return;
    let a1 = angOf(v, a), a2 = angOf(v, b); if (((a2 - a1) % 360 + 360) % 360 > 180) [a1, a2] = [a2, a1];
    if (a2 < a1) a2 += 360;
    s += arc(...v, 30, a1, a2, { w: 1.8 });
  });
  if (t === "equi" || t === "iso") { const tick = (a, b) => { const m = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2], u = norm([b[0] - a[0], b[1] - a[1]]), v = [-u[1], u[0]]; return line(m[0] - v[0] * 9, m[1] - v[1] * 9, m[0] + v[0] * 9, m[1] + v[1] * 9, { w: 2 }); }; s += tick(A, C) + tick(B, C) + (t === "equi" ? tick(A, B) : ""); }
  if (p.sides) { const sideL = (a, b, l) => { const m = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2], u = norm([m[0] - cen[0], m[1] - cen[1]]); return text(m[0] + u[0] * 22, m[1] + u[1] * 22, l, { size: 19, italic: !p.ar }); }; const sl = p.ar ? ["أ", "ب", "ج"] : ["a", "b", "c"]; s += sideL(B, C, sl[0]) + sideL(A, C, sl[1]) + sideL(A, B, sl[2]); }
  if (p.height && t !== "right") { const foot = [C[0], A[1]]; if (foot[0] > B[0]) s += line(...B, ...foot, { w: 1.6, dash: "4 4" }); s += line(...C, ...foot, { w: 2, dash: "7 5" }) + rightMark(foot, C, foot[0] > B[0] ? A : B, 13) + text(C[0] + 14, (C[1] + foot[1]) / 2, p.ar ? "ع" : "h", { size: 19, italic: !p.ar }); }
  return { inner: s, w: 420, h: 340 };
}

function solid(p) {
  const t = p.type || "cube", hid = p.hidden !== false && p.hidden !== 0, D = "7 6";
  const lab = (x, y, s) => p.dims ? text(x, y, s, { size: 19, italic: true }) : "";
  let s = "";
  if (t === "cube" || t === "cuboid") {
    const w = t === "cube" ? 200 : 260, h = t === "cube" ? 200 : 150, dx = 80, dy = -60, x = 40, y = 110;
    const F = [[x, y], [x + w, y], [x + w, y + h], [x, y + h]], Bk = F.map(q => [q[0] + dx, q[1] + dy]);
    s += poly(F, { w: 3 }) + path(`M ${F[0][0]} ${F[0][1]} L ${Bk[0][0]} ${Bk[0][1]} L ${Bk[1][0]} ${Bk[1][1]} L ${Bk[2][0]} ${Bk[2][1]} L ${F[2][0]} ${F[2][1]} M ${F[1][0]} ${F[1][1]} L ${Bk[1][0]} ${Bk[1][1]}`, { w: 3 });
    if (hid) s += path(`M ${Bk[3][0]} ${Bk[3][1]} L ${Bk[0][0]} ${Bk[0][1]} M ${Bk[3][0]} ${Bk[3][1]} L ${Bk[2][0]} ${Bk[2][1]} M ${Bk[3][0]} ${Bk[3][1]} L ${F[3][0]} ${F[3][1]}`, { w: 2, dash: D });
    s += lab(x + w / 2, y + h + 24, t === "cube" ? "a" : "l") + lab(x + w + dx / 2 + 16, y + h + dy / 2 + 6, t === "cube" ? "a" : "w") + lab(x - 18, y + h / 2, t === "cube" ? "a" : "h");
    return { inner: s, w: x + w + dx + 30, h: y + h + 40 };
  }
  if (t === "cyl" || t === "cone") {
    const cx = 160, rx = 110, ry = 32, top = 50, bot = 320;
    if (t === "cyl") {
      s += `<ellipse cx="${cx}" cy="${top}" rx="${rx}" ry="${ry}" fill="none" stroke="currentColor" stroke-width="3"/>`;
      s += line(cx - rx, top, cx - rx, bot, { w: 3 }) + line(cx + rx, top, cx + rx, bot, { w: 3 });
    } else s += line(cx - rx, bot, cx, top, { w: 3 }) + line(cx + rx, bot, cx, top, { w: 3 });
    s += path(`M ${cx - rx} ${bot} A ${rx} ${ry} 0 0 0 ${cx + rx} ${bot}`, { w: 3 });
    s += path(`M ${cx - rx} ${bot} A ${rx} ${ry} 0 0 1 ${cx + rx} ${bot}`, { w: 2, dash: hid ? D : null });
    if (p.dims) { s += line(cx, bot, cx + rx, bot, { w: 2, dash: "4 4" }) + circle(cx, bot, 3, { fill: "currentColor", w: 0 }) + lab(cx + rx / 2, bot - 14, "r"); s += line(cx, t === "cyl" ? top : top, cx, bot, { w: 2, dash: "4 4" }) + lab(cx - 18, (top + bot) / 2, "h"); }
    return { inner: s, w: 320, h: bot + ry + 10 };
  }
  if (t === "sphere") {
    const cx = 160, cy = 160, R = 140;
    s += circle(cx, cy, R, { w: 3 }) + path(`M ${cx - R} ${cy} A ${R} ${R * 0.3} 0 0 0 ${cx + R} ${cy}`, { w: 2.2 }) + path(`M ${cx - R} ${cy} A ${R} ${R * 0.3} 0 0 1 ${cx + R} ${cy}`, { w: 1.8, dash: hid ? D : null });
    if (p.dims) s += line(cx, cy, cx + R, cy, { w: 2, dash: "4 4" }) + circle(cx, cy, 3.5, { fill: "currentColor", w: 0 }) + lab(cx + R / 2, cy - 14, "r");
    return { inner: s, w: 320, h: 320 };
  }
  if (t === "pyramid") {
    const A = [40, 290], B = [260, 290], C = [340, 220], Dd = [120, 220], T = [190, 30];
    s += path(`M ${A} L ${B} L ${C} L ${T} Z M ${A} L ${T} L ${B}`, { w: 3 });
    if (hid) s += path(`M ${A} L ${Dd} L ${C} M ${Dd} L ${T}`, { w: 2, dash: D });
    if (p.dims) { const base = [190, 255]; s += line(...T, ...base, { w: 2, dash: "4 4" }) + lab(204, 150, "h"); }
    return { inner: s, w: 380, h: 320 };
  }
  // منشور ثلاثي
  const F = [[40, 300], [240, 300], [140, 130]], dx = 120, dy = -70, Bk = F.map(q => [q[0] + dx, q[1] + dy]);
  s += poly(F, { w: 3 }) + path(`M ${F[1]} L ${Bk[1]} L ${Bk[2]} L ${F[2]} M ${Bk[2]} L ${F[2]}`, { w: 3 });
  if (hid) s += path(`M ${F[0]} L ${Bk[0]} L ${Bk[1]} M ${Bk[0]} L ${Bk[2]}`, { w: 2, dash: D });
  return { inner: s, w: 380, h: 320 };
}

function fraction(p) {
  const nn = Math.min(24, Math.max(1, Math.round(num(p.n, 4)))), k = Math.min(nn, Math.max(0, Math.round(num(p.k, 1))));
  let s = "";
  if (p.shape === "bar") {
    const W = 420, H = 80, w = W / nn;
    for (let i = 0; i < nn; i++) s += rect(10 + i * w, 10, w, H, { w: 2.5, fill: i < k ? "currentColor" : "none", op: i < k ? 0.35 : null });
    if (p.label) s += text(10 + W / 2, H + 50, p.ar ? `${"٠١٢٣٤٥٦٧٨٩"[k] ?? k} / ${nn}` : `${k} / ${nn}`, { size: 26, bold: true });
    return { inner: s, w: W + 20, h: H + (p.label ? 80 : 20) };
  }
  const cx = 150, cy = 150, R = 130;
  for (let i = 0; i < nn; i++) if (nn > 1) s += sector(cx, cy, R, 90 - (i + 1) * 360 / nn, 90 - i * 360 / nn, { w: 2.5, fill: i < k ? "currentColor" : "none", op: i < k ? 0.35 : null });
  if (nn === 1) s += circle(cx, cy, R, { w: 2.5, fill: k ? "currentColor" : "none", op: k ? 0.35 : null });
  if (p.label) s += text(cx, cy * 2 + 26, `${k} / ${nn}`, { size: 26, bold: true });
  return { inner: s, w: 300, h: 300 + (p.label ? 50 : 0) };
}

function angle(p) {
  const deg = Math.max(1, Math.min(359, num(p.deg, 45))), O = [60, 260], L = 300;
  const B = [O[0] + L * Math.cos(-deg * D2R), O[1] + L * Math.sin(-deg * D2R)];
  let s = arrow(...O, O[0] + L, O[1], { w: 3 }) + arrow(...O, ...B, { w: 3 });
  s += deg === 90 ? rightMark(O, [O[0] + 50, O[1]], B, 22) : arc(...O, 50, 0, deg, { w: 2 });
  if (p.show) { const m = deg / 2 * D2R; s += text(O[0] + 82 * Math.cos(-m), O[1] + 82 * Math.sin(-m), (p.ar ? String(deg).replace(/\d/g, d => "٠١٢٣٤٥٦٧٨٩"[d]) : deg) + "°", { size: 22, bold: true }); }
  s += circle(...O, 4, { fill: "currentColor", w: 0 });
  const minX = Math.min(0, B[0] - 20), minY = Math.min(0, B[1] - 20);
  return { inner: `<g transform="translate(${-minX},${-minY})">${s}</g>`, w: Math.max(O[0] + L + 20, B[0] + 20) - minX, h: 300 - minY };
}

// ---------------------------------------------------------------- فيزياء
function vector(p) {
  const deg = num(p.deg, 35), L = Math.max(60, Math.min(400, num(p.len, 260))), O = [70, 330];
  const E = [O[0] + L * Math.cos(-deg * D2R), O[1] + L * Math.sin(-deg * D2R)];
  let s = "";
  if (p.axes) s += arrow(O[0] - 30, O[1], O[0] + L + 50, O[1], { w: 1.6, stroke: "#868e96", head: 10 }) + arrow(O[0], O[1] + 30, O[0], O[1] - L - 40, { w: 1.6, stroke: "#868e96", head: 10 }) + text(O[0] + L + 50, O[1] + 20, "x", { size: 17, italic: true, fill: "#868e96" }) + text(O[0] - 16, O[1] - L - 40, "y", { size: 17, italic: true, fill: "#868e96" });
  if (p.comps) {
    s += arrow(...O, E[0], O[1], { w: 2.4, dash: "8 5", stroke: "#1971c2" }) + arrow(E[0], O[1], ...E, { w: 2.4, dash: "8 5", stroke: "#2f9e44" });
    s += text((O[0] + E[0]) / 2, O[1] + 22, (p.name || "F") + "x", { size: 19, italic: true, fill: "#1971c2" }) + text(E[0] + 26, (O[1] + E[1]) / 2, (p.name || "F") + "y", { size: 19, italic: true, fill: "#2f9e44" });
  }
  s += arrow(...O, ...E, { w: 3.6, head: 18 });
  s += text((O[0] + E[0]) / 2 - 18 * Math.sin(deg * D2R) - 10, (O[1] + E[1]) / 2 - 18 * Math.cos(deg * D2R) - 6, p.name || "F", { size: 24, bold: true, italic: true });
  if (p.arc) s += arc(...O, 48, 0, deg, { w: 2 }) + text(O[0] + 66 * Math.cos(-deg / 2 * D2R), O[1] + 66 * Math.sin(-deg / 2 * D2R), "θ", { size: 20, italic: true });
  return { inner: s, w: O[0] + L + 80, h: 380 };
}

function freeBody(p) {
  const inc = Math.max(0, Math.min(60, num(p.incline, 0))), S = 90, ar = !!p.ar;
  let s = "";
  const F = (ang, len, lab, c) => { const e = [len * Math.cos(-ang * D2R), len * Math.sin(-ang * D2R)]; return arrow(0, 0, ...e, { w: 3.4, head: 16, stroke: c }) + text(e[0] * 1.2, e[1] * 1.2, lab, { size: 21, bold: true, fill: c }); };
  let bx = 235, by = 200;
  if (inc > 0) {
    const a = inc * D2R, base = 420, hgt = Math.tan(a) * base, X0 = 20, Y0 = 370;
    s += poly([[X0, Y0], [X0 + base, Y0], [X0 + base, Y0 - hgt]], { w: 2.5, fill: "currentColor", op: 0.06 }) + arc(X0, Y0, 54, 0, inc, { w: 1.8 }) + text(X0 + 78 * Math.cos(-inc / 2 * D2R), Y0 + 78 * Math.sin(-inc / 2 * D2R), "θ", { size: 19, italic: true });
    const sx = X0 + base * 0.5, sy = Y0 - hgt * 0.5;
    bx = sx - Math.sin(a) * S / 2; by = sy - Math.cos(a) * S / 2;
  }
  // القوى في إطار الجسم (بيميل مع المستوى)؛ الوزن دايمًا لتحت
  let g = rect(-S / 2, -S / 2, S, S, { w: 3, fill: "currentColor", op: 0.12, rx: 4 });
  if (p.n) g += F(90, 120, ar ? "ر" : "N", "#1971c2");
  if (p.f) g += F(p.fdir === "right" ? 0 : 180, 100, ar ? "ح" : "f", "#f08c00");
  if (p.app) g += F(num(p.appAng, 0), 130, ar ? "ق" : "F", "#2f9e44");
  if (p.t) g += F(90 + num(p.tAng, 0), 120, ar ? "ش" : "T", "#7048e8");
  s += `<g transform="translate(${bx},${by}) rotate(${-inc})">${g}</g>`;
  if (p.w) s += `<g transform="translate(${bx},${by})">${F(-90, 130, ar ? "و" : "W", "#e03131")}</g>`;
  s += circle(bx, by, 4, { fill: "currentColor", w: 0 });
  const minY = Math.min(0, by - 170);
  return { inner: `<g transform="translate(0,${-minY})">${s}</g>`, w: 470, h: 410 - minY };
}

function wave(p) {
  const cyc = Math.max(1, Math.min(6, num(p.cycles, 2))), A = Math.max(20, Math.min(110, num(p.amp, 70))), W = 560, y0 = 140;
  let s = "";
  if (p.type === "long") {
    // طولية: تضاغطات وتخلخلات (خطوط رأسية كثافتها بتتغير)
    for (let i = 0; i <= 160; i++) { const t = i / 160, x = 20 + t * W + Math.sin(t * cyc * 2 * Math.PI) * Math.min(24, W / cyc / 12); s += line(x, y0 - 60, x, y0 + 60, { w: 1.6 }); }
    if (p.labels) { const lw = W / cyc; s += arrow(20 + lw * 0.5, y0 + 84, 20 + lw * 1.5, y0 + 84, { w: 2, both: true, head: 10 }) + text(20 + lw, y0 + 104, "λ", { size: 22, italic: true }) + text(20 + lw * 0.5, y0 - 76, p.ar ? "تضاغط" : "compression", { size: 15 }) + text(20 + lw, y0 - 76, p.ar ? "تخلخل" : "rarefaction", { size: 15 }); }
    return { inner: s, w: W + 40, h: y0 + 120 };
  }
  let d = "";
  for (let i = 0; i <= 240; i++) { const t = i / 240, x = 20 + t * W, y = y0 - A * Math.sin(t * cyc * 2 * Math.PI); d += (i ? " L " : "M ") + x.toFixed(1) + " " + y.toFixed(1); }
  s += line(10, y0, W + 30, y0, { w: 1.5, dash: "6 5", stroke: "#868e96" }) + path(d, { w: 3.2 });
  if (p.labels) {
    const lw = W / cyc, crest1 = 20 + lw * 0.25, crest2 = crest1 + lw;
    s += arrow(crest1, y0 - A - 22, crest2, y0 - A - 22, { w: 2, both: true, head: 10 }) + text((crest1 + crest2) / 2, y0 - A - 40, "λ", { size: 24, italic: true });
    s += arrow(crest1, y0, crest1, y0 - A + 2, { w: 2, both: true, head: 10 }) + text(crest1 - 18, y0 - A / 2, "A", { size: 20, italic: true });
    s += text(crest1, y0 - A - 66, p.ar ? "قمة" : "crest", { size: 15 }) + text(20 + lw * 0.75, y0 + A + 26, p.ar ? "قاع" : "trough", { size: 15 });
  }
  return { inner: s, w: W + 40, h: y0 + A + 50 };
}

function incline(p) {
  const deg = Math.max(5, Math.min(70, num(p.deg, 30))), a = deg * D2R, base = 460, h = Math.tan(a) * base, X0 = 20, Y0 = 20 + Math.max(h, 120);
  let s = poly([[X0, Y0], [X0 + base, Y0], [X0 + base, Y0 - h]], { w: 3, fill: "currentColor", op: 0.06 });
  s += arc(X0, Y0, 60, 0, deg, { w: 2 }) + text(X0 + 82 * Math.cos(-deg / 2 * D2R), Y0 + 82 * Math.sin(-deg / 2 * D2R), (p.ar ? String(deg).replace(/\d/g, d => "٠١٢٣٤٥٦٧٨٩"[d]) : deg) + "°", { size: 18 });
  s += rightMark([X0 + base, Y0], [X0, Y0], [X0 + base, Y0 - h], 16);
  const S = 70, t = 0.58, px = X0 + base * t, py = Y0 - h * t;
  let g = rect(-S / 2, -S, S, S, { w: 3, fill: "currentColor", op: 0.14, rx: 4 });
  s += `<g transform="translate(${px},${py}) rotate(${-deg})">${g}</g>`;
  if (p.comps) {
    const c = [px - Math.sin(a) * S / 2, py - Math.cos(a) * S / 2], Wl = 120;
    s += arrow(...c, c[0], c[1] + Wl, { w: 3, stroke: "#e03131" }) + text(c[0] + 18, c[1] + Wl, p.ar ? "و" : "W", { size: 20, bold: true, fill: "#e03131" });
    const par = Wl * Math.sin(a), per = Wl * Math.cos(a);
    s += arrow(...c, c[0] - par * Math.cos(a), c[1] + par * Math.sin(a), { w: 2.4, dash: "7 5", stroke: "#1971c2" }) + text(c[0] - par * Math.cos(a) - 30, c[1] + par * Math.sin(a) - 10, p.ar ? "و جا θ" : "W sinθ", { size: 16, fill: "#1971c2" });
    s += arrow(...c, c[0] + per * Math.sin(a), c[1] + per * Math.cos(a), { w: 2.4, dash: "7 5", stroke: "#2f9e44" }) + text(c[0] + per * Math.sin(a) + 40, c[1] + per * Math.cos(a), p.ar ? "و جتا θ" : "W cosθ", { size: 16, fill: "#2f9e44" });
  }
  return { inner: s, w: X0 + base + 30, h: Y0 + (p.comps ? 40 : 20) };
}

function mech(p) {
  const t = p.type || "pendulum";
  let s = line(40, 30, 300, 30, { w: 3 });
  for (let x = 45; x < 300; x += 16) s += line(x, 30, x - 10, 18, { w: 1.6 });
  if (t === "pendulum") {
    const L = 230, ang = num(p.deg, 25) * D2R, P0 = [170, 30], B = [P0[0] + L * Math.sin(ang), P0[1] + L * Math.cos(ang)];
    s += line(...P0, P0[0], P0[1] + L, { w: 1.6, dash: "6 5", stroke: "#868e96" }) + line(...P0, ...B, { w: 2.4 }) + circle(...B, 22, { fill: "currentColor", op: 0.25, w: 3 });
    s += arc(...P0, 60, -90, -90 + num(p.deg, 25), { w: 1.8 }) + text(P0[0] + 30, P0[1] + 80, "θ", { size: 19, italic: true }) + text(P0[0] + L * Math.sin(ang) / 2 - 18, P0[1] + L * Math.cos(ang) / 2, "L", { size: 19, italic: true });
    return { inner: s, w: 340, h: 310 };
  }
  if (t === "spring") {
    let d = "M 170 30 L 170 50", y = 50; const coils = 9;
    for (let i = 0; i < coils; i++) { d += ` L ${i % 2 ? 140 : 200} ${y + 9} L 170 ${y + 18}`; y += 18; }
    d += ` L 170 ${y + 14}`;
    s += path(d, { w: 2.4 }) + rect(130, y + 14, 80, 60, { w: 3, fill: "currentColor", op: 0.18, rx: 4 }) + text(170, y + 44, "m", { size: 22, italic: true, bold: true }) + text(222, 30 + (y - 30) / 2, "k", { size: 20, italic: true });
    return { inner: s, w: 340, h: y + 90 };
  }
  // بكرة بثقلين
  s += line(170, 30, 170, 70, { w: 2.4 }) + circle(170, 100, 32, { w: 3 }) + circle(170, 100, 4, { fill: "currentColor", w: 0 });
  s += line(138, 100, 138, 240, { w: 2 }) + line(202, 100, 202, 200, { w: 2 }) + path("M 138 100 A 32 32 0 0 1 202 100", { w: 2 });
  s += rect(110, 240, 56, 56, { w: 3, fill: "currentColor", op: 0.18, rx: 3 }) + text(138, 268, "m₁", { size: 18, italic: true }) + rect(178, 200, 48, 48, { w: 3, fill: "currentColor", op: 0.18, rx: 3 }) + text(202, 224, "m₂", { size: 18, italic: true });
  return { inner: s, w: 340, h: 310 };
}

function magnet(p) {
  const x = 170, y = 170, W = 220, H = 64;
  let s = "";
  if (p.lines) {
    for (const k of [1, 2, 3]) {
      const ry = 34 + k * 26, rx = W / 2 + 40 + k * 26;
      s += path(`M ${x - W / 2 + 6} ${y - 8} C ${x - rx} ${y - ry * 1.6}, ${x + rx} ${y - ry * 1.6}, ${x + W / 2 - 6} ${y - 8}`, { w: 1.6, stroke: "#495057" });
      s += path(`M ${x - W / 2 + 6} ${y + 8} C ${x - rx} ${y + ry * 1.6}, ${x + rx} ${y + ry * 1.6}, ${x + W / 2 - 6} ${y + 8}`, { w: 1.6, stroke: "#495057" });
      s += arrow(x - 4, y - ry * 1.2 - 3, x - 12, y - ry * 1.2 - 3, { w: 1.6, head: 11, stroke: "#495057" }) + arrow(x - 4, y + ry * 1.2 + 3, x - 12, y + ry * 1.2 + 3, { w: 1.6, head: 11, stroke: "#495057" });
    }
    s += arrow(x + W / 2 + 6, y, x + W / 2 + 70, y, { w: 1.6, stroke: "#495057" }) + arrow(x - W / 2 - 70, y, x - W / 2 - 6, y, { w: 1.6, stroke: "#495057" });
  }
  s += rect(x, y - H / 2, W / 2, H, { w: 2.5, fill: "#e03131" }) + rect(x - W / 2, y - H / 2, W / 2, H, { w: 2.5, fill: "#1971c2" });
  s += text(x + W / 4, y + 2, p.ar ? "ش" : "N", { size: 30, bold: true, fill: "#fff" }) + text(x - W / 4, y + 2, p.ar ? "ج" : "S", { size: 30, bold: true, fill: "#fff" });
  return { inner: `<g transform="translate(60,0)">${s}</g>`, w: 460, h: 340 };
}

function lens(p) {
  const kind = p.type || "convex", f = 110, X = 300, Y = 170, W = 600, H = 340, sgn = kind === "concave" ? -1 : 1;
  const u = Math.max(30, Math.min(290, num(p.u, 260)));   // بعد الجسم عن العدسة
  const F0 = sgn * f, v0 = 1 / (1 / F0 - 1 / u), ho = isFinite(v0) ? Math.min(70, 150 / Math.max(0.01, Math.abs(v0 / u))) : 70;
  let s = line(10, Y, W - 10, Y, { w: 1.6, stroke: "#868e96" });
  // العدسة
  s += kind === "convex" ? path(`M ${X} ${Y - 130} Q ${X + 26} ${Y} ${X} ${Y + 130} Q ${X - 26} ${Y} ${X} ${Y - 130} Z`, { w: 2.5, fill: "#74c0fc", op: 0.35 })
    : path(`M ${X - 14} ${Y - 130} L ${X + 14} ${Y - 130} Q ${X + 2} ${Y} ${X + 14} ${Y + 130} L ${X - 14} ${Y + 130} Q ${X - 2} ${Y} ${X - 14} ${Y - 130} Z`, { w: 2.5, fill: "#74c0fc", op: 0.35 });
  for (const k of [-2, -1, 1, 2]) { s += circle(X + k * f, Y, 4, { fill: "currentColor", w: 0 }) + text(X + k * f, Y + 22, (Math.abs(k) === 2 ? "2" : "") + (p.ar ? "ب" : "F"), { size: 16 }); }
  // الجسم
  const ox = X - u;
  s += arrow(ox, Y, ox, Y - ho, { w: 3.4, stroke: "#2f9e44" });
  // الصورة: 1/f = 1/u + 1/v (إشارات حقيقية)
  const F = sgn * f, v = 1 / (1 / F - 1 / u);
  const m = -v / u, hi = ho * m, ix = X + v;
  const ray = (x1, y1, x2, y2, dash) => line(x1, y1, x2, y2, { w: 1.8, stroke: "#f08c00", dash });
  if (p.rays) {
    // شعاع موازي ← يمر بالبؤرة (أو يبدو خارجًا منها)
    s += ray(ox, Y - ho, X, Y - ho);
    const slope1 = kind === "convex" ? ho / f : -ho / f;
    s += ray(X, Y - ho, W - 10, Y - ho + slope1 * (W - 10 - X));
    if (kind === "concave" || v < 0) s += ray(X, Y - ho, Math.max(10, X - 260), Y - ho - slope1 * Math.min(260, X - 10), "6 5");
    // شعاع مار بالمركز
    const sl = ho / u;
    s += ray(ox, Y - ho, W - 10, Y - ho + sl * (W - 10 - ox));
    if (v < 0) s += ray(X, Y - ho + sl * u, Math.max(10, ix - 40), Y - ho + sl * (Math.max(10, ix - 40) - ox), "6 5");
  }
  if (isFinite(v) && Math.abs(ix) < W * 3 && ix > 0 && ix < W) {
    s += arrow(ix, Y, ix, Y - hi, { w: 3, stroke: "#7048e8", dash: v < 0 ? "6 5" : null });
    s += text(ix, Y - hi + (hi > 0 ? -16 : 18), p.ar ? (v < 0 ? "صورة تقديرية" : "صورة حقيقية") : (v < 0 ? "virtual image" : "real image"), { size: 14, fill: "#7048e8" });
  }
  return { inner: s, w: W, h: H };
}

/** رمز دائرة كهربائية (بمقاس ثابت 120×60 تقريبًا والطرفين على المحور الأفقي عشان التوصيل). */
export function symbol(k, label) {
  const y = 40, L = 10, R = 130;
  let s = "", w = 140, h = 80;
  const wire = (a, b) => line(a, y, b, y, { w: 2.5 });
  switch (k) {
    case "cell": s = wire(L, 62) + line(62, y - 26, 62, y + 26, { w: 3 }) + line(78, y - 13, 78, y + 13, { w: 6 }) + wire(78, R) + text(50, y - 30, "+", { size: 16 }); break;
    case "battery": s = wire(L, 40) + [40, 56, 72, 88].map((x, i) => i % 2 ? line(x, y - 13, x, y + 13, { w: 6 }) : line(x, y - 26, x, y + 26, { w: 3 })).join("") + wire(88, R); break;
    case "resistor": s = wire(L, 35) + rect(35, y - 14, 70, 28, { w: 2.5 }) + wire(105, R); break;
    case "resistorz": { let d = `M 35 ${y}`; for (let i = 0; i < 6; i++) d += ` L ${41 + i * 11} ${y + (i % 2 ? 14 : -14)}`; d += ` L 101 ${y} L 105 ${y}`; s = wire(L, 35) + path(d, { w: 2.5 }) + wire(105, R); break; }
    case "rheostat": s = wire(L, 35) + rect(35, y - 14, 70, 28, { w: 2.5 }) + wire(105, R) + arrow(40, y + 26, 100, y - 26, { w: 2, head: 11 }); break;
    case "lamp": s = wire(L, 48) + circle(70, y, 22, { w: 2.5 }) + line(55, y - 15, 85, y + 15, { w: 2.2 }) + line(55, y + 15, 85, y - 15, { w: 2.2 }) + wire(92, R); break;
    case "switch": s = wire(L, 45) + circle(48, y, 4, { fill: "currentColor", w: 0 }) + line(48, y, 92, y - 24, { w: 2.5 }) + circle(95, y, 4, { fill: "currentColor", w: 0 }) + wire(95, R); break;
    case "switchc": s = wire(L, 45) + circle(48, y, 4, { fill: "currentColor", w: 0 }) + line(48, y, 95, y - 3, { w: 2.5 }) + circle(95, y, 4, { fill: "currentColor", w: 0 }) + wire(95, R); break;
    case "ammeter": case "voltmeter": case "galv": case "motor":
      s = wire(L, 48) + circle(70, y, 22, { w: 2.5 }) + text(70, y + 1, { ammeter: "A", voltmeter: "V", galv: "G", motor: "M" }[k], { size: 22, bold: true }) + wire(92, R); break;
    case "acsource": s = wire(L, 48) + circle(70, y, 22, { w: 2.5 }) + path(`M 57 ${y} Q 63.5 ${y - 14} 70 ${y} T 83 ${y}`, { w: 2.2 }) + wire(92, R); break;
    case "capacitor": s = wire(L, 64) + line(64, y - 22, 64, y + 22, { w: 3.2 }) + line(76, y - 22, 76, y + 22, { w: 3.2 }) + wire(76, R); break;
    case "diode": s = wire(L, 52) + poly([[52, y - 18], [52, y + 18], [84, y]], { w: 2.5, fill: "currentColor" }) + line(86, y - 18, 86, y + 18, { w: 3 }) + wire(86, R); break;
    case "led": s = wire(L, 52) + poly([[52, y - 18], [52, y + 18], [84, y]], { w: 2.5, fill: "currentColor" }) + line(86, y - 18, 86, y + 18, { w: 3 }) + wire(86, R) + arrow(72, y - 22, 88, y - 38, { w: 1.8, head: 8 }) + arrow(82, y - 18, 98, y - 34, { w: 1.8, head: 8 }); break;
    case "fuse": s = wire(L, 35) + rect(35, y - 11, 70, 22, { w: 2.5 }) + line(35, y, 105, y, { w: 2 }) + wire(105, R); break;
    case "bell": s = wire(L, 45) + path(`M 50 ${y + 14} A 20 20 0 0 1 90 ${y + 14} Z`, { w: 2.5 }) + wire(95, R); break;
    case "ground": s = line(70, 6, 70, y, { w: 2.5 }) + line(48, y, 92, y, { w: 3 }) + line(55, y + 9, 85, y + 9, { w: 3 }) + line(63, y + 18, 77, y + 18, { w: 3 }); w = 140; break;
    case "junction": s = wire(L, R) + circle(70, y, 6, { fill: "currentColor", w: 0 }) + line(70, y, 70, y + 36, { w: 2.5 }); break;
    case "speaker": s = wire(L, 50) + rect(50, y - 10, 14, 20, { w: 2.5 }) + poly([[64, y - 10], [84, y - 26], [84, y + 26], [64, y + 10]], { w: 2.5 }) + wire(84, R); break;
    default: s = wire(L, R);
  }
  if (label) s += text(k === "junction" ? 96 : 70, k === "ground" ? 74 : y + 34, label, { size: 17, italic: true });
  return { inner: s, w, h: label && k !== "ground" ? h : h };
}
export const SYMBOLS = [["cell", "خلية (عمود)"], ["battery", "بطارية"], ["resistor", "مقاومة"], ["resistorz", "مقاومة (متعرجة)"], ["rheostat", "مقاومة متغيرة"], ["lamp", "مصباح"],
  ["switch", "مفتاح مفتوح"], ["switchc", "مفتاح مغلق"], ["ammeter", "أميتر"], ["voltmeter", "فولتميتر"], ["galv", "جلفانومتر"], ["capacitor", "مكثف"], ["diode", "دايود (ثنائي)"], ["led", "ثنائي باعث للضوء"],
  ["fuse", "منصهر"], ["acsource", "مصدر متردد"], ["motor", "محرك"], ["bell", "جرس"], ["speaker", "سماعة"], ["ground", "أرضي"], ["junction", "نقطة تفرع"]];

// ---------------------------------------------------------------- كيمياء: نموذج بور ولويس
export const ELEMENTS = [
  ["H", "هيدروجين", 1], ["He", "هيليوم", 4], ["Li", "ليثيوم", 7], ["Be", "بيريليوم", 9], ["B", "بورون", 11], ["C", "كربون", 12], ["N", "نيتروجين", 14], ["O", "أكسجين", 16], ["F", "فلور", 19], ["Ne", "نيون", 20],
  ["Na", "صوديوم", 23], ["Mg", "مغنيسيوم", 24], ["Al", "ألومنيوم", 27], ["Si", "سيليكون", 28], ["P", "فوسفور", 31], ["S", "كبريت", 32], ["Cl", "كلور", 35], ["Ar", "أرجون", 40], ["K", "بوتاسيوم", 39], ["Ca", "كالسيوم", 40],
  ["Sc", "سكانديوم", 45], ["Ti", "تيتانيوم", 48], ["V", "فاناديوم", 51], ["Cr", "كروم", 52], ["Mn", "منجنيز", 55], ["Fe", "حديد", 56], ["Co", "كوبالت", 59], ["Ni", "نيكل", 59], ["Cu", "نحاس", 64], ["Zn", "خارصين", 65],
  ["Ga", "جاليوم", 70], ["Ge", "جرمانيوم", 73], ["As", "زرنيخ", 75], ["Se", "سيلينيوم", 79], ["Br", "بروم", 80], ["Kr", "كريبتون", 84]
];
/** توزيع الإلكترونات على المدارات (K L M N): لحد 20 بقاعدة 2-8-8-2، وبعده بترتيب المستويات الفرعية. */
export function shells(z) {
  if (z <= 20) { const out = []; let r = z; for (const cap of [2, 8, 8, 2]) { if (r <= 0) break; out.push(Math.min(cap, r)); r -= cap; } return out; }
  const order = [[1, 2], [2, 2], [2, 6], [3, 2], [3, 6], [4, 2], [3, 10], [4, 6]];
  const sh = [0, 0, 0, 0]; let r = z;
  for (const [nn, cap] of order) { const k = Math.min(cap, r); sh[nn - 1] += k; r -= k; if (!r) break; }
  // الاستثناءات المشهورة
  if (z === 24 || z === 29) { sh[2] += 1; sh[3] -= 1; }
  return sh.filter(x => x > 0);
}
function atom(p) {
  const z = Math.max(1, Math.min(36, Math.round(num(p.z, 8)))), [sym, ar, mass] = ELEMENTS[z - 1];
  if (p.style === "lewis") {
    const v = z === 2 ? 2 : (shells(z).at(-1) || 0), slots = [[0, -1], [1, 0], [0, 1], [-1, 0]];
    let s = text(100, 100, sym, { size: 64, bold: true });
    const order = [0, 1, 2, 3, 0, 1, 2, 3];
    for (let i = 0; i < Math.min(8, v); i++) {
      const [dx, dy] = slots[order[i]], pair = i >= 4, off = pair ? 9 : -9;
      const x = 100 + dx * 52 + (dy ? off : 0), y = 100 + dy * 50 + (dx ? off : 0);
      s += circle(x, y, 5.5, { fill: "#e03131", w: 0 });
    }
    return { inner: s, w: 200, h: 200 };
  }
  const sh = shells(z), cx = 210, cy = 210;
  let s = circle(cx, cy, 34, { fill: "#ffc078", w: 2.5 }) + text(cx, cy - 8, p.ar ? `${z} ب` : `${z}p`, { size: 15, bold: true, fill: "#111" }) + text(cx, cy + 12, p.ar ? `${mass - z} ن` : `${mass - z}n`, { size: 15, bold: true, fill: "#111" });
  sh.forEach((cnt, i) => {
    const r = 62 + i * 42;
    s += circle(cx, cy, r, { w: 1.6, stroke: "#868e96" });
    for (let k = 0; k < cnt; k++) { const a = (k / cnt) * 2 * Math.PI - Math.PI / 2; s += circle(cx + r * Math.cos(a), cy + r * Math.sin(a), 7, { fill: "#1971c2", w: 0 }); }
  });
  const R = 62 + (sh.length - 1) * 42;
  s += text(cx, cy + R + 34, `${p.ar ? ar : sym} (${sym}) — ${sh.join(", ")}`, { size: 19, bold: true });
  return { inner: s, w: 420, h: cy + R + 56 };
}

// ---------------------------------------------------------------- كتالوج الأدوات
/** f: [المفتاح، العنوان، النوع، القيمة الافتراضية، خيارات] */
export const TOOLS = {
  graph: { tab: "math", icon: "📈", name: "رسم بياني للدوال", sub: "حتى ثلاث دوال ونقاط، مع المحاور والشبكة", draw: drawGraph, async: true,
    f: [["f1", "الدالة الأولى", "text", "x^2 - 4"], ["f2", "الدالة الثانية", "text", ""], ["f3", "الدالة الثالثة", "text", ""], ["points", "نقاط", "text", "", "مثال: (1,2) (-3,0)"],
      ["xmin", "س من", "num", -6], ["xmax", "إلى", "num", 6], ["ymin", "ص من", "num", -6], ["ymax", "إلى", "num", 8], ["grid", "شبكة", "chk", 1], ["legend", "أسماء الدوال", "chk", 1], ["ratio", "الشكل", "sel", "sq", [["sq", "مربع تقريبًا"], ["wide", "عريض"]]]] },
  numline: { tab: "math", icon: "↔", name: "خط الأعداد", sub: "بتدريج ونقاط وفترات مفتوحة أو مغلقة", draw: numberLine,
    f: [["from", "من", "num", -5], ["to", "إلى", "num", 5], ["step", "الكتابة كل", "num", 1], ["marks", "نقاط مميزة", "text", ""], ["interval", "فترة", "chk", 0], ["ia", "بدايتها", "num", -2], ["iac", "مغلقة من البداية", "chk", 1], ["ib", "نهايتها", "num", 3], ["ibc", "مغلقة من النهاية", "chk", 0], ["ar", "أرقام عربية", "chk", 0]] },
  polygon: { tab: "math", icon: "⬡", name: "مضلع منتظم", sub: "من 3 إلى 12 ضلعًا، بالأقطار والرؤوس", draw: polygon,
    f: [["n", "عدد الأضلاع", "num", 6], ["labels", "أسماء الرؤوس", "chk", 1], ["diag", "الأقطار", "chk", 0], ["center", "المركز ونصف القطر", "chk", 0], ["fill", "تظليل", "chk", 0], ["side", "طول الضلع", "text", ""], ["ar", "حروف عربية", "chk", 0]] },
  circle: { tab: "math", icon: "◯", name: "الدائرة وأجزاؤها", sub: "نصف قطر، قطر، وتر، مماس، قطاع", draw: circleParts,
    f: [["center", "المركز", "chk", 1], ["radius", "نصف القطر", "chk", 1], ["diameter", "القطر", "chk", 0], ["chord", "وتر", "chk", 0], ["tangent", "مماس", "chk", 0], ["sector", "قطاع", "chk", 0], ["angle", "زاوية القطاع", "num", 60], ["ar", "رموز عربية", "chk", 1]] },
  triangle: { tab: "math", icon: "△", name: "مثلث", sub: "قائم، متساوي الأضلاع، متساوي الساقين، منفرج", draw: triangle,
    f: [["type", "النوع", "sel", "right", [["right", "قائم الزاوية"], ["equi", "متساوي الأضلاع"], ["iso", "متساوي الساقين"], ["scalene", "مختلف الأضلاع"], ["obtuse", "منفرج الزاوية"]]], ["labels", "أسماء الرؤوس", "chk", 1], ["sides", "أسماء الأضلاع", "chk", 0], ["angles", "أقواس الزوايا", "chk", 0], ["height", "الارتفاع", "chk", 0], ["fill", "تظليل", "chk", 0], ["ar", "حروف عربية", "chk", 0]] },
  solid: { tab: "math", icon: "🧊", name: "مجسمات", sub: "مكعب، متوازي مستطيلات، أسطوانة، مخروط، هرم، كرة، منشور", draw: solid,
    f: [["type", "المجسم", "sel", "cube", [["cube", "مكعب"], ["cuboid", "متوازي مستطيلات"], ["cyl", "أسطوانة"], ["cone", "مخروط"], ["pyramid", "هرم رباعي"], ["sphere", "كرة"], ["prism", "منشور ثلاثي"]]], ["hidden", "الأحرف المخفية منقطة", "chk", 1], ["dims", "الأبعاد", "chk", 1]] },
  fraction: { tab: "math", icon: "◔", name: "كسور مرسومة", sub: "دائرة أو شريط مقسوم، والجزء المظلل", draw: fraction,
    f: [["shape", "الشكل", "sel", "circle", [["circle", "دائرة"], ["bar", "شريط"]]], ["n", "عدد الأجزاء", "num", 4], ["k", "المظلل", "num", 1], ["label", "كتابة الكسر", "chk", 1], ["ar", "أرقام عربية", "chk", 0]] },
  angle: { tab: "math", icon: "∠", name: "زاوية", sub: "بقياسها وقوسها", draw: angle, f: [["deg", "القياس (درجة)", "num", 60], ["show", "كتابة القياس", "chk", 1], ["ar", "أرقام عربية", "chk", 0]] },

  vector: { tab: "phys", icon: "↗", name: "متجه ومركّباته", sub: "المقدار والاتجاه والمركبتان والزاوية", draw: vector,
    f: [["name", "الاسم", "text", "F"], ["len", "الطول", "num", 260], ["deg", "الزاوية (درجة)", "num", 35], ["comps", "المركبتان", "chk", 1], ["arc", "قوس الزاوية", "chk", 1], ["axes", "المحاور", "chk", 1]] },
  freebody: { tab: "phys", icon: "⊞", name: "مخطط الجسم الحر", sub: "الوزن، العمودية، الاحتكاك، الشد، قوة خارجية", draw: freeBody,
    f: [["w", "الوزن", "chk", 1], ["n", "القوة العمودية", "chk", 1], ["f", "الاحتكاك", "chk", 1], ["fdir", "اتجاه الاحتكاك", "sel", "left", [["left", "يسار"], ["right", "يمين"]]], ["app", "قوة خارجية", "chk", 1], ["appAng", "زاويتها", "num", 0], ["t", "الشد", "chk", 0], ["tAng", "زاوية الشد", "num", 0], ["incline", "مستوى مائل (درجة، 0 = أفقي)", "num", 0], ["ar", "رموز عربية", "chk", 0]] },
  wave: { tab: "phys", icon: "∿", name: "موجة", sub: "مستعرضة أو طولية، بالطول الموجي والسعة", draw: wave,
    f: [["type", "النوع", "sel", "trans", [["trans", "مستعرضة"], ["long", "طولية"]]], ["cycles", "عدد الأطوال الموجية", "num", 2], ["amp", "السعة", "num", 70], ["labels", "التسميات", "chk", 1], ["ar", "تسميات عربية", "chk", 1]] },
  incline: { tab: "phys", icon: "◺", name: "مستوى مائل", sub: "بزاويته وجسم ومركبتي الوزن", draw: incline, f: [["deg", "الزاوية", "num", 30], ["comps", "مركبتا الوزن", "chk", 1], ["ar", "رموز عربية", "chk", 0]] },
  mech: { tab: "phys", icon: "⚖", name: "بندول ونابض وبكرة", sub: "البندول البسيط، النابض بكتلة، البكرة", draw: mech,
    f: [["type", "النوع", "sel", "pendulum", [["pendulum", "بندول بسيط"], ["spring", "نابض بكتلة"], ["pulley", "بكرة بثقلين"]]], ["deg", "زاوية البندول", "num", 25]] },
  magnet: { tab: "phys", icon: "🧲", name: "مغناطيس وخطوط المجال", sub: "قطبان وخطوط المجال واتجاهها", draw: magnet, f: [["lines", "خطوط المجال", "chk", 1], ["ar", "حروف عربية (ش/ج)", "chk", 1]] },
  lens: { tab: "phys", icon: "🔍", name: "العدسات وتكوين الصور", sub: "محدبة ومقعرة بالأشعة والبؤرة والصورة", draw: lens,
    f: [["type", "العدسة", "sel", "convex", [["convex", "محدبة (لامة)"], ["concave", "مقعرة (مفرقة)"]]], ["u", "بعد الجسم عن العدسة", "num", 260], ["rays", "الأشعة", "chk", 1], ["ar", "رموز عربية", "chk", 1]] },

  atom: { tab: "chem", icon: "⚛", name: "نموذج الذرة", sub: "بور (المدارات والإلكترونات) أو تمثيل لويس النقطي", draw: atom,
    f: [["z", "العدد الذري (1–36)", "num", 8], ["style", "النموذج", "sel", "bohr", [["bohr", "نموذج بور"], ["lewis", "تمثيل لويس"]]], ["ar", "بالعربية", "chk", 1]] },
  lab: { tab: "chem", icon: "⚗", name: "أداة معمل", hidden: true, draw: drawLab,
    f: [["type", "الأداة", "sel", "beaker", LAB.map(x => [x[0], x[1]])], ["level", "مستوى السائل (٪)", "num", 50, null, p => labHas(p.type, "l")], ["liq", "لون السائل", "sel", "#74c0fc", LIQUIDS, p => labHas(p.type, "l")],
      ["liq2", "لون سائل الوعاء الآخر", "sel", "#e7f5ff", LIQUIDS, p => p.type === "titration" || p.type === "distill"], ["grad", "تدريج", "chk", 1, null, p => labHas(p.type, "g")], ["bubbles", "فقاعات", "chk", 0, null, p => labHas(p.type, "b")],
      ["ppt", "راسب", "chk", 0, null, p => labHas(p.type, "p")], ["flame", "اللهب", "chk", 1, null, p => labHas(p.type, "f")], ["reading", "القراءة", "text", "25.00", null, p => p.type === "balance"], ["label", "تسمية تحتها", "text", ""]] },
  symbol: { tab: "phys", icon: "⏚", name: "رمز", hidden: true, draw: p => symbol(p.s, p.label), f: [["s", "الرمز", "sel", "resistor", SYMBOLS], ["label", "التسمية", "text", ""]] }
};
