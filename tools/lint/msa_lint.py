# فحص لغة الواجهة: نصوص البرنامج بالعربية الفصحى المبسّطة.
# يرفض كلمات عامية شائعة داخل نصوص C# (عدا ملفات الفحص) ونصوص القارئ ودليل الاستخدام.
# الاستثناءات: أسماء محفوظة في ملفات المستخدم أو في نوشن (لا تتغيّر حتى لا تنكسر المزامنة والمكتبات القديمة).
import re, sys, glob, os
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = os.path.join(os.path.dirname(__file__), '..', '..', 'src', 'LecturePlayer')
AR = re.compile(r'[؀-ۿ]')
WORDS = ['مش', 'مفيش', 'عايز', 'عاوز', 'دلوقتي', 'بتاع', 'بتاعة', 'بتاعك', 'إزاي', 'ازاي', 'كده', 'كدا',
         'النهارده', 'لسه', 'اللي', 'ده', 'دي', 'دول', 'إيه', 'ايه', 'فين', 'ليه', 'زي', 'عشان', 'علشان',
         'جوه', 'برّه', 'بره', 'تاني', 'تانية', 'التاني', 'التانية', 'أكتر', 'اكتر', 'كويس', 'شوية', 'حاجة',
         'حاجات', 'لوحده', 'لوحدها', 'لوحدهم', 'مقدرتش', 'مقدرش', 'ماينفعش', 'مينفعش', 'هنا بس', 'بس',
         'دوس', 'اضغط هنا بس', 'كليك', 'فولدر', 'الفولدر', 'كورس', 'الكورس', 'كورسات', 'الكورسات', 'الماوس',
         'هات', 'خلّص', 'ماشي', 'أيوه', 'لأ', 'بقى', 'يبقى إيه']
WORDS = [w for w in WORDS if ' ' not in w]
EG = re.compile(r'(?<![؀-ۿ])(' + '|'.join(sorted(map(re.escape, WORDS), key=len, reverse=True)) + r')(?![؀-ۿ])')
# نصوص محفوظة (أسماء في ملفات المستخدم أو أعمدة/قيم نوشن): مسموح بها كما هي
ALLOW_EXACT = {
    'الكورس', 'الكورسات', 'ملفات الكورسات', 'ملف من الكورس', 'خلص', 'اللي', 'ده', 'دي', 'كده',
    'list (هيتحدّث yt-dlp ويتجرّب تاني)', 'online-resolve (هيتحدّث yt-dlp ويتجرّب تاني)',
}
SKIP_FILES = re.compile(r'(Checks|SmokeTest|NotionFake|TestHttpServer|NotionSync|Transcript|IconGlyphs|LiveSources)[^/\\]*\.cs$')

def cs_literals(src):
    i, n = 0, len(src)
    while i < n:
        if src.startswith('//', i):
            j = src.find('\n', i); i = n if j < 0 else j; continue
        if src.startswith('/*', i):
            j = src.find('*/', i + 2); i = n if j < 0 else j + 2; continue
        c = src[i]
        if c == "'":
            j = i + 1
            while j < n and src[j] != "'": j += 2 if src[j] == '\\' else 1
            i = j + 1; continue
        m = re.match(r'(\$+@?|@\$+|@)?"', src[i:i + 4])
        if m and (i == 0 or not (src[i - 1].isalnum() or src[i - 1] == '_')):
            pre = m.group(1) or ''
            j = i + len(pre) + 1
            if src.startswith('"""', i + len(pre)):
                k = src.find('"""', j + 2); yield src[j + 2:k]; i = k + 3; continue
            verb, interp, depth = '@' in pre, '$' in pre, 0
            while j < n:
                ch = src[j]
                if interp and ch == '{':
                    if src.startswith('{{', j): j += 2; continue
                    depth += 1; j += 1; continue
                if interp and ch == '}' and depth: depth -= 1; j += 1; continue
                if depth:
                    if ch == '"':
                        k = j + 1
                        while k < n and src[k] != '"': k += 2 if src[k] == '\\' else 1
                        j = k + 1; continue
                    j += 1; continue
                if verb:
                    if ch == '"':
                        if src.startswith('""', j): j += 2; continue
                        break
                    j += 1; continue
                if ch == '\\': j += 2; continue
                if ch == '"' or ch == '\n': break
                j += 1
            yield src[i + len(pre) + 1:j]; i = j + 1; continue
        i += 1

def js_literals(s):
    for m in re.finditer(r"'(?:\\.|[^'\\\n])*'|\"(?:\\.|[^\"\\\n])*\"|`(?:\\.|[^`\\])*`", re.sub(r'(?m)^\s*//.*$', '', s)):
        yield m.group(0)[1:-1]

def html_text(s):
    s = re.sub(r'<script.*?</script>|<style.*?</style>|<!--.*?-->', '', s, flags=re.S)
    for m in re.finditer(r'>([^<>]+)<|(?:title|placeholder|aria-label|alt)="([^"]*)"', s):
        yield m.group(1) or m.group(2) or ''

def bad(text):
    if text.strip() in ALLOW_EXACT or not AR.search(text): return []
    hits = [m.group(0) for m in EG.finditer(text)]
    if text.lstrip().startswith(('#', '//')) or 'masar' in text and 'document.' in text: return []
    return hits

problems = []
for f in glob.glob(os.path.join(ROOT, '**', '*.cs'), recursive=True):
    if SKIP_FILES.search(f): continue
    src = open(f, encoding='utf-8').read()
    for t in cs_literals(src):
        if len(t) > 400: continue  # سكربتات JS داخل C# (تعليقات المطورين)
        for w in bad(t): problems.append((os.path.relpath(f, ROOT), w, t))
for f in glob.glob(os.path.join(ROOT, 'Reader', '*.js')):
    for t in js_literals(open(f, encoding='utf-8').read()):
        for w in bad(t): problems.append((os.path.relpath(f, ROOT), w, t))
for f in glob.glob(os.path.join(ROOT, 'Reader', '*.html')) + glob.glob(os.path.join(ROOT, 'Help', '*.html')):
    for t in html_text(open(f, encoding='utf-8').read()):
        if 'Help' in f: t = re.sub(r'«[^»]*»', '', t)  # أسماء محفوظة بين «» (مثل رف «ملفات الكورسات» وأعمدة نوشن)
        for w in bad(t): problems.append((os.path.relpath(f, ROOT), w, t))

for f, w, t in problems:
    print(f'{f}: «{w}» في: {t[:120]!r}')
print(f'MSA lint: {len(problems)} problem(s)')
sys.exit(1 if problems else 0)
