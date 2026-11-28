// =====================================================================
//  الجدول الدوري بالعربي (المرحلة الرابعة): البيانات من mendeleev (MIT) — العدد الذري والكتلة والتوزيع
//  والسالبية الكهربية والحالة… والرسم SVG فيكتور: بطاقة عنصر أو الجدول كاملًا، ويتعدلوا بالنقر المزدوج.
// =====================================================================
import { text, rect, line } from "./sci-svg.js";

let data = null;
/** [Z، الرمز، الاسم العربي، الإنجليزي، الكتلة، المجموعة، الدورة، الفئة الفرعية، السلسلة، السالبية، التوزيع، الكثافة، الانصهار K، الغليان K، الحالة، سنة الاكتشاف] */
export function loadElements() {
  data ||= fetch("lib/sci/elements.json").then(r => { if (!r.ok) throw new Error("بيانات الجدول الدوري غير موجودة"); return r.json(); })
    .then(rows => rows.map(r => ({ z: r[0], sym: r[1], ar: r[2], en: r[3], mass: r[4], group: r[5], period: r[6], block: r[7], series: r[8], en_: r[9], conf: r[10], density: r[11], mp: r[12], bp: r[13], state: r[0] >= 104 ? "u" : r[14], year: r[15] })))
    .catch(e => { data = null; throw e; });
  return data;
}

export const SERIES = {
  alkali: ["فلزات قلوية", "#ffb3a7"], alkaline: ["فلزات قلوية ترابية", "#ffd8a8"], transition: ["فلزات انتقالية", "#ffe8cc"], post: ["فلزات بعد انتقالية", "#c3fae8"],
  metalloid: ["أشباه فلزات", "#d3f9d8"], nonmetal: ["لافلزات", "#d0ebff"], halogen: ["هالوجينات", "#e5dbff"], noble: ["غازات نبيلة", "#f3d9fa"],
  lanthanide: ["لانثانيدات", "#fff3bf"], actinide: ["أكتينيدات", "#ffe3e3"]
};
export const STATE = { s: "صلب", l: "سائل", g: "غاز", u: "غير معروفة (مُخلَّق)" };
const AR_DIG = "٠١٢٣٤٥٦٧٨٩";
const dig = (v, ar) => ar ? String(v).replace(/\d/g, d => AR_DIG[d]).replace(".", "٫") : String(v);
const SUP = { "0": "⁰", "1": "¹", "2": "²", "3": "³", "4": "⁴", "5": "⁵", "6": "⁶", "7": "⁷", "8": "⁸", "9": "⁹" };
/** [Ar] 3d6 4s2 ← [Ar] 3d⁶ 4s² (والرقم الناقص معناه 1). */
export const confText = c => String(c || "").replace(/(\d)([spdf])(\d*)/g, (m, n, l, e) => n + l + [...(e || "1")].map(x => SUP[x]).join(""));
/** مكان العنصر في الشبكة: [العمود 1–18، الصف 1–10] (اللانثانيدات والأكتينيدات في صفين تحت). */
export function cellOf(e) {
  if (e.series === "lanthanide" && e.z !== 71) return [e.z - 57 + 3, 9];
  if (e.series === "actinide" && e.z !== 103) return [e.z - 89 + 3, 10];
  if (e.z === 71) return [3, 6];
  if (e.z === 103) return [3, 7];
  return [e.group || 3, e.period];
}
const massText = (m, ar) => m == null ? "—" : dig(m >= 100 && Math.round(m) === m ? `[${m}]` : (Math.round(m * 1000) / 1000), ar);

/** بطاقة عنصر (زي اللي في الكتاب). */
export async function drawElement(p) {
  const els = await loadElements();
  const z = Math.max(1, Math.min(118, Math.round(+p.z || 1))), e = els[z - 1], ar = !!p.ar;
  const fill = p.color !== 0 && p.color !== false ? (SERIES[e.series]?.[1] || "#f1f3f5") : "#ffffff";
  const Wd = 240, Ht = p.conf ? 280 : 250;
  let s = rect(4, 4, Wd - 8, Ht - 8, { w: 3, fill, rx: 10 });
  s += text(22, 36, dig(z, ar), { size: 28, bold: true, anchor: "start", fill: "#111" });
  if (p.mass) s += text(Wd - 22, 36, massText(e.mass, ar), { size: 20, anchor: "end", fill: "#111" });
  s += text(Wd / 2, 118, e.sym, { size: 84, bold: true, fill: "#111" });
  s += text(Wd / 2, 182, e.ar, { size: 30, bold: true, fill: "#111" });
  if (p.en) s += text(Wd / 2, 214, e.en, { size: 18, fill: "#495057" });
  if (p.conf) s += text(Wd / 2, p.en ? 248 : 222, confText(e.conf), { size: 18, fill: "#343a40" });
  return { inner: s, w: Wd, h: Ht };
}

/** الجدول كاملًا (18 عمود + صفا اللانثانيدات والأكتينيدات) بالرموز والأعداد والأسماء العربية. */
export async function drawTable(p) {
  const els = await loadElements();
  const C = 62, Hh = 72, X0 = 10, Y0 = p.title ? 58 : 10, ar = !!p.ar, gap = 16;
  let s = p.title ? text(X0 + 9 * C, 30, "الجدول الدوري للعناصر", { size: 30, bold: true }) : "";
  for (const e of els) {
    const [c, r] = cellOf(e), x = X0 + (c - 1) * C, y = Y0 + (r - 1) * Hh + (r >= 9 ? gap : 0);
    const fill = p.color === "block" ? ({ s: "#ffc9c9", p: "#a5d8ff", d: "#ffec99", f: "#b2f2bb" }[e.block] || "#fff") : (SERIES[e.series]?.[1] || "#f8f9fa");
    s += rect(x + 1, y + 1, C - 2, Hh - 2, { w: 1.2, fill, stroke: "#495057" });
    s += text(x + 5, y + 12, dig(e.z, ar), { size: 11, anchor: "start", fill: "#111" });
    s += text(x + C / 2, y + 36, e.sym, { size: 22, bold: true, fill: "#111" });
    if (p.names) s += text(x + C / 2, y + 60, e.ar, { size: e.ar.length > 8 ? 8.5 : 10.5, fill: "#212529" });
  }
  // أرقام المجموعات والدورات
  if (p.nums) {
    for (let g = 1; g <= 18; g++) s += text(X0 + (g - 1) * C + C / 2, Y0 - 6, dig(g, ar), { size: 12, fill: "#868e96" });
    for (let r = 1; r <= 7; r++) s += text(X0 + 18 * C + 12, Y0 + (r - 1) * Hh + Hh / 2, dig(r, ar), { size: 12, fill: "#868e96" });
  }
  // مفتاح الألوان
  if (p.legend && p.color !== "block") {
    let lx = X0 + 2 * C + 6, ly = Y0 + 10;
    Object.values(SERIES).forEach(([name, col], i) => {
      const cx = lx + (i % 3) * 205, cy = ly + Math.floor(i / 3) * 24;
      s += rect(cx, cy - 8, 16, 16, { w: 1, fill: col, stroke: "#495057" }) + text(cx + 22, cy, name, { size: 13, anchor: "start" });
    });
  }
  return { inner: s, w: X0 * 2 + 18 * C + (p.nums ? 24 : 0), h: Y0 + 10 * Hh + gap + 10 };
}
