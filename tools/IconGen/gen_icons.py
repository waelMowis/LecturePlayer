#!/usr/bin/env python3
"""
مولّد UI/IconGlyphs.Extra.g.cs (الأيقونات الإضافية: Fluent + شعارات اللغات والأدوات والمنصات).

التشغيل (المصادر من npm: @fluentui/svg-icons و simple-icons و devicon، كل واحد متفكوك في فولدر):
    python3 -I gen_icons.py --fluent <dir>/package --simple <dir>/package --devicon <dir>/package \
        --out ../../src/LecturePlayer/UI/IconGlyphs.Extra.g.cs --check <dir للصور>

بيعمل:
  - يحوّل كل SVG لمسار واحد بأوامر مطلقة M L Q C Z في مربع 0..1 (الأقواس A بتتحوّل لـ C)،
    واللي محتاج evenodd بيبدأ بـ «!» (IconGlyphs.Parse بيقراها FillMode.Alternate).
  - يرفض أي أيقونة فيها أكتر من لون واحد أو stroke (لأن الرسم عندنا لون واحد).
  - يتأكد إن الشكل الناتج مطابق للأصل: بيرسم الاتنين (cairo) ويقارن (IoU).
  - يطلع صورة معاينة لكل الأيقونات.
"""
import argparse, io, json, os, re, sys

from svgelements import SVG, Path, Shape, Move, Line, QuadraticBezier, CubicBezier, Arc, Close
import cairosvg
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from icon_list import ICONS  # noqa: E402

BRAND_BOX = 0.82          # الشعار بياخد 82% من المربع (زي هوامش Fluent تقريبًا)
IOU_MIN = 0.93  # الأيقونات الرفيعة (زي SQL Server) بتقل فيها النسبة من الحواف بس
RENDER = 96


def fmt(v):
    s = f"{v:.3f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def shapes_of(file):
    svg = SVG.parse(file)
    out = []
    for e in svg.elements():
        if not isinstance(e, Shape):
            continue
        fill = e.fill
        fill_v = None if fill is None or fill.value is None else fill.hexrgb
        fill_raw = (e.values.get("fill") or "").strip()
        if fill_raw.startswith("url("):
            fill_v = "url"
        stroke = e.stroke
        has_stroke = stroke is not None and stroke.value is not None and (e.stroke_width or 0) > 0
        if fill_v is None and not has_stroke:
            continue
        if e.values.get("visibility") == "hidden" or e.values.get("display") == "none":
            continue
        rule = (e.values.get("fill-rule") or "nonzero").strip()
        out.append((e, fill_v, has_stroke, rule))
    return out


def check_single_color(shapes):
    fills = {f for _, f, _, _ in shapes if f is not None}
    if any(s for _, _, s, _ in shapes):
        return "فيه stroke"
    if len(fills) > 1:
        return f"أكتر من لون: {sorted(fills)}"
    return None


def to_segments(shapes):
    """(قايمة أوامر بالإحداثيات الأصلية، evenodd؟)"""
    rules = {r for _, _, _, r in shapes}
    evenodd = "evenodd" in rules
    if len(rules) > 1:
        # خليط: الأشكال المنفصلة مش متداخلة غالبًا، فـ evenodd بيطلع نفس النتيجة — والمقارنة بالرسم بتأكد
        evenodd = True
    cmds = []
    for e, _, _, _ in shapes:
        p = abs(Path(e))
        for seg in p:
            if isinstance(seg, Move):
                cmds.append(("M", [seg.end]))
            elif isinstance(seg, Close):
                cmds.append(("Z", []))
            elif isinstance(seg, Line):
                if seg.start is not None and seg.end is not None and abs(seg.start - seg.end) < 1e-9:
                    continue
                cmds.append(("L", [seg.end]))
            elif isinstance(seg, QuadraticBezier):
                cmds.append(("Q", [seg.control, seg.end]))
            elif isinstance(seg, CubicBezier):
                cmds.append(("C", [seg.control1, seg.control2, seg.end]))
            elif isinstance(seg, Arc):
                for c in seg.as_cubic_curves():
                    cmds.append(("C", [c.control1, c.control2, c.end]))
            else:
                raise ValueError(f"segment مش معروف: {type(seg)}")
    return cmds, evenodd


def bbox_of(shapes):
    xs0, ys0, xs1, ys1 = [], [], [], []
    for e, _, _, _ in shapes:
        b = abs(Path(e)).bbox()
        if b is None:
            continue
        xs0.append(b[0]); ys0.append(b[1]); xs1.append(b[2]); ys1.append(b[3])
    return min(xs0), min(ys0), max(xs1), max(ys1)


def emit(cmds, mapx, mapy, evenodd):
    parts = ["!" if evenodd else ""]
    for c, pts in cmds:
        parts.append(c + " ".join(f"{fmt(mapx(p.x))} {fmt(mapy(p.y))}" for p in pts))
    return "".join(parts)


def svg_of_path(d, color="#000000"):
    rule = "evenodd" if d.startswith("!") else "nonzero"
    d = d.lstrip("!")
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1" width="{RENDER}" height="{RENDER}">'
            f'<path fill="{color}" fill-rule="{rule}" d="{d}"/></svg>')


def alpha(png_bytes):
    return Image.open(io.BytesIO(png_bytes)).convert("RGBA").split()[3]


def iou(a, b):
    pa, pb = a.load(), b.load()
    inter = union = 0
    for y in range(a.height):
        for x in range(a.width):
            u, v = pa[x, y] > 127, pb[x, y] > 127
            inter += u and v
            union += u or v
    return 1.0 if union == 0 else inter / union


def reference_alpha(file, kind, box):
    """رسم الأصل بـ cairo (مستقل عن svgelements) بنفس التحويل لـ 0..1."""
    if kind == "fl":
        return alpha(cairosvg.svg2png(url=file, output_width=RENDER, output_height=RENDER))
    # الشعارات: نرسم كبير ونقص حوالين المربع اللي اتحطّ فيه الشعار
    big = 1024
    raw = open(file, encoding="utf-8").read()
    vb = re.search(r'viewBox="([^"]+)"', raw)
    vx, vy, vw, vh = (float(t) for t in re.split(r"[ ,]+", vb.group(1).strip()))
    scale = big / max(vw, vh)
    im = alpha(cairosvg.svg2png(url=file, output_width=round(vw * scale), output_height=round(vh * scale)))
    x0, y0, size = box  # بإحداثيات الـ viewBox
    crop = ((x0 - vx) * scale, (y0 - vy) * scale, (x0 - vx + size) * scale, (y0 - vy + size) * scale)
    # crop ممكن يطلع بره الصورة (هوامش) — نلصق في كانفاس أكبر
    left, top = round(crop[0]), round(crop[1])
    canvas = Image.new("L", (round(size * scale), round(size * scale)), 0)
    canvas.paste(im, (-left, -top))
    return canvas.resize((RENDER, RENDER), Image.LANCZOS)


def devicon_candidates(dv, name):
    data = {d["name"]: d for d in json.load(open(os.path.join(dv, "devicon.json"), encoding="utf-8"))}
    if name not in data:
        raise SystemExit(f"devicon: مش موجود {name}")
    versions = data[name]["versions"]["svg"]
    order = [v for v in ("plain", "original", "line") if v in versions]
    return [os.path.join(dv, "icons", name, f"{name}-{v}.svg") for v in order], data[name].get("color")


def si_slug(title):
    """نفس titleToSlug بتاعة Simple Icons."""
    import unicodedata
    t = title.lower().replace("+", "plus").replace(".", "dot").replace("&", "and")
    for a_, b_ in (("đ", "d"), ("ħ", "h"), ("ı", "i"), ("ĸ", "k"), ("ŀ", "l"), ("ł", "l"), ("ß", "ss"), ("ŧ", "t")):
        t = t.replace(a_, b_)
    t = unicodedata.normalize("NFD", t)
    return re.sub(r"[^a-z0-9]", "", t)


def readable_brand(hex6):
    if not hex6:
        return None
    h = hex6.lstrip("#")
    if len(h) == 3:
        h = "".join(c * 2 for c in h)
    r, g, b = int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)
    lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255
    if lum < 0.22:   # أسود/كحلي غامق: مش هيبان على الخلفية الغامقة → خليه على اللون العادي
        return None
    if lum < 0.45:   # غامق شوية: نفتّحه ناحية الأبيض عشان يبان (نفس فكرة IconGlyphs.Readable)
        t = min(0.6, (0.45 - lum) * 1.8 + 0.1)
        r, g, b = (round(c + (255 - c) * t) for c in (r, g, b))
    return f"#{r:02X}{g:02X}{b:02X}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--fluent", required=True)
    ap.add_argument("--simple", required=True)
    ap.add_argument("--devicon", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--check", required=True)
    ap.add_argument("--existing", required=True, help="IconGlyphs.g.cs (عشان المفاتيح ماتتكررش)")
    a = ap.parse_args()
    os.makedirs(a.check, exist_ok=True)

    existing = set(re.findall(r'^\s*\("[^"]*", "([^"]+)"', open(a.existing, encoding="utf-8").read(), re.M))
    si_data = {d.get("slug") or si_slug(d["title"]): d for d in json.load(open(os.path.join(a.simple, "data", "simple-icons.json"), encoding="utf-8"))}

    rows, colors, problems, report = [], {}, [], []
    keys = set()
    for cat, key, label, src in ICONS:
        if key in keys or key in existing:
            problems.append(f"{key}: مفتاح متكرر")
            continue
        keys.add(key)
        kind, name = src.split(":", 1)
        brand = None
        if kind == "fl":
            files = [os.path.join(a.fluent, "icons", f"{name}_20_filled.svg")]
        elif kind == "si":
            files = [os.path.join(a.simple, "icons", f"{name}.svg")]
            brand = si_data.get(name, {}).get("hex")
        else:
            files, brand = devicon_candidates(a.devicon, name)

        chosen = None
        why = []
        for f in files:
            if not os.path.exists(f):
                why.append(f"{os.path.basename(f)}: مش موجود"); continue
            shapes = shapes_of(f)
            bad = check_single_color(shapes)
            if bad:
                why.append(f"{os.path.basename(f)}: {bad}"); continue
            chosen = (f, shapes)
            break
        if not chosen:
            problems.append(f"{key}: " + " / ".join(why))
            continue
        f, shapes = chosen
        cmds, evenodd = to_segments(shapes)
        if kind == "fl":
            mapx = mapy = (lambda v: v / 20.0)
            box = None
        else:
            x0, y0, x1, y1 = bbox_of(shapes)
            size = max(x1 - x0, y1 - y0) / BRAND_BOX
            cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
            bx, by = cx - size / 2, cy - size / 2
            mapx = (lambda v, bx=bx, size=size: (v - bx) / size)
            mapy = (lambda v, by=by, size=size: (v - by) / size)
            box = (bx, by, size)
        d = emit(cmds, mapx, mapy, evenodd)

        mine = alpha(cairosvg.svg2png(bytestring=svg_of_path(d).encode()))
        ref = reference_alpha(f, kind, box)
        score = iou(mine, ref)
        report.append((key, os.path.basename(f), score, evenodd, len(d)))
        if score < IOU_MIN:
            problems.append(f"{key}: الشكل مختلف عن الأصل (IoU {score:.3f})")
            Image.merge("RGB", (ref, mine, Image.new("L", mine.size, 0))).save(os.path.join(a.check, f"diff_{key}.png"))
            continue
        rows.append((cat, key, label, d))
        bc = readable_brand(brand) if kind != "fl" else None
        if bc:
            colors[key] = bc

    for k, f, s, eo, n in report:
        print(f"{k:24} {f:40} IoU={s:.3f} {'evenodd' if eo else ''} {n}b")
    if problems:
        print("\n".join(["--- مشاكل ---"] + problems))
        sys.exit(1)

    write_cs(a.out, rows, colors)
    contact_sheet(rows, colors, os.path.join(a.check, "sheet.png"))
    print(f"OK: {len(rows)} أيقونة، {len(colors)} لون علامة")


HEADER = """// <auto-generated> مولّد بـ tools/IconGen/gen_icons.py — ماتعدّلش بإيدك.
//
// المصادر:
//  - Fluent UI System Icons — Copyright (c) 2020 Microsoft Corporation — MIT License
//    https://github.com/microsoft/fluentui-system-icons
//  - Devicon — Copyright (c) 2015 konpa — MIT License
//    https://github.com/devicons/devicon
//  - Simple Icons — CC0 1.0 Universal
//    https://github.com/simple-icons/simple-icons
//
// MIT License: Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
// الشعارات علامات تجارية لأصحابها، ومستخدمة هنا بس عشان المستخدم يعلّم بيها كورساته (من غير أي ادعاء بشراكة أو رعاية).
"""


def cs_str(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def write_cs(out, rows, colors):
    lines = [HEADER, "namespace LecturePlayer.UI;", "", "internal static partial class IconGlyphs", "{",
             "    /// <summary>الأيقونات الإضافية (الفئة، المفتاح، الاسم، المسار في 0..1؛ «!» في الأول = evenodd).</summary>",
             "    private static readonly (string Category, string Key, string Label, string Path)[] Extra =",
             "    {"]
    for cat, key, label, d in rows:
        lines.append(f"        ({cs_str(cat)}, {cs_str(key)}, {cs_str(label)}, {cs_str(d)}),")
    lines += ["    };", "",
              "    /// <summary>لون العلامة الأصلي (لو واضح على الخلفية الغامقة) — بيتقترح لما تختار الشعار.</summary>",
              "    private static readonly (string Key, string Hex)[] BrandHex =",
              "    {"]
    for k, v in colors.items():
        lines.append(f"        ({cs_str(k)}, {cs_str(v)}),")
    lines += ["    };", "}", ""]
    with open(out, "w", encoding="utf-8", newline="\n") as fp:
        fp.write("\n".join(lines))


def contact_sheet(rows, colors, out):
    cols, cell = 12, 84
    n = len(rows)
    rws = (n + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rws * cell), (30, 31, 34))
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 10)
    except Exception:
        font = ImageFont.load_default()
    dr = ImageDraw.Draw(sheet)
    for i, (cat, key, label, d) in enumerate(rows):
        x, y = (i % cols) * cell, (i // cols) * cell
        col = colors.get(key, "#9ECBF0")
        png = cairosvg.svg2png(bytestring=svg_of_path(d, col).encode().replace(b'width="96" height="96"', b'width="48" height="48"'))
        im = Image.open(io.BytesIO(png)).convert("RGBA")
        sheet.paste(im, (x + (cell - 48) // 2, y + 8), im)
        dr.text((x + 4, y + cell - 16), key[:14], fill=(170, 170, 170), font=font)
    sheet.save(out)


if __name__ == "__main__":
    main()
