// =====================================================================
//  محرك الشرح في «مسار»: طبقة رسم فوق أي «سطح» (صفحة PDF، صفحة سبورة، الشاشة).
//  كل حاجة بتتحفظ فيكتور بوحدات الصفحة نفسها (العرض = 1000) فبتفضل في مكانها مع التكبير.
//  الأصل مابيتلمسش: الشرح طبقة منفصلة بتتبعت للبرنامج يحفظها جنب المكتبة.
// =====================================================================
import { getStroke } from "./lib/perfect-freehand.mjs";

export const W = 1000;
const SVGNS = "http://www.w3.org/2000/svg";
const uid = () => Math.random().toString(36).slice(2, 9) + Date.now().toString(36).slice(-5);
const now = () => Date.now();
const clone = o => JSON.parse(JSON.stringify(o));
const dist = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1]);
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const r2 = v => Math.round(v * 100) / 100;

// ---------------------------------------------------------------- الأقلام والألوان والملصقات
export const PENS = {
  pen:    { name: "قلم حبر", size: 3, opacity: 1, thinning: 0.55, smoothing: 0.6, streamline: 0.5 },
  pencil: { name: "قلم رصاص", size: 1.6, opacity: 0.9, thinning: 0.15, smoothing: 0.5, streamline: 0.35 },
  marker: { name: "ماركر", size: 7, opacity: 1, thinning: 0, smoothing: 0.6, streamline: 0.55 },
  hl:     { name: "قلم تظليل", size: 16, opacity: 0.35, thinning: 0, smoothing: 0.7, streamline: 0.6, blend: true },
  temp:   { name: "قلم مؤقت (بيختفي)", size: 3.5, opacity: 1, thinning: 0.5, smoothing: 0.6, streamline: 0.5, temp: true },
  line:   { name: "خط مستقيم", size: 3, opacity: 1, thinning: 0, smoothing: 0, streamline: 0, straight: true }
};
export const COLORS = ["#111111", "#ffffff", "#e03131", "#f76707", "#f59f00", "#ffd43b", "#2f9e44", "#69db7c",
  "#1971c2", "#4dabf7", "#6741d9", "#c2255c", "#f783ac", "#868e96", "#795548", "#0ca678"];
export const SHAPES = [
  ["line", "خط", "╱"], ["arrow", "سهم", "→"], ["arrow2", "سهم بطرفين", "↔"], ["rect", "مستطيل", "▭"], ["round", "مستطيل بحواف مدورة", "▢"],
  ["ellipse", "دايرة", "◯"], ["tri", "مثلث", "△"], ["star", "نجمة", "☆"], ["brace", "قوس { }", "{"], ["check", "صح", "✓"], ["cross", "غلط", "✗"]
];
export const STICKERS = [
  { id: "ok", label: "صح", icon: "✓", color: "#2f9e44" }, { id: "no", label: "غلط", icon: "✗", color: "#e03131" },
  { id: "imp", label: "مهم", icon: "⭐", color: "#f59f00" }, { id: "rev", label: "راجع", icon: "🔁", color: "#1971c2" },
  { id: "q", label: "سؤال", icon: "❓", color: "#6741d9" }, { id: "warn", label: "تحذير", icon: "⚠", color: "#f76707" },
  { id: "idea", label: "فكرة", icon: "💡", color: "#f59f00" }, { id: "def", label: "تعريف", icon: "📘", color: "#1971c2" },
  { id: "rule", label: "قاعدة", icon: "📐", color: "#0ca678" }, { id: "ex", label: "مثال", icon: "🧪", color: "#c2255c" },
  { id: "exam", label: "امتحان", icon: "📝", color: "#e03131" }, { id: "mem", label: "حفظ", icon: "🧠", color: "#6741d9" },
  { id: "ar", label: "", icon: "➜", color: "#e03131", arrow: 0 }, { id: "al", label: "", icon: "➜", color: "#e03131", arrow: 180 },
  { id: "au", label: "", icon: "➜", color: "#e03131", arrow: -90 }, { id: "ad", label: "", icon: "➜", color: "#e03131", arrow: 90 }
];
export const EMOJI = ["👍", "👎", "👏", "🔥", "✅", "❌", "⭐", "❗", "❓", "💡", "📌", "🎯", "⚠️", "📎", "🧠", "📚", "✍️", "👀", "😀", "🤔", "😮", "🙌", "💯", "🏆"];
export const FONTS = ["Segoe UI", "Tahoma", "Arial", "Times New Roman", "Traditional Arabic", "Sakkal Majalla", "Simplified Arabic", "Consolas", "Comic Sans MS"];

// ---------------------------------------------------------------- الهندسة
function pathFromOutline(pts) {
  if (!pts.length) return "";
  const d = pts.reduce((acc, [x0, y0], i, arr) => {
    const [x1, y1] = arr[(i + 1) % arr.length];
    acc.push(r2(x0), r2(y0), r2((x0 + x1) / 2), r2((y0 + y1) / 2));
    return acc;
  }, ["M", ...pts[0].map(r2), "Q"]);
  d.push("Z");
  return d.join(" ");
}
export function strokePath(it) {
  const p = PENS[it.pen] || PENS.pen;
  if (it.pts.length === 1) { const [x, y] = it.pts[0]; const r = it.size / 2; return `M ${x - r} ${y} a ${r} ${r} 0 1 0 ${2 * r} 0 a ${r} ${r} 0 1 0 ${-2 * r} 0`; }
  const outline = getStroke(it.pts, {
    size: it.size, thinning: p.thinning, smoothing: p.smoothing, streamline: p.streamline,
    simulatePressure: !it.pr, last: true, start: { cap: true }, end: { cap: true }
  });
  return pathFromOutline(outline);
}
function segDist(p, a, b) {
  const dx = b[0] - a[0], dy = b[1] - a[1], l = dx * dx + dy * dy;
  if (!l) return dist(p, a);
  const t = clamp(((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / l, 0, 1);
  return Math.hypot(p[0] - a[0] - t * dx, p[1] - a[1] - t * dy);
}
function rot(p, c, deg) {
  if (!deg) return p;
  const a = deg * Math.PI / 180, s = Math.sin(a), co = Math.cos(a);
  const x = p[0] - c[0], y = p[1] - c[1];
  return [c[0] + x * co - y * s, c[1] + x * s + y * co];
}
function inPoly(p, poly) {
  let inside = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const [xi, yi] = poly[i], [xj, yj] = poly[j];
    if (((yi > p[1]) !== (yj > p[1])) && (p[0] < (xj - xi) * (p[1] - yi) / (yj - yi) + xi)) inside = !inside;
  }
  return inside;
}
/** الصندوق حوالين العنصر (بوحدات الصفحة) — مع اللف بالتقريب. */
export function bbox(it) {
  let x0, y0, x1, y1;
  if (it.t === "stroke") {
    x0 = y0 = Infinity; x1 = y1 = -Infinity;
    for (const [x, y] of it.pts) { x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); }
    const h = it.size / 2; return [x0 - h, y0 - h, x1 + h, y1 + h];
  }
  if (it.t === "shape" && (it.k === "line" || it.k === "arrow" || it.k === "arrow2")) {
    const h = (it.sw || 2) * 2 + 6;
    return [Math.min(it.x1, it.x2) - h, Math.min(it.y1, it.y2) - h, Math.max(it.x1, it.x2) + h, Math.max(it.y1, it.y2) + h];
  }
  if (it.t === "step") { const r = it.r || 16; return [it.x - r, it.y - r, it.x + r, it.y + r]; }
  if (it.t === "note") return [it.x, it.y, it.x + 26, it.y + 26];
  let b = it.t === "shape" ? [Math.min(it.x1, it.x2), Math.min(it.y1, it.y2), Math.max(it.x1, it.x2), Math.max(it.y1, it.y2)]
    : [it.x, it.y, it.x + (it.w || 0), it.y + (it.h || 0)];
  if (it.t === "callout" && it.tip) b = [Math.min(b[0], it.tip[0]), Math.min(b[1], it.tip[1]), Math.max(b[2], it.tip[0]), Math.max(b[3], it.tip[1])];
  if (it.rot) {
    const c = [(b[0] + b[2]) / 2, (b[1] + b[3]) / 2];
    const cs = [[b[0], b[1]], [b[2], b[1]], [b[2], b[3]], [b[0], b[3]]].map(p => rot(p, c, it.rot));
    b = [Math.min(...cs.map(p => p[0])), Math.min(...cs.map(p => p[1])), Math.max(...cs.map(p => p[0])), Math.max(...cs.map(p => p[1]))];
  }
  return b;
}
/** المسافة بين نقطة والعنصر (0 = جواه). */
function hitDist(it, p) {
  if (it.t === "stroke") {
    let d = Infinity;
    for (let i = 0; i < it.pts.length; i++) d = Math.min(d, i ? segDist(p, it.pts[i - 1], it.pts[i]) : dist(p, it.pts[0]));
    return Math.max(0, d - it.size / 2);
  }
  if (it.t === "shape" && (it.k === "line" || it.k === "arrow" || it.k === "arrow2"))
    return Math.max(0, segDist(p, [it.x1, it.y1], [it.x2, it.y2]) - (it.sw || 2));
  const b = bbox(it);
  if (it.t === "shape" && !it.fill && ["rect", "round", "ellipse", "tri", "star"].includes(it.k)) {
    // شكل مفرّغ: بيتلمس من حدوده بس (عشان تقدر ترسم جواه)
    const inner = p[0] > b[0] + 8 && p[0] < b[2] - 8 && p[1] > b[1] + 8 && p[1] < b[3] - 8;
    const inside = p[0] >= b[0] && p[0] <= b[2] && p[1] >= b[1] && p[1] <= b[3];
    return inside && !inner ? 0 : inside ? 6 : Math.hypot(Math.max(b[0] - p[0], 0, p[0] - b[2]), Math.max(b[1] - p[1], 0, p[1] - b[3]));
  }
  return Math.hypot(Math.max(b[0] - p[0], 0, p[0] - b[2]), Math.max(b[1] - p[1], 0, p[1] - b[3]));
}

/** تكملة الشكل: رسمة بالإيد ← خط / دايرة / مستطيل / مثلث (أو null). */
export function recognize(pts) {
  if (pts.length < 6) return null;
  let len = 0; for (let i = 1; i < pts.length; i++) len += dist(pts[i - 1], pts[i]);
  const xs = pts.map(p => p[0]), ys = pts.map(p => p[1]);
  const b = [Math.min(...xs), Math.min(...ys), Math.max(...xs), Math.max(...ys)];
  const diag = Math.hypot(b[2] - b[0], b[3] - b[1]);
  if (diag < 20) return null;
  const a = pts[0], z = pts[pts.length - 1];
  if (dist(a, z) / len > 0.94) return { k: "line", x1: a[0], y1: a[1], x2: z[0], y2: z[1] };
  if (dist(a, z) > diag * 0.25) return null; // مش مقفول
  const c = [(b[0] + b[2]) / 2, (b[1] + b[3]) / 2], rx = (b[2] - b[0]) / 2 || 1, ry = (b[3] - b[1]) / 2 || 1;
  const rs = pts.map(p => Math.hypot((p[0] - c[0]) / rx, (p[1] - c[1]) / ry));
  const mean = rs.reduce((s, v) => s + v, 0) / rs.length;
  const sd = Math.sqrt(rs.reduce((s, v) => s + (v - mean) ** 2, 0) / rs.length);
  const corners = rdp(pts, diag * 0.07);
  const closedCorners = corners.length > 1 && dist(corners[0], corners[corners.length - 1]) < diag * 0.25 ? corners.slice(0, -1) : corners;
  if (closedCorners.length === 3) return { k: "tri", poly: closedCorners, x1: b[0], y1: b[1], x2: b[2], y2: b[3] };
  if (closedCorners.length === 4) return { k: "rect", x1: b[0], y1: b[1], x2: b[2], y2: b[3] };
  if (sd / mean < 0.13) return { k: "ellipse", x1: b[0], y1: b[1], x2: b[2], y2: b[3] };
  return null;
}
function rdp(pts, eps) {
  if (pts.length < 3) return pts.slice();
  let max = 0, idx = 0;
  for (let i = 1; i < pts.length - 1; i++) { const d = segDist(pts[i], pts[0], pts[pts.length - 1]); if (d > max) { max = d; idx = i; } }
  if (max <= eps) return [pts[0], pts[pts.length - 1]];
  const l = rdp(pts.slice(0, idx + 1), eps), r = rdp(pts.slice(idx), eps);
  return l.slice(0, -1).concat(r);
}

// ---------------------------------------------------------------- الرسم (SVG + HTML)
function el(tag, attrs, parent) {
  const e = document.createElementNS(SVGNS, tag);
  for (const k in attrs) if (attrs[k] != null) e.setAttribute(k, attrs[k]);
  if (parent) parent.appendChild(e);
  return e;
}
function shapeElems(it, g) {
  const sw = it.sw || 3, stroke = it.c || "#e03131", fill = it.fill || "none";
  const common = { stroke, "stroke-width": sw, fill, "fill-opacity": it.fill ? (it.fo ?? 0.25) : null, "stroke-linecap": "round", "stroke-linejoin": "round" };
  const x0 = Math.min(it.x1, it.x2), y0 = Math.min(it.y1, it.y2), x1 = Math.max(it.x1, it.x2), y1 = Math.max(it.y1, it.y2), w = x1 - x0, h = y1 - y0;
  const head = (from, to) => {
    const ang = Math.atan2(to[1] - from[1], to[0] - from[0]), L = Math.max(10, sw * 4.5), sp = 0.45;
    const p1 = [to[0] - L * Math.cos(ang - sp), to[1] - L * Math.sin(ang - sp)], p2 = [to[0] - L * Math.cos(ang + sp), to[1] - L * Math.sin(ang + sp)];
    el("path", { d: `M ${p1} L ${to} L ${p2} Z`, fill: stroke, stroke, "stroke-width": sw / 2, "stroke-linejoin": "round" }, g);
  };
  switch (it.k) {
    case "line": case "arrow": case "arrow2":
      el("line", { ...common, fill: null, x1: it.x1, y1: it.y1, x2: it.x2, y2: it.y2 }, g);
      if (it.k !== "line") head([it.x1, it.y1], [it.x2, it.y2]);
      if (it.k === "arrow2") head([it.x2, it.y2], [it.x1, it.y1]);
      break;
    case "rect": el("rect", { ...common, x: x0, y: y0, width: w, height: h }, g); break;
    case "round": el("rect", { ...common, x: x0, y: y0, width: w, height: h, rx: Math.min(w, h) * 0.18 }, g); break;
    case "ellipse": el("ellipse", { ...common, cx: x0 + w / 2, cy: y0 + h / 2, rx: w / 2, ry: h / 2 }, g); break;
    case "tri": {
      const poly = it.poly || [[x0 + w / 2, y0], [x1, y1], [x0, y1]];
      el("polygon", { ...common, points: poly.map(p => p.join(",")).join(" ") }, g); break;
    }
    case "star": {
      const cx = x0 + w / 2, cy = y0 + h / 2, pts = [];
      for (let i = 0; i < 10; i++) { const r = i % 2 ? 0.45 : 1, a = -Math.PI / 2 + i * Math.PI / 5; pts.push([cx + Math.cos(a) * w / 2 * r, cy + Math.sin(a) * h / 2 * r]); }
      el("polygon", { ...common, points: pts.map(p => p.join(",")).join(" ") }, g); break;
    }
    case "brace": {
      // { بالعرض لو الصندوق عريض، وبالطول لو طويل
      const d = h >= w
        ? `M ${x1} ${y0} C ${x0 + w / 2} ${y0} ${x0 + w / 2} ${y0 + h / 2} ${x0} ${y0 + h / 2} C ${x0 + w / 2} ${y0 + h / 2} ${x0 + w / 2} ${y1} ${x1} ${y1}`
        : `M ${x0} ${y1} C ${x0} ${y0 + h / 2} ${x0 + w / 2} ${y0 + h / 2} ${x0 + w / 2} ${y0} C ${x0 + w / 2} ${y0 + h / 2} ${x1} ${y0 + h / 2} ${x1} ${y1}`;
      el("path", { ...common, fill: "none", d }, g); break;
    }
    case "check": el("path", { ...common, fill: "none", "stroke-width": Math.max(sw, 4), d: `M ${x0} ${y0 + h * 0.55} L ${x0 + w * 0.38} ${y1} L ${x1} ${y0}` }, g); break;
    case "cross": el("path", { ...common, fill: "none", "stroke-width": Math.max(sw, 4), d: `M ${x0} ${y0} L ${x1} ${y1} M ${x1} ${y0} L ${x0} ${y1}` }, g); break;
  }
}
function stickerElems(it, g) {
  const s = STICKERS.find(x => x.id === it.sid);
  const x = it.x, y = it.y, w = it.w, h = it.h;
  if (it.emoji) {
    const t = el("text", { x: x + w / 2, y: y + h * 0.82, "font-size": h * 0.9, "text-anchor": "middle" }, g);
    t.textContent = it.emoji; return;
  }
  if (it.img) { el("image", { href: it.img, x, y, width: w, height: h, preserveAspectRatio: "xMidYMid meet" }, g); return; }
  if (it.stamp) {
    // ختم: إطار مزدوج بلونه وكلام في النص
    const c = it.c || "#e03131", rr = it.shape === "circle" ? Math.min(w, h) / 2 : it.shape === "rect" ? 2 : h * 0.25;
    if (it.shape === "circle") {
      el("ellipse", { cx: x + w / 2, cy: y + h / 2, rx: w / 2, ry: h / 2, fill: "none", stroke: c, "stroke-width": h * 0.06 }, g);
      el("ellipse", { cx: x + w / 2, cy: y + h / 2, rx: w / 2 - h * 0.09, ry: h / 2 - h * 0.09, fill: "none", stroke: c, "stroke-width": h * 0.025 }, g);
    } else {
      el("rect", { x, y, width: w, height: h, rx: rr, fill: "none", stroke: c, "stroke-width": h * 0.07 }, g);
      el("rect", { x: x + h * 0.1, y: y + h * 0.1, width: w - h * 0.2, height: h * 0.8, rx: rr * 0.7, fill: "none", stroke: c, "stroke-width": h * 0.025 }, g);
    }
    const t = el("text", { x: x + w / 2, y: y + h * 0.64, "font-size": h * 0.38, "font-weight": 700, "text-anchor": "middle", fill: c, "font-family": "Segoe UI, Tahoma" }, g);
    t.textContent = it.stamp; return;
  }
  if (!s) return;
  if (s.arrow != null) {
    const cx = x + w / 2, cy = y + h / 2, gg = el("g", { transform: `rotate(${s.arrow} ${cx} ${cy})` }, g);
    el("path", { d: `M ${x} ${cy - h * 0.16} L ${x + w * 0.6} ${cy - h * 0.16} L ${x + w * 0.6} ${y} L ${x + w} ${cy} L ${x + w * 0.6} ${y + h} L ${x + w * 0.6} ${cy + h * 0.16} L ${x} ${cy + h * 0.16} Z`, fill: it.c || s.color }, gg);
    return;
  }
  const c = it.c || s.color;
  el("rect", { x, y, width: w, height: h, rx: h / 2, fill: c }, g);
  const t = el("text", { x: x + w / 2, y: y + h * 0.68, "font-size": h * 0.5, "font-weight": 700, "text-anchor": "middle", fill: "#fff", "font-family": "Segoe UI, Tahoma", direction: "rtl" }, g);
  t.textContent = (s.icon + " " + s.label).trim();
}

/** عنصر SVG لكل حاجة بتترسم (والنصوص بتترسم HTML عشان العربي يطلع مظبوط وتتعدّل). */
function renderSvg(it) {
  const g = el("g", { "data-id": it.id });
  if (it.ly === "t") g.classList.add("ink-temp-layer");
  if (it.t === "stroke") {
    const p = PENS[it.pen] || PENS.pen;
    el("path", { d: strokePath(it), fill: it.c, "fill-opacity": it.o ?? p.opacity }, g);
    if (p.blend) g.setAttribute("class", (g.getAttribute("class") || "") + " ink-hl");
  } else if (it.t === "shape") {
    shapeElems(it, g);
    if (it.o != null && it.o < 1) g.setAttribute("opacity", it.o);
  } else if (it.t === "sticker" || it.t === "image") {
    stickerElems(it, g);
  } else if (it.t === "step") {
    const r = it.r || 16;
    if (!it._numOnly) el("circle", { cx: it.x, cy: it.y, r, fill: it.c || "#e03131", stroke: "#fff", "stroke-width": r * 0.12 }, g);
    const t = el("text", { x: it.x, y: it.y + r * 0.36, "font-size": r * 1.05, "font-weight": 700, "text-anchor": "middle", fill: "#fff", "font-family": "Segoe UI, Tahoma" }, g);
    t.textContent = String(it.n);
  } else if (it.t === "callout") {
    const x0 = it.x, y0 = it.y, w = it.w, h = it.h, tip = it.tip || [x0 + w / 2, y0 + h + 40];
    const cx = x0 + w / 2, cy = y0 + h / 2, bw = Math.min(w, h) * 0.18;
    const ang = Math.atan2(tip[1] - cy, tip[0] - cx), perp = ang + Math.PI / 2;
    const b1 = [cx + Math.cos(perp) * bw, cy + Math.sin(perp) * bw], b2 = [cx - Math.cos(perp) * bw, cy - Math.sin(perp) * bw];
    el("path", { d: `M ${b1} L ${tip} L ${b2} Z`, fill: it.bg || "#fff9db", stroke: it.c || "#f59f00", "stroke-width": 2 }, g);
    el("rect", { x: x0, y: y0, width: w, height: h, rx: 12, fill: it.bg || "#fff9db", stroke: it.c || "#f59f00", "stroke-width": 2 }, g);
  } else if (it.t === "curtain") {
    g.classList.add("ink-curtain");
    const rv = clamp(it.rv || 0, 0, 1);
    el("rect", { x: it.x, y: it.y + it.h * rv, width: it.w, height: it.h * (1 - rv), fill: it.c || "#ced4da", stroke: "#868e96", "stroke-width": 1.5, rx: 4 }, g);
    if (rv < 1) {
      const t = el("text", { x: it.x + it.w / 2, y: it.y + it.h * rv + Math.min(36, it.h * (1 - rv) / 2 + 10), "font-size": 22, "text-anchor": "middle", fill: "#495057", "font-family": "Segoe UI, Tahoma" }, g);
      t.textContent = "دوس للكشف ⬇";
    }
  }
  if (it.rot && it.t !== "stroke") {
    const b = it.t === "shape" ? [Math.min(it.x1, it.x2), Math.min(it.y1, it.y2), Math.max(it.x1, it.x2), Math.max(it.y1, it.y2)] : [it.x, it.y, it.x + (it.w || 0), it.y + (it.h || 0)];
    g.setAttribute("transform", `rotate(${it.rot} ${(b[0] + b[2]) / 2} ${(b[1] + b[3]) / 2})`);
  }
  return g;
}

// ---------------------------------------------------------------- المستند (كل الصفحات)
export class InkDoc {
  constructor(json) {
    this.pages = new Map();   // key ← [items]
    this.del = new Map();     // id ← وقت المسح (للدمج بين الأجهزة)
    this.dirty = new Set();   // صفحات اتغيرت ولسه ماتبعتتش
    if (json) this.load(json);
  }
  load(json) {
    this.pages.clear(); this.del.clear();
    const o = typeof json === "string" ? JSON.parse(json || "{}") : json || {};
    for (const k in o.pages || {}) this.pages.set(String(k), (o.pages[k] || []).filter(Boolean));
    for (const k in o.del || o.tomb || {}) this.del.set(k, (o.del || o.tomb)[k]);
  }
  /** دمج نسخة جاية من جهاز تاني: الأحدث يكسب، والممسوح يفضل ممسوح. بيرجّع الصفحات اللي اتغيرت. */
  merge(json) {
    const o = typeof json === "string" ? JSON.parse(json || "{}") : json || {};
    const changed = new Set();
    for (const k in o.del || {}) if (!this.del.has(k) || this.del.get(k) < o.del[k]) this.del.set(k, o.del[k]);
    const keys = new Set([...Object.keys(o.pages || {}), ...this.pages.keys()]);
    for (const k of keys) {
      const mine = this.items(k), theirs = (o.pages || {})[k] || [];
      const byId = new Map(mine.map(i => [i.id, i]));
      let out = mine.slice(), dirty = false;
      for (const t of theirs) {
        const m = byId.get(t.id);
        if (!m) { out.push(t); dirty = true; }
        else if ((t.u || 0) > (m.u || 0)) { out[out.indexOf(m)] = t; dirty = true; }
      }
      const kept = out.filter(i => !(this.del.has(i.id) && this.del.get(i.id) >= (i.u || 0)));
      if (kept.length !== out.length) dirty = true;
      if (dirty) { this.pages.set(String(k), kept); changed.add(String(k)); }
    }
    return changed;
  }
  items(key) { return this.pages.get(String(key)) || []; }
  set(key, items) { this.pages.set(String(key), items); this.dirty.add(String(key)); }
  find(id) { for (const [k, arr] of this.pages) { const it = arr.find(x => x.id === id); if (it) return [k, it]; } return null; }
  /** للبرنامج: الصفحات اللي اتغيرت بس (وأوقات المسح). */
  takeChanges() {
    const pages = {};
    for (const k of this.dirty) pages[k] = this.items(k).filter(i => !i.tempPen);
    this.dirty.clear();
    return { pages, tomb: Object.fromEntries(this.del) };
  }
  toJSON() {
    const pages = {};
    for (const [k, v] of this.pages) { const keep = v.filter(i => !i.tempPen); if (keep.length) pages[k] = keep; }
    return { v: 1, pages, del: Object.fromEntries(this.del) };
  }
}

// =====================================================================
//  المحرك
// =====================================================================
export const Ink = {
  doc: new InkDoc(),
  surfaces: new Map(),
  on: false,               // وضع الشرح شغال
  tool: "pen",
  pen: "pen",
  shape: "arrow",
  eraser: "stroke",        // stroke | partial | area
  sticker: null,           // {sid} | {emoji} | {stamp,...} | {img}
  layer: "p",              // p = دائم، t = مؤقت
  visible: true,
  penOnly: false,
  recognize: false,
  snap: false,             // لقط على شبكة (للسبورة)
  grid: 20,
  prefs: { pens: {}, color: "#e03131", fav: ["#111111", "#e03131", "#1971c2", "#2f9e44", "#f59f00", "#6741d9"], recent: [], text: { font: "Segoe UI", size: 22, c: "#111111", bg: "", bold: false, align: "start", dir: "auto" }, shape: { sw: 3, fill: "", fo: 0.25 }, stamps: [], myStickers: [] },
  sel: null,               // {key, ids:Set}
  undo: [], redo: [],
  clip: null,
  post: () => { },
  onChange: null,
  onTool: null,
  container: null,
  overlay: null,
  _saveTimer: 0,

  init(o) {
    this.post = o.post || this.post;
    this.container = o.container || document.body;
    this.onTool = o.onTool || null;
    this.onSel = o.onSel || null;
    this.overlay = document.createElement("div");
    this.overlay.className = "ink-global";
    document.body.appendChild(this.overlay);
    this.laser = new Laser(this.overlay);
    this.spot = new Spotlight(this.overlay);
    this.lens = new Lens(this.overlay, this);
    this.ruler = new Ruler(this.overlay, "ruler");
    this.protractor = new Ruler(this.overlay, "protractor");
    window.addEventListener("paste", e => this.onPaste(e));
    window.addEventListener("resize", () => { for (const s of this.surfaces.values()) this.layout(s); });
  },

  // ---------------- البيانات
  /** نسخة أحدث من الملف (جهاز تاني): بتتدمج من غير ما تبوّظ اللي بترسمه دلوقتي. */
  mergeRemote(json) { const ch = this.doc.merge(json); for (const k of ch) this.render(this.surfaces.get(k)); return ch.size; },
  load(json) { this.doc = new InkDoc(json); this.undo = []; this.redo = []; this.sel = null; for (const s of this.surfaces.values()) this.render(s); },
  setPrefs(p) { if (p) this.prefs = Object.assign(this.prefs, typeof p === "string" ? JSON.parse(p) : p); },
  savePrefs() { this.post({ t: "inkprefs", prefs: this.prefs }); },
  changed(keys) {
    for (const k of keys) this.doc.dirty.add(String(k));
    clearTimeout(this._saveTimer);
    this._saveTimer = setTimeout(() => this.flush(), 1200);
    if (this.onChange) this.onChange();
  },
  flush() {
    clearTimeout(this._saveTimer);
    if (!this.doc.dirty.size) return;
    this.post({ t: "ink", ...this.doc.takeChanges() });
  },
  count() { let n = 0; for (const v of this.doc.pages.values()) n += v.filter(i => i.ly !== "t").length; return n; },

  // ---------------- الأسطح
  /** سطح جديد (أو تحديث): el = العنصر اللي الطبقة بتتحط جواه (صفحة)، aspect = الطول ÷ العرض. */
  attach(el, key, aspect, opts) {
    key = String(key);
    let s = this.surfaces.get(key);
    if (s && s.el === el) {
      if (opts && !!opts.dark !== !!s.dark) { s.dark = !!opts.dark; this.render(s); }
      // نفس الصفحة: نرجّع نفس الطبقة (PDF.js بيشيل أي حاجة غريبة وهو بيعيد رسم الصفحة)
      if (!s.layer.isConnected) el.appendChild(s.layer);
      s.aspect = aspect || s.aspect; this.layout(s); return s;
    }
    if (s && s.layer) { s.layer.remove(); if (s.ro) s.ro.disconnect(); }
    const layer = document.createElement("div");
    layer.className = "ink-layer";
    layer.dataset.key = key;
    const svg = el_("svg", "ink-svg"), live = el_("svg", "ink-live"), html = document.createElement("div"), ui = document.createElement("div");
    html.className = "ink-html"; ui.className = "ink-ui";
    layer.append(svg, html, live, ui);
    el.appendChild(layer);
    s = { key, el, layer, svg, live, html, ui, aspect: aspect || 1.414, dark: !!(opts && opts.dark) };
    this.surfaces.set(key, s);
    if (window.ResizeObserver) { s.ro = new ResizeObserver(() => this.layout(s)); s.ro.observe(layer); }
    this.bindPointer(s);
    this.layout(s);
    this.render(s);
    return s;
    function el_(tag, cls) { const e = document.createElementNS(SVGNS, tag); e.setAttribute("class", cls); return e; }
  },
  /** أي طبقة اتشالت من صفحة لسه موجودة بترجع فورًا (الشرح مايختفيش أثناء التكبير). */
  keepAlive(root) {
    if (this._mo) this._mo.disconnect();
    this._mo = new MutationObserver(muts => {
      for (const m of muts) for (const n of m.removedNodes) {
        if (!n.classList || !n.classList.contains("ink-layer")) continue;
        const s = this.surfaces.get(n.dataset.key);
        if (s && s.layer === n && s.el.isConnected && !n.isConnected) s.el.appendChild(n);
      }
    });
    this._mo.observe(root, { childList: true, subtree: true });
  },
  detach(key) { const s = this.surfaces.get(String(key)); if (s) { s.layer.remove(); if (s.ro) s.ro.disconnect(); this.surfaces.delete(String(key)); } },
  clearSurfaces() { for (const s of this.surfaces.values()) { s.layer.remove(); if (s.ro) s.ro.disconnect(); } this.surfaces.clear(); this.sel = null; },
  layout(s) {
    const H = W * s.aspect;
    for (const v of [s.svg, s.live]) { v.setAttribute("viewBox", `0 0 ${W} ${H}`); v.setAttribute("preserveAspectRatio", "none"); }
    const w = s.layer.clientWidth || s.el.clientWidth || 1;
    s.layer.style.setProperty("--k", (w / W).toFixed(5));
    const changed = s.H && Math.abs(s.H - H) > 0.5;
    s.H = H;
    if (changed) this.render(s);
    this.applyMode(s);
  },
  applyMode(s) {
    const drawing = this.on && this.tool !== "hand";
    s.layer.classList.toggle("active", drawing);
    s.layer.classList.toggle("hidden", !this.visible);
    s.layer.classList.toggle("pen-only", this.penOnly);
    s.layer.dataset.tool = this.tool;
  },
  surfaceAt(x, y) {
    for (const s of this.surfaces.values()) {
      const r = s.layer.getBoundingClientRect();
      if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) return s;
    }
    return null;
  },
  toPage(s, e) {
    const r = s.layer.getBoundingClientRect();
    return [r2((e.clientX - r.left) / r.width * W), r2((e.clientY - r.top) / r.width * W)];
  },
  snapPt(p) {
    if (!this.snap) return p;
    const g = this.grid;
    return [Math.round(p[0] / g) * g, Math.round(p[1] / g) * g];
  },

  // ---------------- الرسم
  render(s) {
    if (!s) return;
    s.svg.textContent = ""; s.html.textContent = "";
    for (const it of this.doc.items(s.key)) this.renderOne(s, it);
    this.renderSel();
  },
  renderAll() { for (const s of this.surfaces.values()) this.render(s); },
  renderOne(s, it) {
    const g = renderSvg(it);
    if (s.dark) for (const e of g.querySelectorAll("[fill],[stroke]")) {
      // سبورة سودا/خضرا: الحبر الأسود بيبان أبيض
      for (const a of ["fill", "stroke"]) { const v = (e.getAttribute(a) || "").toLowerCase(); if (v === "#111111" || v === "#111" || v === "#000000") e.setAttribute(a, "#f1f3f5"); }
    }
    if (it.t === "curtain") g.addEventListener("pointerdown", e => {
      // الستارة بتتكشف بكليك حتى وأدوات الشرح مقفولة (وانت بتعرض)
      if (this.on && !["hand", "laser", "spot", "lens"].includes(this.tool)) return;
      e.preventDefault(); e.stopPropagation();
      this.revealCurtain(s, it.id);
    });
    s.svg.appendChild(g);
    if (it.t === "text" || it.t === "callout" || it.t === "note") s.html.appendChild(this.htmlOf(s, it));
  },
  htmlOf(s, it) {
    const d = document.createElement("div");
    d.dataset.id = it.id;
    const pct = (v, base) => (v / base * 100) + "%";
    if (it.t === "note") {
      d.className = "ink-note" + (it.open ? " open" : "");
      d.style.left = pct(it.x, W); d.style.top = pct(it.y, s.H);
      d.style.setProperty("--nc", it.c || "#ffd43b");
      const ic = document.createElement("div"); ic.className = "ic"; ic.textContent = "📝";
      ic.onclick = ev => { if (this.tool === "select" || !this.on || this.tool === "hand" || this.tool === "note") { ev.stopPropagation(); this.toggleNote(s, it.id); } };
      d.appendChild(ic);
      if (it.open) {
        const ta = document.createElement("textarea");
        ta.value = it.text || ""; ta.dir = "auto"; ta.placeholder = "اكتب ملاحظتك…";
        ta.onpointerdown = ev => ev.stopPropagation();
        ta.oninput = () => { const f = this.doc.find(it.id); if (f) { f[1].text = ta.value; f[1].u = now(); this.changed([s.key]); } };
        ta.onkeydown = ev => { if (ev.key === "Escape") { ev.stopPropagation(); this.toggleNote(s, it.id); } };
        d.appendChild(ta);
        setTimeout(() => { if (it._focus) { ta.focus(); delete it._focus; } }, 0);
      }
      return d;
    }
    d.className = it.t === "callout" ? "ink-text ink-callout" : "ink-text";
    d.style.left = pct(it.x, W); d.style.top = pct(it.y, s.H);
    d.style.width = pct(it.w, W);
    if (it.t === "callout") d.style.height = pct(it.h, s.H);
    d.style.fontFamily = (it.font || "Segoe UI") + ", Tahoma, sans-serif";
    d.style.fontSize = `calc(var(--k) * ${it.size || 22}px)`;
    d.style.color = s.dark && /^#(111|111111|000000)$/i.test(it.c || "#111") ? "#f1f3f5" : (it.c || "#111");
    if (it.bg && it.t === "text") d.style.background = it.bg;
    if (it.bold) d.style.fontWeight = "700";
    d.style.textAlign = it.align || "start";
    d.dir = it.dir || "auto";
    if (it.rot) d.style.transform = `rotate(${it.rot}deg)`;
    d.textContent = it.text || "";
    d.ondblclick = ev => { ev.stopPropagation(); this.editText(s, it.id); };
    return d;
  },
  toggleNote(s, id) {
    const f = this.doc.find(id); if (!f) return;
    f[1].open = !f[1].open; if (f[1].open) f[1]._focus = true;
    this.render(s);
  },
  /** تعديل نص موجود: contenteditable لحد ما تخرج منه. */
  editText(s, id, fresh) {
    const f = this.doc.find(id); if (!f) return;
    const it = f[1], d = s.html.querySelector(`[data-id="${id}"]`); if (!d) return;
    const before = clone(this.doc.items(s.key));
    d.contentEditable = "true"; d.classList.add("editing");
    d.focus();
    const range = document.createRange(); range.selectNodeContents(d); range.collapse(false);
    const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(range);
    const done = () => {
      d.removeEventListener("blur", done);
      d.contentEditable = "false"; d.classList.remove("editing");
      const text = d.innerText.replace(/ /g, " ").replace(/\n+$/, "");
      const items = this.doc.items(s.key);
      if (!text.trim() && it.t === "text") { this.doc.set(s.key, items.filter(x => x.id !== id)); this.doc.del.set(id, now()); }
      else { it.text = text; it.u = now(); if (it.t === "text") it.h = r2(d.offsetHeight / (s.layer.clientWidth / W)); }
      if (!fresh || text.trim()) this.pushUndo(s.key, before);
      this.changed([s.key]);
      this.render(s);
    };
    d.addEventListener("blur", done);
    d.onkeydown = ev => { if (ev.key === "Escape" || (ev.key === "Enter" && ev.ctrlKey)) { ev.preventDefault(); ev.stopPropagation(); d.blur(); } };
    d.onpointerdown = ev => ev.stopPropagation();
  },

  // ---------------- التعديل والتراجع
  pushUndo(key, before) {
    this.undo.push({ key: String(key), before, after: clone(this.doc.items(key)) });
    if (this.undo.length > 300) this.undo.shift();
    this.redo = [];
  },
  /** تغيير على صفحة: fn بتعدّل نسخة من العناصر وترجّعها. */
  edit(key, fn) {
    key = String(key);
    const before = clone(this.doc.items(key));
    const after = fn(clone(before));
    const ids = new Set(after.map(i => i.id));
    for (const i of before) if (!ids.has(i.id)) this.doc.del.set(i.id, now());
    this.doc.set(key, after);
    this.pushUndo(key, before);
    this.changed([key]);
    this.render(this.surfaces.get(key));
  },
  add(key, it) {
    it.id ||= uid(); it.u = now(); it.ly ||= this.layer;
    this.edit(key, items => { items.push(it); return items; });
    return it;
  },
  restore(key, items) {
    const cur = this.doc.items(key), ids = new Set(items.map(i => i.id));
    for (const i of cur) if (!ids.has(i.id)) this.doc.del.set(i.id, now());
    for (const i of items) { this.doc.del.delete(i.id); i.u = now(); }
    this.doc.set(key, items);
    this.changed([key]);
    this.sel = null;
    this.render(this.surfaces.get(key));
  },
  doUndo() {
    const u = this.undo.pop(); if (!u) return false;
    this.redo.push(u); this.restore(u.key, clone(u.before));
    if (this.onGoto) this.onGoto(u.key);
    return true;
  },
  doRedo() {
    const u = this.redo.pop(); if (!u) return false;
    this.undo.push(u); this.restore(u.key, clone(u.after));
    if (this.onGoto) this.onGoto(u.key);
    return true;
  },

  // ---------------- الأدوات
  setTool(t, sub) {
    if (this.tool === "laser") this.laser.stop();
    if (this.tool === "spot") this.spot.hide();
    if (this.tool === "lens") this.lens.hide();
    this.tool = t;
    if (t === "pen" && sub) this.pen = sub;
    if (t === "shape" && sub) this.shape = sub;
    if (t === "eraser" && sub) this.eraser = sub;
    if (t === "sticker" && sub) this.sticker = sub;
    if (t === "ruler") { this.ruler.toggle(); this.tool = "pen"; }
    if (t === "protractor") { this.protractor.toggle(); this.tool = "pen"; }
    if (t !== "select") this.setSel(null);
    for (const s of this.surfaces.values()) this.applyMode(s);
    if (this.tool === "spot") this.spot.show();
    if (this.onTool) this.onTool();
  },
  setOn(on) {
    this.on = !!on;
    if (!on) { this.setSel(null); this.laser.stop(); this.spot.hide(); this.lens.hide(); this.flush(); }
    else if (this.tool === "spot") this.spot.show();
    document.body.classList.toggle("ink-on", this.on);
    for (const s of this.surfaces.values()) this.applyMode(s);
    if (this.onTool) this.onTool();
  },
  setVisible(v) { this.visible = !!v; for (const s of this.surfaces.values()) this.applyMode(s); if (this.onTool) this.onTool(); },
  penOpts(name) {
    const base = PENS[name] || PENS.pen, mine = this.prefs.pens[name] || {};
    return { c: mine.c || (name === "hl" ? "#ffd43b" : this.prefs.color), size: mine.size ?? base.size, o: mine.o ?? base.opacity };
  },
  setPenOpt(name, k, v) { (this.prefs.pens[name] ||= {})[k] = v; if (k === "c") this.useColor(v); this.savePrefs(); if (this.onTool) this.onTool(); },
  useColor(c) {
    this.prefs.color = c;
    this.prefs.recent = [c, ...this.prefs.recent.filter(x => x !== c)].slice(0, 8);
  },
  /** اللون الحالي للأداة الشغالة (للزراير السريعة). */
  currentColor() {
    if (this.tool === "pen") return this.penOpts(this.pen).c;
    if (this.tool === "text") return this.prefs.text.c;
    return this.prefs.color;
  },
  setColor(c) {
    if (this.tool === "pen") this.setPenOpt(this.pen, "c", c);
    else if (this.tool === "text") { this.prefs.text.c = c; this.useColor(c); this.savePrefs(); }
    else { this.useColor(c); this.savePrefs(); }
    if (this.sel) this.recolorSel(c);
    if (this.onTool) this.onTool();
  },

  // ---------------- التحديد
  setSel(sel) {
    this.sel = sel && sel.ids.size ? sel : null;
    this.renderSel();
    if (this.onSel) this.onSel(this.sel);
  },
  selItems() { if (!this.sel) return []; return this.doc.items(this.sel.key).filter(i => this.sel.ids.has(i.id)); },
  selBox() {
    const its = this.selItems(); if (!its.length) return null;
    const bs = its.map(bbox);
    return [Math.min(...bs.map(b => b[0])), Math.min(...bs.map(b => b[1])), Math.max(...bs.map(b => b[2])), Math.max(...bs.map(b => b[3]))];
  },
  renderSel() {
    for (const s of this.surfaces.values()) s.ui.textContent = "";
    if (!this.sel) return;
    const s = this.surfaces.get(this.sel.key); if (!s) return;
    const b = this.selBox(); if (!b) return;
    const k = 100 / W, kh = 100 / s.H;
    const box = document.createElement("div");
    box.className = "ink-selbox";
    box.style.left = (b[0] * k) + "%"; box.style.top = (b[1] * kh) + "%";
    box.style.width = ((b[2] - b[0]) * k) + "%"; box.style.height = ((b[3] - b[1]) * kh) + "%";
    for (const h of ["nw", "ne", "sw", "se", "rot"]) { const d = document.createElement("div"); d.className = "h " + h; d.dataset.h = h; box.appendChild(d); }
    s.ui.appendChild(box);
  },
  recolorSel(c) {
    if (!this.sel) return;
    const ids = this.sel.ids;
    this.edit(this.sel.key, items => items.map(i => { if (ids.has(i.id)) { i.c = c; i.u = now(); } return i; }));
    this.renderSel();
  },
  resizeSel(v) {
    if (!this.sel) return;
    const ids = this.sel.ids;
    this.edit(this.sel.key, items => items.map(i => {
      if (!ids.has(i.id)) return i;
      if (i.t === "stroke") i.size = v; else if (i.t === "shape") i.sw = v; else if (i.t === "text" || i.t === "callout") i.size = Math.max(8, v * 6);
      i.u = now(); return i;
    }));
    this.renderSel();
  },
  deleteSel() {
    if (!this.sel) return;
    const ids = this.sel.ids;
    this.edit(this.sel.key, items => items.filter(i => !ids.has(i.id)));
    this.setSel(null);
  },
  orderSel(front) {
    if (!this.sel) return;
    const ids = this.sel.ids;
    this.edit(this.sel.key, items => { const a = items.filter(i => ids.has(i.id)), b = items.filter(i => !ids.has(i.id)); return front ? b.concat(a) : a.concat(b); });
    this.renderSel();
  },
  copySel(cut) {
    const its = this.selItems(); if (!its.length) return;
    this.clip = { items: clone(its), box: this.selBox() };
    if (cut) this.deleteSel();
  },
  paste(key, at) {
    if (!this.clip) return;
    key = String(key);
    const b = this.clip.box, dx = at ? at[0] - (b[0] + b[2]) / 2 : 24, dy = at ? at[1] - (b[1] + b[3]) / 2 : 24;
    const fresh = this.clip.items.map(i => { const c = clone(i); c.id = uid(); c.u = now(); moveItem(c, dx, dy); return c; });
    this.edit(key, items => items.concat(fresh));
    this.setSel({ key, ids: new Set(fresh.map(i => i.id)) });
  },
  duplicateSel() { if (!this.sel) return; const key = this.sel.key; this.copySel(false); this.paste(key); },
  /** «ملصقاتي»: التحديد بيتحفظ ملصق تعيد استخدامه في أي كتاب. */
  saveSelAsSticker(name) {
    const its = this.selItems(); if (!its.length) return;
    const b = this.selBox();
    const items = clone(its).map(i => { moveItem(i, -b[0], -b[1]); delete i.u; return i; });
    this.prefs.myStickers.push({ id: uid(), name: name || ("ملصق " + (this.prefs.myStickers.length + 1)), w: b[2] - b[0], h: b[3] - b[1], items });
    this.savePrefs();
    if (this.onTool) this.onTool();
  },
  placeMySticker(key, st, at) {
    const fresh = st.items.map(i => { const c = clone(i); c.id = uid(); c.u = now(); c.ly = this.layer; moveItem(c, at[0] - st.w / 2, at[1] - st.h / 2); return c; });
    this.edit(String(key), items => items.concat(fresh));
  },
  clearPage(key, what) {
    key = String(key);
    this.edit(key, items => items.filter(i => what === "all" ? false : what === "temp" ? i.ly !== "t" : what.startsWith("#") ? (i.c || "").toLowerCase() !== what.toLowerCase() : true));
    this.setSel(null);
  },
  /**
   * بعد محرر الصفحات: الشرح يمشي مع صفحته (ترتيب جديد/لف/تكرار/مسح).
   * entries = الصفحات الجديدة بالترتيب: {kind:"src", index (من 0), rot} أو غيرها (من غير شرح).
   * aspects[index] = الطول÷العرض لصفحة الكتاب القديمة. العناصر بتاخد أرقام جديدة والقديمة بتتمسح (عشان الأجهزة التانية).
   */
  remapPages(entries, aspects) {
    const t = now(), old = this.doc.pages, next = new Map();
    entries.forEach((e, i) => {
      if (e.kind !== "src") return;
      const items = old.get(String(e.index + 1)) || [];
      if (!items.length) return;
      next.set(String(i + 1), items.map(it => {
        const c = clone(it); c.id = uid(); c.u = t;
        let H = (aspects[e.index] || 1.414) * W;
        for (let r = ((e.rot || 0) % 360 + 360) % 360; r > 0; r -= 90) {
          // لفة 90° مع عقارب الساعة: (u,v) ← (H−v, u) وبعدين نصغّر للعرض الجديد
          rotateItem(c, [0, 0], 90); moveItem(c, H, 0);
          const k = W / H; scaleItem(c, [0, 0], k, k); H = W * W / H;
          if (c.t === "shape" && c.sw) c.sw = r2(c.sw * k); // سُمك الخط كمان (الصفحة بقت أعرض)
        }
        return c;
      }));
    });
    for (const items of old.values()) for (const it of items) this.doc.del.set(it.id, t);
    const keys = new Set([...old.keys(), ...next.keys()]);
    this.doc.pages = next;
    for (const k of keys) this.doc.dirty.add(k);
    this.undo = []; this.redo = []; this.sel = null;
    this.changed([]);
  },
  resetCurtains() {
    for (const [k, items] of this.doc.pages) if (items.some(i => i.t === "curtain" && i.rv)) this.edit(k, its => its.map(i => { if (i.t === "curtain") { i.rv = 0; i.u = now(); } return i; }));
  },

  // ---------------- الماوس والقلم واللمس
  bindPointer(s) {
    const L = s.layer;
    let act = null;
    const touches = new Map(); // اللمس: صباع = رسم (أو تحريك لو «القلم بس يرسم»)، صباعين = تحريك وتكبير
    let gesture = null;
    const startGesture = () => {
      if (act && act.cancel) act.cancel(); else if (act) act.end(act.lastP || [0, 0], {});
      act = null;
      const pts = [...touches.values()];
      gesture = { pts: pts.map(p => [p.x, p.y]), d0: pts.length > 1 ? Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y) : 0, scale: 1 };
    };
    L.addEventListener("pointerdown", e => {
      if (!this.on || this.tool === "hand") return;
      if (!L.isConnected || !L.getBoundingClientRect().width) return; // الصفحة بتتعاد رسمتها
      if (e.target.closest && e.target.closest(".ink-text.editing, .ink-note textarea")) return;
      if (e.pointerType === "pen") this._penSeen = now();
      if (e.pointerType === "touch") {
        e.preventDefault();
        touches.set(e.pointerId, { x: e.clientX, y: e.clientY });
        try { L.setPointerCapture(e.pointerId); } catch (x) { }
        // ماسك القلم أو «القلم بس يرسم»: كف الإيد مابيرسمش
        if (this.penOnly || now() - (this._penSeen || 0) < 1500 || touches.size > 1) { startGesture(); return; }
      } else if (e.button !== 0 && e.button !== 5 && e.pointerType !== "pen") return;
      e.preventDefault(); e.stopPropagation();
      try { L.setPointerCapture(e.pointerId); } catch (x) { }
      const p = this.toPage(s, e);
      const dbl = this.dblHit(s, e, p);
      if (dbl) {
        // دبل كليك على نص: نلغي اللي عملته الدوسة الأولى (نص فاضي جديد مثلًا) ونعدّل النص
        e.preventDefault(); e.stopPropagation();
        this.setSel(null);
        setTimeout(() => this.editText(s, dbl.id), 0);
        return;
      }
      const eraserButton = e.pointerType === "pen" && (e.button === 5 || (e.buttons & 32));
      act = this.begin(s, e, p, eraserButton ? "eraser" : this.tool);
      act.lastP = p;
    });
    L.addEventListener("pointermove", e => {
      if (this.on && (this.tool === "laser" || this.tool === "lens") && !act && e.pointerType !== "touch") { this.hover(s, e); }
      if (e.pointerType === "touch" && touches.has(e.pointerId)) {
        touches.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (gesture) {
          const pts = [...touches.values()];
          const cx = pts.reduce((a, p) => a + p.x, 0) / pts.length, cy = pts.reduce((a, p) => a + p.y, 0) / pts.length;
          const ox = gesture.pts.reduce((a, p) => a + p[0], 0) / gesture.pts.length, oy = gesture.pts.reduce((a, p) => a + p[1], 0) / gesture.pts.length;
          if (this.onPan && gesture.pts.length === pts.length) this.onPan(ox - cx, oy - cy);
          if (pts.length > 1 && gesture.d0 > 0 && this.onPinch) {
            const d = Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y), sc = d / gesture.d0;
            if (Math.abs(sc - 1) > 0.12) { this.onPinch(sc, cx, cy); gesture.d0 = d; }
          }
          gesture.pts = pts.map(p => [p.x, p.y]);
          return;
        }
      }
      if (!act || act.pid !== e.pointerId) return;
      e.preventDefault();
      const evs = e.getCoalescedEvents ? e.getCoalescedEvents() : [e];
      if (!L.isConnected) return;
      for (const ce of evs.length ? evs : [e]) { const q = this.toPage(s, ce); act.lastP = q; act.move(q, ce); }
    });
    const end = e => {
      if (e.pointerType === "touch") { touches.delete(e.pointerId); if (gesture) { if (!touches.size) gesture = null; else startGesture(); return; } }
      if (!act || act.pid !== e.pointerId) return;
      const a = act; act = null;
      try { L.releasePointerCapture(e.pointerId); } catch (x) { }
      a.end(L.isConnected ? this.toPage(s, e) : a.lastP, e);
    };
    L.addEventListener("pointerup", end);
    L.addEventListener("pointercancel", e => { if (act && act.cancel && act.pid === e.pointerId) { act.cancel(); act = null; } else end(e); });
    L.addEventListener("pointerleave", e => { if (this.tool === "lens") this.lens.hide(); });
  },
  /** دبل كليك (بنحسبه إحنا: الماوس بيبقى ممسوك على الطبقة فالمتصفح مابيبعتش dblclick). */
  dblHit(s, e, p) {
    const t = now(), last = this._lastDown;
    this._lastDown = { t, x: e.clientX, y: e.clientY, key: s.key };
    if (!last || last.key !== s.key || t - last.t > 450 || Math.hypot(e.clientX - last.x, e.clientY - last.y) > 8) return null;
    if (!["select", "text", "callout", "pen", "shape"].includes(this.tool)) return null;
    this._lastDown = null;
    return [...this.doc.items(s.key)].reverse().find(i => (i.t === "text" || i.t === "callout") && hitDist(i, p) <= 4) || null;
  },
  hover(s, e) {
    if (this.tool === "laser") this.laser.point(e.clientX, e.clientY);
    if (this.tool === "lens") this.lens.show(s, e);
  },
  /** شكل تحت الطرف (للموصّلات): مستطيلات/دواير/نجوم/ملصقات/نص/صور. */
  anchorAt(key, p) {
    const items = this.doc.items(key);
    for (let i = items.length - 1; i >= 0; i--) {
      const it = items[i];
      if (it.lock || !isAnchor(it)) continue;
      const b = bbox(it);
      if (p[0] >= b[0] - 10 && p[0] <= b[2] + 10 && p[1] >= b[1] - 10 && p[1] <= b[3] + 10) return it;
    }
    return null;
  },
  topAt(s, p, tol = 6) {
    const items = this.doc.items(s.key);
    for (let i = items.length - 1; i >= 0; i--) if (!items[i].lock && hitDist(items[i], p) <= tol) return items[i];
    return null;
  },

  /** بداية حركة: بيرجّع {move, end} حسب الأداة. */
  begin(s, e, p, tool) {
    const pid = e.pointerId, key = s.key, self = this;
    const pr = e.pointerType === "pen" && e.pressure > 0 && e.pressure !== 0.5;
    if (tool === "pen") {
      const name = this.pen, o = this.penOpts(name), base = PENS[name];
      // ضغط القلم، وميل القلم بيعرّض الرصاص والماركر زي الحقيقي
      const tilted = (name === "pencil" || name === "marker");
      const pv = ev => { if (!pr) return 0.5; let v = ev.pressure || 0.5; if (tilted && (ev.tiltX || ev.tiltY)) { const t = Math.min(90, Math.hypot(ev.tiltX || 0, ev.tiltY || 0)); v = Math.min(1, v * (1 + t / 90)); } return r2(v); };
      const straight = base.straight || e.shiftKey;
      p = this.ruler.snap(s, p);
      const pc = s.dark && /^#(111|111111|000000)$/i.test(o.c) ? "#f1f3f5" : o.c;
      const it = { id: uid(), t: "stroke", pen: name, c: pc, size: o.size, o: o.o, pts: [[p[0], p[1], pv(e)]], ly: this.layer };
      if (pr) it.pr = 1;
      if (base.temp) it.tempPen = true;
      const live = el("path", { fill: it.c, "fill-opacity": it.o, class: base.blend ? "ink-hl" : "" }, s.live);
      let raf = 0, still = 0, lastMove = now();
      const draw = () => { raf = 0; live.setAttribute("d", strokePath(it)); };
      return {
        pid,
        cancel() { if (raf) cancelAnimationFrame(raf); live.remove(); },
        move(q, ev) {
          q = self.ruler.snap(s, q);
          if (straight || ev.shiftKey) {
            const a = it.pts[0];
            let dx = q[0] - a[0], dy = q[1] - a[1];
            const ang = Math.round(Math.atan2(dy, dx) / (Math.PI / 12)) * (Math.PI / 12), L = Math.hypot(dx, dy);
            if (ev.shiftKey || base.straight) { dx = Math.cos(ang) * L; dy = Math.sin(ang) * L; }
            it.pts = [a, [r2(a[0] + dx), r2(a[1] + dy), 0.5]];
          } else {
            const last = it.pts[it.pts.length - 1];
            if (dist(last, q) < 0.6) { if (now() - lastMove > 650 && name === "hl" && it.pts.length > 3) still = 1; return; }
            lastMove = now();
            it.pts.push([q[0], q[1], pv(ev)]);
          }
          if (!raf) raf = requestAnimationFrame(draw);
        },
        end() {
          if (raf) cancelAnimationFrame(raf);
          live.remove();
          // قلم التظليل: لو وقفت ثانية في الآخر الخط يتعدّل مستقيم
          if (still && name === "hl") it.pts = [it.pts[0], it.pts[it.pts.length - 1]];
          if (it.tempPen) { self.tempStroke(s, it); return; }
          if (self.recognize && !straight && (name === "pen" || name === "marker" || name === "pencil")) {
            const sh = recognize(it.pts);
            if (sh) { self.add(key, { t: "shape", ...sh, c: it.c, sw: Math.max(1.5, it.size * 0.8), ly: it.ly }); return; }
          }
          self.add(key, it);
        }
      };
    }
    if (tool === "eraser") {
      const mode = this.eraser, R = mode === "partial" ? 9 : 7;
      const before = clone(this.doc.items(key));
      let changed = false;
      const pts = [p];
      const cursor = el("circle", { cx: p[0], cy: p[1], r: R, class: "ink-eraser" }, s.live);
      const lasso = mode === "area" ? el("path", { class: "ink-lasso" }, s.live) : null;
      const eraseAt = q => {
        const items = this.doc.items(key);
        let out = [];
        for (const it of items) {
          if (it.lock) { out.push(it); continue; }
          if (mode === "partial" && it.t === "stroke") {
            const runs = []; let cur = [];
            for (const pt of it.pts) { if (dist(pt, q) <= R + it.size / 2) { if (cur.length) runs.push(cur); cur = []; } else cur.push(pt); }
            if (cur.length) runs.push(cur);
            if (runs.length === 1 && runs[0].length === it.pts.length) { out.push(it); continue; }
            changed = true; this.doc.del.set(it.id, now());
            for (const run of runs) if (run.length >= 2) out.push({ ...it, id: uid(), u: now(), pts: run });
          } else if (mode !== "area" && hitDist(it, q) <= R) { changed = true; this.doc.del.set(it.id, now()); }
          else out.push(it);
        }
        if (changed) { this.doc.pages.set(key, out); this.render(s); s.live.appendChild(cursor); if (lasso) s.live.appendChild(lasso); }
      };
      if (mode !== "area") eraseAt(p);
      return {
        pid,
        move(q) {
          cursor.setAttribute("cx", q[0]); cursor.setAttribute("cy", q[1]);
          if (mode === "area") { pts.push(q); lasso.setAttribute("d", "M " + pts.map(x => x.join(" ")).join(" L ") + " Z"); }
          else { const last = pts[pts.length - 1]; const n = Math.ceil(dist(last, q) / 4); for (let i = 1; i <= n; i++) eraseAt([last[0] + (q[0] - last[0]) * i / n, last[1] + (q[1] - last[1]) * i / n]); pts.push(q); }
        },
        end() {
          cursor.remove(); if (lasso) lasso.remove();
          if (mode === "area" && pts.length > 3) {
            const items = self.doc.items(key);
            const keep = items.filter(it => { if (it.lock) return true; const b = bbox(it); const inside = inPoly([(b[0] + b[2]) / 2, (b[1] + b[3]) / 2], pts); if (inside) self.doc.del.set(it.id, now()); return !inside; });
            if (keep.length !== items.length) { changed = true; self.doc.pages.set(key, keep); }
          }
          if (changed) { self.pushUndo(key, before); self.changed([key]); self.render(s); }
        }
      };
    }
    if (tool === "shape") {
      const k = this.shape, sp = this.prefs.shape;
      p = this.snapPt(p);
      const it = { id: uid(), t: "shape", k, x1: p[0], y1: p[1], x2: p[0], y2: p[1], c: this.prefs.color, sw: sp.sw, fill: sp.fill || null, fo: sp.fo, ly: this.layer };
      const g = el("g", {}, s.live);
      return {
        pid,
        move(q, ev) {
          q = self.snapPt(q);
          let x2 = q[0], y2 = q[1];
          if (ev.shiftKey) {
            if (k === "line" || k === "arrow" || k === "arrow2") {
              const dx = x2 - it.x1, dy = y2 - it.y1, ang = Math.round(Math.atan2(dy, dx) / (Math.PI / 12)) * (Math.PI / 12), L = Math.hypot(dx, dy);
              x2 = it.x1 + Math.cos(ang) * L; y2 = it.y1 + Math.sin(ang) * L;
            } else { const m = Math.max(Math.abs(x2 - it.x1), Math.abs(y2 - it.y1)); x2 = it.x1 + Math.sign(x2 - it.x1 || 1) * m; y2 = it.y1 + Math.sign(y2 - it.y1 || 1) * m; }
          }
          it.x2 = r2(x2); it.y2 = r2(y2);
          g.textContent = ""; shapeElems(it, g);
        },
        end() {
          g.remove();
          if (Math.hypot(it.x2 - it.x1, it.y2 - it.y1) < 6) return;
          if (["line", "arrow", "arrow2"].includes(k)) {
            // موصّل: لو الطرف على شكل، بيتلزق فيه ويمشي معاه
            const t1 = self.anchorAt(key, [it.x1, it.y1]), t2 = self.anchorAt(key, [it.x2, it.y2]);
            if (t1) it.a1 = t1.id; if (t2 && t2 !== t1) it.a2 = t2.id;
            if (it.a1 || it.a2) connect(it, self.doc.items(key));
          }
          if (!["line", "arrow", "arrow2"].includes(k)) { const x0 = Math.min(it.x1, it.x2), y0 = Math.min(it.y1, it.y2), x1 = Math.max(it.x1, it.x2), y1 = Math.max(it.y1, it.y2); Object.assign(it, { x1: x0, y1: y0, x2: x1, y2: y1 }); }
          self.add(key, it);
        }
      };
    }
    if (tool === "select") return this.beginSelect(s, e, p);
    if (tool === "laser") { this.laser.point(e.clientX, e.clientY); return { pid, move: (q, ev) => this.laser.point(ev.clientX, ev.clientY), end() { } }; }
    if (tool === "spot") {
      this.spot.start(e.clientX, e.clientY);
      return { pid, move: (q, ev) => this.spot.drag(ev.clientX, ev.clientY), end: (q, ev) => this.spot.release(ev.clientX, ev.clientY) };
    }
    if (tool === "lens") { this.lens.show(s, e); return { pid, move: (q, ev) => this.lens.show(s, ev), end() { } }; }
    // أدوات بتحط حاجة بكليك واحدة (وبالسحب بتحدد مقاسها)
    const start = this.snapPt(p);
    const preview = el("rect", { class: "ink-drag", x: start[0], y: start[1], width: 0, height: 0 }, s.live);
    let last = start;
    return {
      pid,
      move(q) { last = self.snapPt(q); preview.setAttribute("x", Math.min(start[0], last[0])); preview.setAttribute("y", Math.min(start[1], last[1])); preview.setAttribute("width", Math.abs(last[0] - start[0])); preview.setAttribute("height", Math.abs(last[1] - start[1])); },
      end() {
        preview.remove();
        const dragged = Math.abs(last[0] - start[0]) > 12 && Math.abs(last[1] - start[1]) > 12;
        const box = dragged ? [Math.min(start[0], last[0]), Math.min(start[1], last[1]), Math.abs(last[0] - start[0]), Math.abs(last[1] - start[1])] : null;
        self.place(s, tool, start, box);
      }
    };
  },

  /** كليك بأداة «حط»: نص، ملاحظة، فقاعة، رقم، ملصق، ستارة. */
  place(s, tool, p, box) {
    const key = s.key, T = this.prefs.text;
    // كليك على ستارة: تتكشف ربع ربع (بأي أداة حط)
    const hit = this.topAt(s, p, 0);
    if (hit && hit.t === "curtain" && (tool !== "curtain" || !box)) { this.revealCurtain(s, hit.id); return; }
    // كليك بأداة النص على نص موجود = تعديله (مش نص جديد فوقه)
    const onText = !box && [...this.doc.items(key)].reverse().find(i => (i.t === "text" || i.t === "callout") && hitDist(i, p) <= 4);
    if (onText && (tool === "text" || tool === "callout")) { requestAnimationFrame(() => this.editText(s, onText.id)); return; }
    if (tool === "text") {
      const it = this.add(key, { t: "text", x: p[0], y: p[1] - (T.size * 0.6), w: box ? box[2] : 320, h: T.size * 1.5, text: "", font: T.font, size: T.size, c: T.c, bg: T.bg || null, bold: T.bold || null, align: T.align, dir: T.dir });
      this.undo.pop(); // التراجع بيتسجل لما الكتابة تخلص
      requestAnimationFrame(() => this.editText(s, it.id, true));
    } else if (tool === "note") {
      this.add(key, { t: "note", x: p[0] - 13, y: p[1] - 13, text: "", c: this.prefs.color === "#111111" ? "#ffd43b" : this.prefs.color, open: true, _focus: true });
    } else if (tool === "callout") {
      const b = box || [p[0], p[1], 260, 90];
      const it = this.add(key, { t: "callout", x: b[0], y: b[1], w: b[2], h: b[3], tip: [b[0] + b[2] * 0.25, b[1] + b[3] + 70], text: "", font: T.font, size: Math.min(T.size, 22), c: this.prefs.color, bg: "#fff9db" });
      this.undo.pop();
      requestAnimationFrame(() => this.editText(s, it.id, true));
    } else if (tool === "step") {
      const n = this.doc.items(key).filter(i => i.t === "step").reduce((m, i) => Math.max(m, i.n || 0), 0) + 1;
      this.add(key, { t: "step", x: p[0], y: p[1], n, r: box ? Math.max(10, Math.min(box[2], box[3]) / 2) : 16, c: this.prefs.color });
    } else if (tool === "sticker" && this.sticker) {
      const st = this.sticker;
      if (st.mine) { this.placeMySticker(key, st.mine, p); return; }
      const w = box ? box[2] : st.emoji ? 60 : st.stamp ? Math.max(140, st.stamp.length * 22) : st.img ? (st.w || 200) : STICKERS.find(x => x.id === st.sid)?.arrow != null ? 110 : 150;
      const h = box ? box[3] : st.emoji ? 60 : st.stamp ? 64 : st.img ? (st.h || 150) : STICKERS.find(x => x.id === st.sid)?.arrow != null ? 60 : 44;
      this.add(key, { t: st.img ? "image" : "sticker", x: p[0] - w / 2, y: p[1] - h / 2, w, h, ...clone(st), rot: st.stamp ? -6 : 0 });
    } else if (tool === "curtain") {
      const b = box || [p[0] - 150, p[1] - 40, 300, 80];
      this.add(key, { t: "curtain", x: b[0], y: b[1], w: b[2], h: b[3], rv: 0, c: "#ced4da" });
    }
  },
  revealCurtain(s, id) {
    this.edit(s.key, items => items.map(i => { if (i.id === id) { i.rv = i.rv >= 0.99 ? 0 : Math.min(1, (i.rv || 0) + 0.25); i.u = now(); } return i; }));
  },
  /** القلم المؤقت: بيظهر ويختفي بالتدريج من غير ما يتسجل. */
  tempStroke(s, it) {
    const g = renderSvg(it); g.classList.add("ink-fading");
    s.svg.appendChild(g);
    setTimeout(() => g.remove(), 3200);
  },

  // ---------------- التحديد: كليك، Shift+كليك، دايرة حوالين، نقل، تكبير، لف
  beginSelect(s, e, p) {
    const key = s.key, self = this;
    const h = e.target.closest && e.target.closest(".ink-selbox .h");
    const inBox = this.sel && this.sel.key === key && (() => { const b = this.selBox(); return b && p[0] >= b[0] && p[0] <= b[2] && p[1] >= b[1] && p[1] <= b[3]; })();
    if (h || inBox) {
      // تحريك/تكبير/لف اللي متحدد
      const before = clone(this.doc.items(key)), b = this.selBox(), orig = clone(this.selItems());
      const c = [(b[0] + b[2]) / 2, (b[1] + b[3]) / 2], mode = h ? h.dataset.h : "move";
      return {
        pid: e.pointerId,
        move(q, ev) {
          const items = self.doc.items(key);
          const map = new Map(orig.map(o => [o.id, o]));
          const out = items.map(i => {
            const o = map.get(i.id); if (!o) return i;
            const n = clone(o);
            if (mode === "move") {
              moveItem(n, q[0] - p[0], q[1] - p[1]);
              // موصّل اتسحب لوحده (من غير الشكل اللي ماسك فيه): بيتفك
              if (n.a1 && !map.has(n.a1)) delete n.a1;
              if (n.a2 && !map.has(n.a2)) delete n.a2;
            }
            else if (mode === "rot") { const a = Math.atan2(q[1] - c[1], q[0] - c[0]) - Math.atan2(p[1] - c[1], p[0] - c[0]); rotateItem(n, c, a * 180 / Math.PI); }
            else {
              const ax = mode.includes("w") ? b[2] : b[0], ay = mode.includes("n") ? b[3] : b[1];
              let sx = (q[0] - ax) / ((mode.includes("w") ? b[0] : b[2]) - ax), sy = (q[1] - ay) / ((mode.includes("n") ? b[1] : b[3]) - ay);
              if (ev.shiftKey || orig.some(x => x.t === "sticker" || x.t === "image" || x.t === "step")) sx = sy = Math.max(Math.abs(sx), Math.abs(sy)) * Math.sign(sx || 1);
              scaleItem(n, [ax, ay], Math.max(0.05, sx), Math.max(0.05, sy));
            }
            return n;
          });
          connectAll(out);
          self.doc.pages.set(key, out);
          self.render(s);
        },
        end() {
          for (const i of self.doc.items(key)) if (self.sel.ids.has(i.id)) i.u = now();
          self.pushUndo(key, before); self.changed([key]); self.render(s);
        }
      };
    }
    const hit = this.topAt(s, p);
    if (hit) {
      const ids = e.shiftKey && this.sel && this.sel.key === key ? new Set(this.sel.ids) : new Set();
      if (ids.has(hit.id) && e.shiftKey) ids.delete(hit.id); else ids.add(hit.id);
      this.setSel({ key, ids });
      return this.beginSelect(s, { ...e, target: s.layer, shiftKey: false, pointerId: e.pointerId }, p);
    }
    // دايرة حوالين
    const pts = [p];
    const path = el("path", { class: "ink-lasso" }, s.live);
    return {
      pid: e.pointerId,
      move(q) { pts.push(q); path.setAttribute("d", "M " + pts.map(x => x.join(" ")).join(" L ") + " Z"); },
      end() {
        path.remove();
        if (pts.length < 4) { self.setSel(null); return; }
        const ids = new Set();
        for (const it of self.doc.items(key)) {
          if (it.lock) continue;
          if (it.t === "stroke") { const n = it.pts.filter(pt => inPoly(pt, pts)).length; if (n > it.pts.length / 2) ids.add(it.id); }
          else { const b = bbox(it); if (inPoly([(b[0] + b[2]) / 2, (b[1] + b[3]) / 2], pts)) ids.add(it.id); }
        }
        self.setSel(ids.size ? { key, ids } : null);
      }
    };
  },

  // ---------------- لصق صورة من الحافظة
  onPaste(e) {
    if (!this.on) return;
    if (document.activeElement && (document.activeElement.isContentEditable || /INPUT|TEXTAREA/.test(document.activeElement.tagName))) return;
    const s = this.visibleSurface(); if (!s) return;
    const file = [...(e.clipboardData?.items || [])].find(i => i.type.startsWith("image/"));
    if (file) { e.preventDefault(); this.insertImageFile(file.getAsFile(), s); return; }
    if (this.clip) { e.preventDefault(); this.paste(s.key); }
  },
  visibleSurface() {
    let best = null, area = 0;
    for (const s of this.surfaces.values()) {
      const r = s.layer.getBoundingClientRect();
      const a = Math.max(0, Math.min(r.bottom, innerHeight) - Math.max(r.top, 0)) * Math.max(0, Math.min(r.right, innerWidth) - Math.max(r.left, 0));
      if (a > area) { area = a; best = s; }
    }
    return best;
  },
  /** صورة ← بتتصغّر لحد 1600 بكسل وتتحط في نص الجزء الظاهر من الصفحة. */
  async insertImageFile(file, s, at) {
    if (!file) return;
    s ||= this.visibleSurface(); if (!s) return;
    const url = await new Promise(r => { const fr = new FileReader(); fr.onload = () => r(fr.result); fr.readAsDataURL(file); });
    const img = await new Promise((r, j) => { const i = new Image(); i.onload = () => r(i); i.onerror = j; i.src = url; });
    const max = 1600, k = Math.min(1, max / Math.max(img.width, img.height));
    const c = document.createElement("canvas"); c.width = Math.round(img.width * k); c.height = Math.round(img.height * k);
    c.getContext("2d").drawImage(img, 0, 0, c.width, c.height);
    const png = file.type === "image/png" || file.type === "image/gif";
    const data = c.toDataURL(png ? "image/png" : "image/jpeg", 0.86);
    const r = s.layer.getBoundingClientRect();
    const vis = [Math.max(r.top, 0), Math.min(r.bottom, innerHeight)];
    const cy = at ? at[1] : ((vis[0] + vis[1]) / 2 - r.top) / r.width * W;
    const w = Math.min(500, W * 0.6), h = w * c.height / c.width;
    this.add(s.key, { t: "image", img: data, x: at ? at[0] - w / 2 : (W - w) / 2, y: cy - h / 2, w, h });
  }
};

// ---------------------------------------------------------------- الموصّلات
const isAnchor = it => (it.t === "shape" && !["line", "arrow", "arrow2", "brace", "check", "cross"].includes(it.k)) || ["sticker", "text", "callout", "image", "step"].includes(it.t);
/** نقطة على حدود الشكل في اتجاه نقطة تانية (مستطيل/دايرة). */
function borderPoint(it, toward) {
  const b = bbox(it), c = [(b[0] + b[2]) / 2, (b[1] + b[3]) / 2];
  const dx = toward[0] - c[0], dy = toward[1] - c[1];
  if (!dx && !dy) return c;
  const hw = (b[2] - b[0]) / 2 + 4, hh = (b[3] - b[1]) / 2 + 4;
  if ((it.t === "shape" && it.k === "ellipse") || it.t === "step") {
    const t = 1 / Math.sqrt((dx * dx) / (hw * hw) + (dy * dy) / (hh * hh));
    return [r2(c[0] + dx * t), r2(c[1] + dy * t)];
  }
  const t = Math.min(hw / Math.abs(dx || 1e-9), hh / Math.abs(dy || 1e-9));
  return [r2(c[0] + dx * t), r2(c[1] + dy * t)];
}
const centerOf = it => { const b = bbox(it); return [(b[0] + b[2]) / 2, (b[1] + b[3]) / 2]; };
/** خط/سهم ملزوق: أطرافه بتتحسب من الأشكال اللي ماسك فيها. */
function connect(line, items) {
  const A = line.a1 && items.find(i => i.id === line.a1), B = line.a2 && items.find(i => i.id === line.a2);
  if (!A && !B) return false;
  const pa = A ? centerOf(A) : [line.x1, line.y1], pb = B ? centerOf(B) : [line.x2, line.y2];
  if (A) [line.x1, line.y1] = borderPoint(A, pb);
  if (B) [line.x2, line.y2] = borderPoint(B, pa);
  return true;
}
function connectAll(items) {
  for (const it of items) if (it.t === "shape" && (it.a1 || it.a2)) connect(it, items);
}

// ---------------------------------------------------------------- تحويلات العناصر
function moveItem(i, dx, dy) {
  if (i.t === "stroke") i.pts = i.pts.map(p => [r2(p[0] + dx), r2(p[1] + dy), p[2]]);
  else if (i.t === "shape") { i.x1 += dx; i.x2 += dx; i.y1 += dy; i.y2 += dy; if (i.poly) i.poly = i.poly.map(p => [p[0] + dx, p[1] + dy]); }
  else { i.x += dx; i.y += dy; if (i.tip) i.tip = [i.tip[0] + dx, i.tip[1] + dy]; }
}
function scaleItem(i, a, sx, sy) {
  const f = p => [r2(a[0] + (p[0] - a[0]) * sx), r2(a[1] + (p[1] - a[1]) * sy)];
  if (i.t === "stroke") { i.pts = i.pts.map(p => [...f(p), p[2]]); i.size = r2(i.size * Math.sqrt(Math.abs(sx * sy))); }
  else if (i.t === "shape") { [i.x1, i.y1] = f([i.x1, i.y1]); [i.x2, i.y2] = f([i.x2, i.y2]); if (i.poly) i.poly = i.poly.map(f); }
  else if (i.t === "step") { [i.x, i.y] = f([i.x, i.y]); i.r = r2((i.r || 16) * Math.sqrt(Math.abs(sx * sy))); }
  else if (i.t === "note") { [i.x, i.y] = f([i.x, i.y]); }
  else {
    const p0 = f([i.x, i.y]), p1 = f([i.x + (i.w || 0), i.y + (i.h || 0)]);
    i.x = Math.min(p0[0], p1[0]); i.y = Math.min(p0[1], p1[1]); i.w = Math.abs(p1[0] - p0[0]); i.h = Math.abs(p1[1] - p0[1]);
    if (i.tip) i.tip = f(i.tip);
    if (i.t === "text" && i.size) i.size = r2(i.size * Math.sqrt(Math.abs(sx * sy)));
  }
}
function rotateItem(i, c, deg) {
  if (i.t === "stroke") { i.pts = i.pts.map(p => [...rot(p, c, deg).map(r2), p[2]]); return; }
  if (i.t === "shape" && ["line", "arrow", "arrow2"].includes(i.k)) { [i.x1, i.y1] = rot([i.x1, i.y1], c, deg); [i.x2, i.y2] = rot([i.x2, i.y2], c, deg); return; }
  if (i.t === "shape" && i.poly) { i.poly = i.poly.map(p => rot(p, c, deg)); return; }
  const b = i.t === "shape" ? [Math.min(i.x1, i.x2), Math.min(i.y1, i.y2), Math.max(i.x1, i.x2), Math.max(i.y1, i.y2)] : [i.x, i.y, i.x + (i.w || 0), i.y + (i.h || 0)];
  const ic = [(b[0] + b[2]) / 2, (b[1] + b[3]) / 2], nc = rot(ic, c, deg);
  moveItem(i, nc[0] - ic[0], nc[1] - ic[1]);
  if (i.t !== "step" && i.t !== "note") i.rot = r2(((i.rot || 0) + deg) % 360);
}
export const _test = { moveItem, scaleItem, rotateItem, hitDist, inPoly };
export { renderSvg };

// =====================================================================
//  أدوات العرض: ليزر، كشاف، عدسة، مسطرة ومنقلة
// =====================================================================
class Laser {
  constructor(host) { this.c = document.createElement("canvas"); this.c.className = "ink-laser"; host.appendChild(this.c); this.pts = []; this.raf = 0; }
  point(x, y) {
    this.c.width = innerWidth; this.c.height = innerHeight;
    this.pts.push([x, y, now()]);
    if (!this.raf) this.raf = requestAnimationFrame(() => this.draw());
  }
  draw() {
    this.raf = 0;
    const ctx = this.c.getContext("2d"), t = now();
    this.pts = this.pts.filter(p => t - p[2] < 700);
    ctx.clearRect(0, 0, this.c.width, this.c.height);
    for (let i = 1; i < this.pts.length; i++) {
      const a = this.pts[i - 1], b = this.pts[i], life = 1 - (t - b[2]) / 700;
      ctx.strokeStyle = `rgba(255,40,40,${life * 0.8})`; ctx.lineWidth = 2 + life * 6; ctx.lineCap = "round";
      ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke();
    }
    const h = this.pts[this.pts.length - 1];
    if (h) {
      const g = ctx.createRadialGradient(h[0], h[1], 0, h[0], h[1], 16);
      g.addColorStop(0, "rgba(255,255,255,1)"); g.addColorStop(0.3, "rgba(255,40,40,1)"); g.addColorStop(1, "rgba(255,40,40,0)");
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(h[0], h[1], 16, 0, Math.PI * 2); ctx.fill();
      this.raf = requestAnimationFrame(() => this.draw());
    }
  }
  stop() { this.pts = []; const ctx = this.c.getContext("2d"); ctx.clearRect(0, 0, this.c.width, this.c.height); }
}
class Spotlight {
  constructor(host) { this.d = document.createElement("div"); this.d.className = "ink-spot"; this.d.hidden = true; host.appendChild(this.d); this.r = 130; this.rect = null; }
  show() { this.d.hidden = false; this.place(innerWidth / 2, innerHeight / 2); }
  hide() { this.d.hidden = true; }
  place(x, y) { const d = this.d; this.rect = null; d.classList.remove("rect"); d.style.left = (x - this.r) + "px"; d.style.top = (y - this.r) + "px"; d.style.width = d.style.height = (this.r * 2) + "px"; }
  start(x, y) { this.sx = x; this.sy = y; this.moved = false; this.d.hidden = false; this.place(x, y); }
  drag(x, y) {
    if (Math.abs(x - this.sx) + Math.abs(y - this.sy) > 14) this.moved = true;
    if (!this.moved) return;
    const d = this.d; d.classList.add("rect");
    d.style.left = Math.min(x, this.sx) + "px"; d.style.top = Math.min(y, this.sy) + "px";
    d.style.width = Math.abs(x - this.sx) + "px"; d.style.height = Math.abs(y - this.sy) + "px";
  }
  release(x, y) { if (!this.moved) this.place(x, y); }
  wheel(dy) { this.r = clamp(this.r - dy * 0.3, 50, 600); }
}
class Lens {
  constructor(host, ink) { this.c = document.createElement("canvas"); this.c.className = "ink-lens"; this.c.width = this.c.height = 240; this.c.hidden = true; host.appendChild(this.c); this.ink = ink; this.z = 2.2; }
  show(s, e) {
    const page = s.el.querySelector("canvas");
    this.c.hidden = false;
    this.c.style.left = (e.clientX - 120) + "px"; this.c.style.top = (e.clientY - 120) + "px";
    const ctx = this.c.getContext("2d");
    ctx.fillStyle = "#fff"; ctx.fillRect(0, 0, 240, 240);
    if (!page) return;
    const r = page.getBoundingClientRect(), kx = page.width / r.width, ky = page.height / r.height;
    const sw = 240 / this.z;
    ctx.drawImage(page, (e.clientX - r.left - sw / 2) * kx, (e.clientY - r.top - sw / 2) * ky, sw * kx, sw * ky, 0, 0, 240, 240);
  }
  hide() { this.c.hidden = true; }
}
/** مسطرة (القلم بيمشي على حرفها) ومنقلة (لقياس الزوايا) — بيتسحبوا ويتلفوا. */
class Ruler {
  constructor(host, kind) {
    this.kind = kind;
    const d = this.d = document.createElement("div");
    d.className = "ink-" + kind; d.hidden = true;
    const ang = document.createElement("div"); ang.className = "ang"; d.appendChild(ang); this.angEl = ang;
    const rh = document.createElement("div"); rh.className = "rh"; rh.title = "لف"; d.appendChild(rh);
    if (kind === "ruler") for (let i = 0; i <= 30; i++) { const t = document.createElement("i"); t.style.left = (i / 30 * 100) + "%"; if (i % 5 === 0) t.className = "big"; d.appendChild(t); }
    else for (let i = 0; i <= 18; i++) { const t = document.createElement("i"); t.style.transform = `rotate(${-i * 10}deg)`; if (i % 3 === 0) t.className = "big"; d.appendChild(t); }
    host.appendChild(d);
    this.x = innerWidth / 2 - 260; this.y = innerHeight / 2; this.a = 0;
    let drag = null;
    d.addEventListener("pointerdown", e => {
      e.preventDefault(); e.stopPropagation(); d.setPointerCapture(e.pointerId);
      const r = d.getBoundingClientRect(), c = [r.left + r.width / 2, r.top + r.height / 2];
      drag = e.target === rh ? { rot: true, c, a0: this.a, s: Math.atan2(e.clientY - c[1], e.clientX - c[0]) } : { dx: e.clientX - this.x, dy: e.clientY - this.y };
    });
    d.addEventListener("pointermove", e => {
      if (!drag) return;
      if (drag.rot) { this.a = drag.a0 + (Math.atan2(e.clientY - drag.c[1], e.clientX - drag.c[0]) - drag.s) * 180 / Math.PI; if (e.shiftKey) this.a = Math.round(this.a / 15) * 15; }
      else { this.x = e.clientX - drag.dx; this.y = e.clientY - drag.dy; }
      this.place();
    });
    d.addEventListener("pointerup", () => { drag = null; });
    d.addEventListener("wheel", e => { e.preventDefault(); this.a += e.deltaY > 0 ? 1 : -1; this.place(); }, { passive: false });
  }
  toggle() { this.d.hidden = !this.d.hidden; this.place(); }
  place() {
    this.d.style.left = this.x + "px"; this.d.style.top = this.y + "px"; this.d.style.transform = `rotate(${this.a}deg)`;
    this.angEl.textContent = Math.round(((this.a % 360) + 360) % 360) + "°";
  }
  /** المسطرة: نقطة قريبة من حرفها اللي فوق بتتلزق عليه. */
  snap(s, q) {
    if (this.kind !== "ruler" || this.d.hidden) return q;
    const lr = s.layer.getBoundingClientRect(), k = lr.width / W;
    const px = [lr.left + q[0] * k, lr.top + q[1] * k];
    const a = this.a * Math.PI / 180, o = [this.x, this.y], dir = [Math.cos(a), Math.sin(a)];
    const v = [px[0] - o[0], px[1] - o[1]], t = v[0] * dir[0] + v[1] * dir[1], len = this.d.offsetWidth;
    if (t < -10 || t > len + 10) return q;
    const n = [-dir[1], dir[0]], d = v[0] * n[0] + v[1] * n[1];
    if (Math.abs(d) > 22) return q;
    const sp = [o[0] + dir[0] * t, o[1] + dir[1] * t];
    return [r2((sp[0] - lr.left) / k), r2((sp[1] - lr.top) / k)];
  }
}
