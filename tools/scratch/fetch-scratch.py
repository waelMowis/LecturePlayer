# «البرمجة بالكتل (مبنية على Scratch 3)» داخل «مسار» — تعمل بدون إنترنت.
#
# بيجهّز فولدر فيه نسخة مجمّدة من scratch-gui 4.1.7 (آخر إصدار بترخيص BSD-3، نوفمبر 2024؛ الإصدارات من 5.0 بترخيص AGPL)
# + React 16.14 (MIT) + مكتبة الوسائط (الأزياء والخلفيات والأصوات، CC BY-SA 2.0) من مصادرها الرسمية:
#   - الحزم من npm بـ sha512 مثبّت (integrity).
#   - ملفات الوسائط من cdn.assets.scratch.mit.edu، وكل ملف اسمه هو الـ md5 بتاعه فبنتأكد منه.
# وبنعدّل نسخة الواجهة (من غير ما نغيّر سلوكها):
#   - العلامات التجارية: بنشيل القطة وجوبو وبيكو ونانو وتيرا وجيجا من المكتبة، ونبدّل كائن المشروع الجديد بشخصية «مسار».
#   - روابط الوسائط بتشاور على الفولدر المحلي (assets/) بدل الإنترنت.
#   - صور الدروس بلغات غير العربية والإنجليزية بنحذفها (الواجهة عربية بس).
#
# الاستخدام:  python tools/scratch/fetch-scratch.py <فولدر> [--no-assets]
import hashlib, base64, io, json, os, re, shutil, sys, tarfile, urllib.request, concurrent.futures

HERE = os.path.dirname(os.path.abspath(__file__))
PKGS = {
    "scratch-gui": ("https://registry.npmjs.org/scratch-gui/-/scratch-gui-4.1.7.tgz", "sha512-DZHERBC95J4rRE//OXytfnzPksZVir4zp3TVyvhv1xME9f8GJRbFFYe9ZTptSjZWyk2boUGlbu+ad0uE/x+A0A=="),
    "react": ("https://registry.npmjs.org/react/-/react-16.14.0.tgz", "sha512-0X2CImDkJGApiAlcf0ODKIneSwBPhqJawOa5wCtKbu7ZECrmS26NvtSILynQ66cgkT/RJ4LidJOc3bUESwmU8g=="),
    "react-dom": ("https://registry.npmjs.org/react-dom/-/react-dom-16.14.0.tgz", "sha512-1gCeQXDLoIqMgqD3IO2Ah9bnf0w9kzhwN5q4FGnHZ67hBm9yePzB5JJAIQCc8x3pFnNlwFq4RidZggNAAkzWWw=="),
}
CDN = "https://cdn.assets.scratch.mit.edu/internalapi/asset/{}/get/"
STAMP = "scratch-gui=4.1.7;react=16.14.0;v=3"
# العلامات التجارية لـ MIT (ملف TRADEMARK في الحزمة): مابنضمّنهاش
MARKS = re.compile(r"^(cat( 2| flying)?|gobo|pico( walking)?|nano|tera|giga( walking)?)$", re.I)
KEEP_LANG = {"ar", "en", "RTL", "LTR"}
# رسائل ناقصة الترجمة في العربية (بتظهر بالإنجليزي في الواجهة)
AR_FIX = {
    "gui.gui.defaultProjectTitle": "مشروعي",
    "gui.menuBar.settings": "الإعدادات", "gui.menuBar.language": "اللغة", "gui.menuBar.colorMode": "ألوان الواجهة",
    "gui.theme.default": "الأصلية", "gui.theme.dark": "داكنة", "gui.theme.highContrast": "تباين عالٍ",
    "gui.menuBar.modeMenu": "الوضع", "gui.menuBar.normalMode": "الوضع العادي", "gui.menuBar.caturdayMode": "وضع المرح",
    "gui.gui.shouldDeleteSprite": "هل تريد حذف هذا الكائن؟", "gui.gui.shouldDeleteCostume": "هل تريد حذف هذا المظهر؟",
    "gui.gui.shouldDeleteSound": "هل تريد حذف هذا الصوت؟", "gui.gui.confirm": "نعم", "gui.gui.deleteAssetHeading": "تأكيد الحذف",
    "gui.SpriteInfo.showSpriteAction": "إظهار الكائن", "gui.SpriteInfo.hideSpriteAction": "إخفاء الكائن",
    "gui.library.gettingStarted": "البداية", "gui.library.basics": "الأساسيات", "gui.library.intermediate": "متوسط", "gui.library.prompts": "أفكار",
}


def get(url, tries=4):
    last = None
    for _ in range(tries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "Masar-build"})
            with urllib.request.urlopen(req, timeout=60) as r:
                return r.read()
        except Exception as e:  # noqa: BLE001
            last = e
    raise RuntimeError(f"تعذّر تنزيل {url}: {last}")


def pkg(name):
    url, integrity = PKGS[name]
    data = get(url)
    algo, want = integrity.split("-", 1)
    got = base64.b64encode(hashlib.new(algo, data).digest()).decode()
    if got != want:
        raise RuntimeError(f"{name}: integrity مختلف ({got})")
    return tarfile.open(fileobj=io.BytesIO(data), mode="r:gz")


def js_str_unescape(s):
    return s.replace("\\'", "'").replace("\\\\", "\\")


def js_str_escape(s):
    return s.replace("\\", "\\\\").replace("'", "\\'")


def mark_md5(sprites):
    return {c["md5ext"] for s in sprites if MARKS.match(s.get("name", "")) for c in s.get("costumes", [])}


def patch_gui(js, character):
    n0 = len(js)
    # 1) المكتبات (JSON.parse('[...]')): من غير الكائنات العلامات التجارية وأزيائها
    LIB = re.compile(r"JSON\.parse\('(\[\{\"name\":\"(?:[^'\\]|\\.)*?\])'\)")
    arrays = [json.loads(js_str_unescape(m.group(1))) for m in LIB.finditer(js)]
    marks = {c["md5ext"] for arr in arrays for it in arr if isinstance(it, dict) and "costumes" in it and MARKS.match(it.get("name", "")) for c in it["costumes"]}
    if not marks:
        raise RuntimeError("ماقدرتش ألاقي مكتبة الكائنات في الواجهة")
    removed = []

    def lib(m):
        arr = json.loads(js_str_unescape(m.group(1)))
        keep = []
        for it in arr:
            if isinstance(it, dict) and (MARKS.match(it.get("name", "")) and "costumes" in it or it.get("md5ext") in marks):
                removed.append(it.get("name"))
                continue
            keep.append(it)
        return "JSON.parse('" + js_str_escape(json.dumps(keep, ensure_ascii=False, separators=(",", ":"))) + "')"

    js = LIB.sub(lib, js)
    # 2) كائن المشروع الجديد: شخصية «مسار» بدل القطة (زيّين)
    for asset_id, svg in (("bcf454acf82e4504149f7ffe07081dbc", character[0]), ("0fb9be3e8397c983338cb71dc84d0b25", character[1])):
        pat = re.compile(r'(\{id:"' + asset_id + r'",assetType:"ImageVector",dataFormat:"SVG",data:\w+\.encode\(\')((?:[^\'\\]|\\.)*)(\'\))')
        js, k = pat.subn(lambda m: m.group(1) + js_str_escape(svg).replace("\n", "\\n") + m.group(3), js)
        if k != 1:
            raise RuntimeError("ماقدرتش ألاقي زيّ المشروع الجديد " + asset_id)
    # 3) الرسائل العربية: ترجمة اللي ناقص، واسم المشروع الافتراضي، ومن غير اسم العلامة
    i = js.index('"gui.gui.defaultProjectTitle":"مشروع سكراتش"')
    a, depth = i, 0
    while True:
        a -= 1
        if js[a] == "}": depth += 1
        elif js[a] == "{":
            if depth == 0: break
            depth -= 1
    b, depth = i, 0
    while True:
        if js[b] == "{": depth += 1
        elif js[b] == "}":
            if depth == 0: break
            depth -= 1
        b += 1
    ar = js[a:b]
    for k, v in AR_FIX.items():
        ar = re.sub('"' + re.escape(k) + '":"[^"]*"', lambda m: '"' + k + '":"' + v + '"', ar)
    js = js[:a] + ar + js[b:]
    js = js.replace("سكراتش", "البرنامج")
    # 4) الوسائط من الفولدر المحلي
    for old, new in (('"/internalapi/asset/").concat(e.assetId,".").concat(e.dataFormat,"/get/")', '"/").concat(e.assetId,".").concat(e.dataFormat)'),
                     ('"https://cdn.assets.scratch.mit.edu/internalapi/asset/".concat(e,"/get/")', '"assets/".concat(e)')):
        if js.count(old) != 1:
            raise RuntimeError("رابط الوسائط اتغيّر: " + old)
        js = js.replace(old, new)
    print(f"  الواجهة: شلنا {len(removed)} عنصر علامة تجارية، الحجم {n0 // 1024} ← {len(js) // 1024} كيلوبايت")
    return js


def main():
    dest = os.path.abspath(sys.argv[1])
    with_assets = "--no-assets" not in sys.argv
    stamp = STAMP + (";assets" if with_assets else "")
    sf = os.path.join(dest, ".masar-stamp")
    if os.path.exists(sf) and open(sf).read().strip() == stamp and os.path.exists(os.path.join(dest, "scratch-gui.js")):
        print("✓ البرمجة بالكتل جاهزة (" + stamp + ")")
        copy_host(dest)
        return
    if os.path.exists(dest):
        shutil.rmtree(dest)
    os.makedirs(dest)
    character = [open(os.path.join(HERE, f"character-{i}.svg"), encoding="utf-8").read().strip() for i in (1, 2)]

    gui = pkg("scratch-gui")
    libs = {}
    for m in gui.getmembers():
        if not m.isfile() or not m.name.startswith("package/dist/"):
            continue
        rel = m.name[len("package/dist/"):]
        if rel.endswith(".map") or rel.endswith(".hex"):
            continue
        if rel.startswith("static/assets/"):
            mm = re.match(r"^(.+?)\.([A-Za-z_]+)\.([0-9a-f]{20})\.(\w+)$", os.path.basename(rel))
            if mm and mm.group(2) not in KEEP_LANG:
                continue
        data = gui.extractfile(m).read()
        if rel.startswith("libraries/"):
            libs[rel] = json.loads(data)
            continue
        if rel == "scratch-gui.js":
            data = patch_gui(data.decode("utf-8"), character).encode("utf-8")
        out = os.path.join(dest, rel.replace("/", os.sep))
        os.makedirs(os.path.dirname(out), exist_ok=True)
        open(out, "wb").write(data)
    for m in gui.getmembers():
        if m.name in ("package/LICENSE", "package/TRADEMARK"):
            open(os.path.join(dest, "SCRATCH-" + os.path.basename(m.name) + ".txt"), "wb").write(gui.extractfile(m).read())
    for name, file in (("react", "umd/react.production.min.js"), ("react-dom", "umd/react-dom.production.min.js")):
        t = pkg(name)
        open(os.path.join(dest, os.path.basename(file)), "wb").write(t.extractfile("package/" + file).read())
        open(os.path.join(dest, name.upper() + "-LICENSE.txt"), "wb").write(t.extractfile("package/LICENSE").read())

    if with_assets:
        want = set()
        marks = mark_md5(libs.get("libraries/sprites.json", []))
        for key in ("costumes", "backdrops", "sounds"):
            for it in libs.get(f"libraries/{key}.json", []):
                if it["md5ext"] not in marks:
                    want.add(it["md5ext"])
        for sp in libs.get("libraries/sprites.json", []):
            if MARKS.match(sp["name"]):
                continue
            for c in sp.get("costumes", []) + sp.get("sounds", []):
                want.add(c["md5ext"])
        os.makedirs(os.path.join(dest, "assets"))

        def one(md5ext):
            data = get(CDN.format(md5ext))
            if hashlib.md5(data).hexdigest() != md5ext.split(".")[0]:
                raise RuntimeError("ملف وسائط تالف: " + md5ext)
            open(os.path.join(dest, "assets", md5ext), "wb").write(data)
            return len(data)

        with concurrent.futures.ThreadPoolExecutor(12) as ex:
            total = sum(ex.map(one, sorted(want)))
        print(f"  الوسائط: {len(want)} ملف ({total // 2**20} ميجا)")
    copy_host(dest)
    open(sf, "w").write(stamp)
    size = sum(os.path.getsize(os.path.join(r, f)) for r, _, fs in os.walk(dest) for f in fs)
    print(f"✓ البرمجة بالكتل اتجهزت في {dest} ({size // 2**20} MB)")


def copy_host(dest):
    """صفحة الاستضافة بتاعة «مسار» (بتتنسخ كل مرة عشان تعديلاتها توصل من غير تنزيل تاني)"""
    for f in ("index.html", "host.js", "character-1.svg", "character-2.svg", "icon-sprite.svg"):
        shutil.copy(os.path.join(HERE, f), os.path.join(dest, f))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    main()
