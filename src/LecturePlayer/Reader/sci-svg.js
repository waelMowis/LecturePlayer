// =====================================================================
//  أدوات رسم SVG صغيرة للأشكال العلمية (كل الإحداثيات بوحدات الصفحة: عرضها 1000).
//  اللون الأساسي currentColor (بيتحدد من لون العنصر)، والكلام بخط النظام عشان العربي.
// =====================================================================
export const FONT = "Segoe UI, Tahoma, Arial, sans-serif";
export const esc = s => String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const n = v => Math.round(v * 100) / 100;
const AR = /[؀-ۿ]/;

export function text(x, y, s, o = {}) {
  if (s == null || s === "") return "";
  const size = o.size || 20, anchor = o.anchor || "middle";
  const dir = AR.test(String(s)) ? ' direction="rtl" unicode-bidi="embed"' : "";
  // العربي RTL: «start» بقى يمين — بنعكس المحاذاة عشان المكان يفضل زي ما طلبنا
  const a = dir && anchor !== "middle" ? (anchor === "start" ? "end" : "start") : anchor;
  return `<text x="${n(x)}" y="${n(y)}" font-size="${size}" text-anchor="${a}" dominant-baseline="${o.base || "middle"}" fill="${o.fill || "currentColor"}" font-family="${FONT}"${o.bold ? ' font-weight="700"' : ""}${o.italic ? ' font-style="italic"' : ""}${dir}>${esc(s)}</text>`;
}
export function line(x1, y1, x2, y2, o = {}) {
  return `<line x1="${n(x1)}" y1="${n(y1)}" x2="${n(x2)}" y2="${n(y2)}" stroke="${o.stroke || "currentColor"}" stroke-width="${o.w || 2.5}" stroke-linecap="round"${o.dash ? ` stroke-dasharray="${o.dash}"` : ""}/>`;
}
export function path(d, o = {}) {
  return `<path d="${d}" fill="${o.fill || "none"}" stroke="${o.stroke || "currentColor"}" stroke-width="${o.w ?? 2.5}" stroke-linejoin="round" stroke-linecap="round"${o.dash ? ` stroke-dasharray="${o.dash}"` : ""}${o.op != null ? ` fill-opacity="${o.op}"` : ""}/>`;
}
export function circle(cx, cy, r, o = {}) {
  return `<circle cx="${n(cx)}" cy="${n(cy)}" r="${n(r)}" fill="${o.fill || "none"}" stroke="${o.stroke || "currentColor"}" stroke-width="${o.w ?? 2.5}"${o.dash ? ` stroke-dasharray="${o.dash}"` : ""}${o.op != null ? ` fill-opacity="${o.op}"` : ""}/>`;
}
export function rect(x, y, w, h, o = {}) {
  return `<rect x="${n(x)}" y="${n(y)}" width="${n(w)}" height="${n(h)}" rx="${o.rx || 0}" fill="${o.fill || "none"}" stroke="${o.stroke || "currentColor"}" stroke-width="${o.w ?? 2.5}"${o.dash ? ` stroke-dasharray="${o.dash}"` : ""}${o.op != null ? ` fill-opacity="${o.op}"` : ""}/>`;
}
export function poly(pts, o = {}) {
  return path("M " + pts.map(p => n(p[0]) + " " + n(p[1])).join(" L ") + (o.open ? "" : " Z"), o);
}
/** سهم: خط وراسه مثلث مملي. */
export function arrow(x1, y1, x2, y2, o = {}) {
  const w = o.w || 3, hl = o.head || Math.max(10, w * 4), a = Math.atan2(y2 - y1, x2 - x1);
  const bx = x2 - Math.cos(a) * hl, by = y2 - Math.sin(a) * hl;
  const p1 = [bx + Math.cos(a + Math.PI / 2) * hl * 0.45, by + Math.sin(a + Math.PI / 2) * hl * 0.45];
  const p2 = [bx - Math.cos(a + Math.PI / 2) * hl * 0.45, by - Math.sin(a + Math.PI / 2) * hl * 0.45];
  let s = line(x1, y1, bx, by, { ...o, w });
  s += `<path d="M ${n(x2)} ${n(y2)} L ${n(p1[0])} ${n(p1[1])} L ${n(p2[0])} ${n(p2[1])} Z" fill="${o.stroke || "currentColor"}" stroke="none"/>`;
  if (o.both) s += arrow(bx, by, x1, y1, { ...o, both: false }).replace(/<line[^>]*\/>/, "");
  return s;
}
/** قوس زاوية (بالدرجات، 0 = يمين، عكس عقارب الساعة على الشاشة). */
export function arc(cx, cy, r, a1, a2, o = {}) {
  const rad = d => -d * Math.PI / 180;
  const x1 = cx + r * Math.cos(rad(a1)), y1 = cy + r * Math.sin(rad(a1)), x2 = cx + r * Math.cos(rad(a2)), y2 = cy + r * Math.sin(rad(a2));
  const large = Math.abs(a2 - a1) % 360 > 180 ? 1 : 0;
  return path(`M ${n(x1)} ${n(y1)} A ${n(r)} ${n(r)} 0 ${large} 0 ${n(x2)} ${n(y2)}`, o);
}
export function sector(cx, cy, r, a1, a2, o = {}) {
  const rad = d => -d * Math.PI / 180;
  const x1 = cx + r * Math.cos(rad(a1)), y1 = cy + r * Math.sin(rad(a1)), x2 = cx + r * Math.cos(rad(a2)), y2 = cy + r * Math.sin(rad(a2));
  const large = Math.abs(a2 - a1) % 360 > 180 ? 1 : 0;
  return path(`M ${n(cx)} ${n(cy)} L ${n(x1)} ${n(y1)} A ${n(r)} ${n(r)} 0 ${large} 0 ${n(x2)} ${n(y2)} Z`, o);
}
/** علامة الزاوية القائمة عند الرأس p بين اتجاهين. */
export function rightMark(p, a, b, s = 16) {
  const u = norm([a[0] - p[0], a[1] - p[1]]), v = norm([b[0] - p[0], b[1] - p[1]]);
  const q1 = [p[0] + u[0] * s, p[1] + u[1] * s], q2 = [p[0] + (u[0] + v[0]) * s, p[1] + (u[1] + v[1]) * s], q3 = [p[0] + v[0] * s, p[1] + v[1] * s];
  return path(`M ${n(q1[0])} ${n(q1[1])} L ${n(q2[0])} ${n(q2[1])} L ${n(q3[0])} ${n(q3[1])}`, { w: 1.8 });
}
export const norm = v => { const l = Math.hypot(v[0], v[1]) || 1; return [v[0] / l, v[1] / l]; };
/** زاوية رأس (درجات الشاشة) من p ناحية q. */
export const angOf = (p, q) => -Math.atan2(q[1] - p[1], q[0] - p[0]) * 180 / Math.PI;

/** يلف الرسم في SVG مستقل بمقاسه ولونه ← data URL. */
export function wrap(inner, w, h, color, pad = 6) {
  const W2 = w + pad * 2, H2 = h + pad * 2;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${-pad} ${-pad} ${n(W2)} ${n(H2)}" width="${n(W2 * 2)}" height="${n(H2 * 2)}" color="${color}" style="color:${color}" direction="ltr">${inner}</svg>`;
  return { url: "data:image/svg+xml;base64," + btoa(unescape(encodeURIComponent(svg))), w: W2, h: H2, svg };
}
