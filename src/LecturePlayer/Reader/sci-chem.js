// =====================================================================
//  كيمياء السبورة: قراءة معادلة مكتوبة بصيغة mhchem (2H2 + O2 -> 2H2O) وموازنتها.
//  الموازنة = حل نظام خطي (عدد ذرات كل عنصر + الشحنة) بكسور مضبوطة (BigInt) بلا تقريب.
// =====================================================================

const ARROWS = ["<=>>", "<<=>", "<=>", "<->", "->", "<-", "="];

/** بيفصل المعادلة لطرفين والسهم (والكلام اللي فوق/تحت السهم بين أقواس مربعة). */
export function splitEquation(text) {
  const s = String(text || "").trim();
  for (const a of ARROWS) {
    // السهم لازم يبقى بين مسافات أو حروف عادية (مش جزء من شحنة زي ^-)
    const re = new RegExp("(^|\\s)" + a.replace(/[<>=-]/g, c => "\\" + c) + "((?:\\[[^\\]]*\\]){0,2})(?=\\s|$)");
    const m = re.exec(s);
    if (m) {
      const at = m.index + m[1].length;
      return { left: s.slice(0, at).trim(), arrow: a + m[2], right: s.slice(at + a.length + m[2].length).trim() };
    }
  }
  return null;
}

/** بيقسم طرف لمواد: «+» بين مسافات بس (عشان شحنة زي Na+ ماتتقسمش). */
function splitSide(side) {
  return side.split(/\s+\+\s+/).map(x => x.trim()).filter(Boolean);
}

/** مادة واحدة ← {coef, body, formula, charge}: المعامل في الأول، والحالة (s)(aq) وعلامات ^ v بتتشال من الحساب. */
export function parseTerm(term) {
  let t = term.trim();
  let coef = null;
  const cm = /^(\d+(?:\/\d+)?)\s*(?=[A-Z(\[e^])/.exec(t);
  if (cm) { coef = cm[1]; t = t.slice(cm[0].length); }
  const body = t;
  // نشيل علامات الغاز والراسب والحالة
  let f = t.replace(/\s+[\^v]\s*$/, "").replace(/\((s|l|g|aq)\)\s*$/i, "").trim();
  // الشحنة: ^{2+} أو ^2+ أو ^+ أو + / - في الآخر (Na+، Cl-، e-)
  let charge = 0;
  const ch = /\^\{?(\d*)([+-])\}?$/.exec(f);
  if (ch) {
    charge = (ch[1] ? +ch[1] : 1) * (ch[2] === "+" ? 1 : -1);
    f = f.slice(0, ch.index);
  } else {
    // زي mhchem: Fe2+ (عنصر واحد) = شحنة 2+، لكن MnO4- (أكتر من عنصر) = الرقم أسفل والشحنة 1-
    const m = /(\d*)([+-])$/.exec(f);
    if (m) {
      const rest = f.slice(0, m.index), single = /^[A-Z][a-z]?$/.test(rest);
      charge = (single && m[1] ? +m[1] : 1) * (m[2] === "+" ? 1 : -1);
      f = single ? rest : rest + m[1];
    }
  }
  return { coef, body, formula: f, charge };
}

/** عدد ذرات كل عنصر في صيغة (بالأقواس والماء المتبلور CuSO4*5H2O). */
export function countAtoms(formula) {
  const out = new Map();
  const add = (m, k) => { for (const [e, n] of m) out.set(e, (out.get(e) || 0) + n * k); };
  const parts = String(formula).split(/[*·.]/);
  for (const raw of parts) {
    let p = raw.trim(); if (!p) continue;
    let k = 1;
    const km = /^(\d+)/.exec(p);
    if (km && parts.length > 1) { k = +km[1]; p = p.slice(km[1].length); }
    if (p === "e") { continue; } // الإلكترون: شحنته بس
    add(parseGroup(p), k);
  }
  return out;
}

function parseGroup(s) {
  let i = 0;
  function group(close) {
    const m = new Map();
    while (i < s.length) {
      const c = s[i];
      if (c === "(" || c === "[") {
        i++;
        const inner = group(c === "(" ? ")" : "]");
        const n = num();
        for (const [e, v] of inner) m.set(e, (m.get(e) || 0) + v * n);
      } else if (c === ")" || c === "]") {
        if (c !== close) throw new Error("قوس غير متطابق في " + s);
        i++; return m;
      } else if (/[A-Z]/.test(c)) {
        let e = c; i++;
        while (i < s.length && /[a-z]/.test(s[i])) e += s[i++];
        const n = num();
        m.set(e, (m.get(e) || 0) + n);
      } else if (c === "_" || c === "{" || c === "}") { i++; }
      else throw new Error("رمز غير مفهوم «" + c + "» في " + s);
    }
    if (close) throw new Error("قوس غير مغلق في " + s);
    return m;
  }
  function num() {
    let d = "";
    if (s[i] === "_") i++;
    if (s[i] === "{") { i++; while (i < s.length && /\d/.test(s[i])) d += s[i++]; if (s[i] === "}") i++; }
    else while (i < s.length && /\d/.test(s[i])) d += s[i++];
    return d ? +d : 1;
  }
  return group(null);
}

// ---------- كسور مضبوطة
const babs = a => a < 0n ? -a : a;
const bgcd = (a, b) => { a = babs(a); b = babs(b); while (b) [a, b] = [b, a % b]; return a; };
const frac = (n, d = 1n) => { if (d < 0n) { n = -n; d = -d; } const g = bgcd(n, d) || 1n; return [n / g, d / g]; };
const fsub = (a, b) => frac(a[0] * b[1] - b[0] * a[1], a[1] * b[1]);
const fmul = (a, b) => frac(a[0] * b[0], a[1] * b[1]);
const fdiv = (a, b) => frac(a[0] * b[1], a[1] * b[0]);

/** موازنة: بترجّع المعاملات (أعداد صحيحة أصغر ما يمكن) أو ترمي خطأ مفهوم. */
export function balance(text) {
  const eq = splitEquation(text);
  if (!eq) throw new Error("اكتب المعادلة بسهم بين الطرفين، مثل: H2 + O2 -> H2O");
  const L = splitSide(eq.left).map(parseTerm), R = splitSide(eq.right).map(parseTerm);
  if (!L.length || !R.length) throw new Error("أحد طرفي المعادلة فارغ");
  const terms = [...L, ...R];
  const atoms = terms.map(t => countAtoms(t.formula));
  const elems = [...new Set(atoms.flatMap(m => [...m.keys()]))];
  const rows = elems.map(e => terms.map((t, j) => BigInt((atoms[j].get(e) || 0) * (j < L.length ? 1 : -1))));
  if (terms.some(t => t.charge)) rows.push(terms.map((t, j) => BigInt(t.charge * (j < L.length ? 1 : -1))));
  // حذف جاوس (صيغة صفية مختزلة) بكسور
  const n = terms.length;
  const M = rows.map(r => r.map(v => frac(v)));
  const piv = [];
  let r = 0;
  for (let c = 0; c < n && r < M.length; c++) {
    let p = r; while (p < M.length && M[p][c][0] === 0n) p++;
    if (p === M.length) continue;
    [M[r], M[p]] = [M[p], M[r]];
    const pv = M[r][c];
    M[r] = M[r].map(x => fdiv(x, pv));
    for (let k = 0; k < M.length; k++) if (k !== r && M[k][c][0] !== 0n) {
      const f = M[k][c];
      M[k] = M[k].map((x, j) => fsub(x, fmul(f, M[r][j])));
    }
    piv.push(c); r++;
  }
  const free = [...Array(n).keys()].filter(c => !piv.includes(c));
  if (free.length === 0) throw new Error("لا توجد موازنة ممكنة — راجع الصيغ");
  if (free.length > 1) throw new Error("للمعادلة أكثر من موازنة ممكنة — اكتبها تفاعلين منفصلين");
  const fc = free[0];
  const sol = Array(n).fill(null);
  sol[fc] = frac(1n);
  piv.forEach((c, i) => { sol[c] = frac(-M[i][fc][0], M[i][fc][1]); });
  // كلها لازم تبقى بنفس الإشارة
  const sign = sol.find(x => x[0] !== 0n)[0] < 0n ? -1n : 1n;
  if (sol.some(x => x[0] * sign <= 0n)) throw new Error("لا توجد موازنة بمعاملات موجبة — راجع الطرفين");
  let lcm = 1n; for (const x of sol) lcm = lcm / bgcd(lcm, x[1]) * x[1];
  let ints = sol.map(x => x[0] * sign * (lcm / x[1]));
  const g = ints.reduce((a, b) => bgcd(a, b));
  ints = ints.map(x => Number(x / g));
  return { left: L, right: R, arrow: eq.arrow, coefs: ints };
}

/** يكتب المعادلة من جديد بالمعاملات (1 مابيتكتبش). hide = مكان فاضي للتمرين. */
export function rebuild(parts, coefs, hide) {
  const { left, right, arrow } = parts;
  const one = (t, k) => {
    const name = t.body;
    if (hide) return "$\\underline{\\hspace{1.4em}}$ " + name;
    return (k && k !== 1 ? k + " " : "") + name;
  };
  const lc = coefs ? coefs.slice(0, left.length) : left.map(t => t.coef ? +t.coef : 1);
  const rc = coefs ? coefs.slice(left.length) : right.map(t => t.coef ? +t.coef : 1);
  return left.map((t, i) => one(t, lc[i])).join(" + ") + " " + arrow + " " + right.map((t, i) => one(t, rc[i])).join(" + ");
}

/** نفس المعادلة من غير موازنة (عشان «إخفاء المعاملات» على معادلة مكتوبة). */
export function parseOnly(text) {
  const eq = splitEquation(text);
  if (!eq) return null;
  return { left: splitSide(eq.left).map(parseTerm), right: splitSide(eq.right).map(parseTerm), arrow: eq.arrow };
}

/** هل المعادلة موزونة فعلًا بالمعاملات المكتوبة؟ (للفحص قبل الإدراج) */
export function isBalanced(text) {
  const p = parseOnly(text); if (!p) return false;
  const tot = new Map(); let q = 0;
  const run = (arr, s) => arr.forEach(t => {
    const k = (t.coef ? +t.coef : 1) * s;
    for (const [e, n] of countAtoms(t.formula)) tot.set(e, (tot.get(e) || 0) + n * k);
    q += t.charge * k;
  });
  run(p.left, 1); run(p.right, -1);
  return q === 0 && [...tot.values()].every(v => v === 0);
}
