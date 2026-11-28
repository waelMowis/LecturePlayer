# أدوات أردوينو لمحاكي «مسار» (بتشتغل من غير نت بعد التثبيت):
#   arduino-cli (المترجم) + نواة arduino:avr (avr-gcc لأونو) + مكتبتين Servo و LiquidCrystal
#
# بتتنزل من المصادر الرسمية بس (GitHub arduino/arduino-cli + فهرس Arduino الرسمي)، بإصدارات مثبّتة:
#   - arduino-cli: بنقارن الـ sha256 بالقيمة المثبّتة هنا + بملف checksums الرسمي للإصدار.
#   - النواة والمكتبات: arduino-cli نفسه بيتأكد من sha256 كل أرشيف من الفهرس الرسمي.
# الرخص: arduino-cli و avr-gcc (GPL) برامج منفصلة بنشغّلها زي ما هي (مش مدموجة في مسار)،
#        ونواة أردوينو (LGPL) بتتبني جوه برنامج الطالب للمحاكاة بس. التفاصيل في THIRD-PARTY-NOTICES.
#
# الاستخدام:  pwsh tools/arduino/fetch-arduino.ps1 -Dest <فولدر>
# لو الفولدر جاهز بنفس الإصدارات (ملف .masar-stamp) مابنزّلش حاجة تاني.
param(
    [Parameter(Mandatory = $true)][string]$Dest,
    [string]$SampleOut = ''   # (للمطورين) نسخة من ملف hex لتجربة الترجمة
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$CliVersion = '1.6.0'
$CliSha256 = '2769B9B282CE597DB9679D8EC14D726D57004CA4205EF4D7A795BF36B6CDC9D8'
$Core = 'arduino:avr@1.8.6'
$Libs = @('Servo@1.2.2', 'LiquidCrystal@1.0.7')
$Stamp = "cli=$CliVersion;core=$Core;libs=$($Libs -join ',');v=1"

$Dest = [System.IO.Path]::GetFullPath($Dest)
$stampFile = Join-Path $Dest '.masar-stamp'
# (للمطورين) ترجمة أمثلة tools/arduino/samples/*.ino ← ملفات hex لتجربة المحاكي
function Compile-Samples {
    if (-not $SampleOut) { return }
    New-Item -ItemType Directory -Force -Path $SampleOut | Out-Null
    $cli = Join-Path $Dest 'arduino-cli.exe'
    $env:ARDUINO_DIRECTORIES_DATA = Join-Path $Dest 'data'
    $env:ARDUINO_DIRECTORIES_USER = Join-Path $Dest 'user'
    foreach ($f in Get-ChildItem (Join-Path $PSScriptRoot 'samples') -Filter '*.ino' -ErrorAction SilentlyContinue) {
        $sd = Join-Path ([System.IO.Path]::GetTempPath()) ("masar-sample-" + [guid]::NewGuid().ToString('N') + "\" + $f.BaseName)
        New-Item -ItemType Directory -Force -Path $sd | Out-Null
        Copy-Item $f.FullName (Join-Path $sd $f.Name)
        & $cli --config-file (Join-Path $Dest 'arduino-cli.yaml') compile --fqbn arduino:avr:uno --output-dir $SampleOut $sd
        if ($LASTEXITCODE -ne 0) { throw "ترجمة المثال $($f.Name) فشلت" }
    }
}

if ((Test-Path (Join-Path $Dest 'arduino-cli.exe')) -and (Test-Path $stampFile) -and ((Get-Content $stampFile -Raw).Trim() -eq $Stamp)) {
    Write-Host "✓ أدوات أردوينو جاهزة ($Stamp)"
    Compile-Samples
    exit 0
}

if (Test-Path $Dest) { Remove-Item $Dest -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Dest | Out-Null
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("masar-arduino-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

try {
    # 1) arduino-cli
    $base = "https://github.com/arduino/arduino-cli/releases/download/v$CliVersion"
    $zipName = "arduino-cli_${CliVersion}_Windows_64bit.zip"
    $zip = Join-Path $tmp $zipName
    $sums = Join-Path $tmp 'checksums.txt'
    Invoke-WebRequest -Uri "$base/$zipName" -OutFile $zip -UseBasicParsing
    # اسم ملف checksums في إصدارات arduino-cli: «<الإصدار>-checksums.txt»
    $gotSums = $false
    foreach ($n in @("$CliVersion-checksums.txt", "arduino-cli_${CliVersion}_checksums.txt")) {
        try { Invoke-WebRequest -Uri "$base/$n" -OutFile $sums -UseBasicParsing; $gotSums = $true; break } catch { }
    }
    if (-not $gotSums) { throw "ملف checksums الرسمي لـ arduino-cli $CliVersion مش موجود" }
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToUpperInvariant()
    $official = (Get-Content $sums | Where-Object { $_ -match [regex]::Escape($zipName) + '\s*$' } | ForEach-Object { ($_ -split '\s+')[0] } | Select-Object -First 1)
    if (-not $official) { throw "ملف checksums الرسمي مافيهوش $zipName" }
    if ($actual -ne $official.ToUpperInvariant()) { throw "sha256 لـ $zipName مختلف عن ملف checksums الرسمي: $actual" }
    if ($actual -ne $CliSha256) { throw "sha256 لـ $zipName ($actual) مختلف عن القيمة المثبّتة في السكريبت ($CliSha256)" }
    Expand-Archive -Path $zip -DestinationPath $Dest -Force
    $cli = Join-Path $Dest 'arduino-cli.exe'
    if (-not (Test-Path $cli)) { throw 'arduino-cli.exe مش موجود بعد فك الضغط' }

    # إعدادات خاصة بمسار (مابنقراش إعدادات Arduino IDE لو متثبت عند المستخدم)
    $cfg = Join-Path $Dest 'arduino-cli.yaml'
    $cfgText = @"
updater:
  enable_notification: false
network:
  connection_timeout: 5s
metrics:
  enabled: false
"@
    [System.IO.File]::WriteAllText($cfg, $cfgText)

    $env:ARDUINO_DIRECTORIES_DATA = Join-Path $Dest 'data'
    $env:ARDUINO_DIRECTORIES_USER = Join-Path $Dest 'user'
    $env:ARDUINO_DIRECTORIES_DOWNLOADS = Join-Path $tmp 'downloads'
    $env:ARDUINO_BUILD_CACHE_PATH = Join-Path $tmp 'cache'
    $run = {
        param([string[]]$a)
        & $cli --config-file $cfg @a
        if ($LASTEXITCODE -ne 0) { throw "arduino-cli $($a -join ' ') فشل (exit $LASTEXITCODE)" }
    }

    # 2) النواة والمكتبات
    & $run @('core', 'update-index')
    & $run @('core', 'install', $Core)
    & $run @('lib', 'update-index')
    & $run (@('lib', 'install') + $Libs)

    # فهرس المكتبات كبير ومش لازم للترجمة
    Get-ChildItem $env:ARDUINO_DIRECTORIES_DATA -Filter 'library_index*' -ErrorAction SilentlyContinue | Remove-Item -Force
    Remove-Item (Join-Path $env:ARDUINO_DIRECTORIES_DATA 'staging') -Recurse -Force -ErrorAction SilentlyContinue

    # 3) تجربة ترجمة من غير نت (Blink + Servo + LiquidCrystal) عشان نتأكد إن الأدوات سليمة
    $sk = Join-Path $tmp 'check'
    New-Item -ItemType Directory -Force -Path $sk | Out-Null
    $ino = @'
#include <Servo.h>
#include <LiquidCrystal.h>
Servo s; LiquidCrystal lcd(12, 11, 5, 4, 3, 2);
void setup() { pinMode(13, OUTPUT); s.attach(9); lcd.begin(16, 2); Serial.begin(9600); }
void loop() { digitalWrite(13, !digitalRead(13)); Serial.println(millis()); delay(500); }
'@
    [System.IO.File]::WriteAllText((Join-Path $sk 'check.ino'), $ino)
    $env:HTTPS_PROXY = 'http://127.0.0.1:9'   # أي محاولة اتصال بالنت تفشل ← الترجمة لازم تنجح من غيره
    $env:HTTP_PROXY = 'http://127.0.0.1:9'
    & $run @('compile', '--fqbn', 'arduino:avr:uno', '--output-dir', (Join-Path $tmp 'out'), $sk)
    Remove-Item Env:HTTPS_PROXY, Env:HTTP_PROXY
    if (-not (Test-Path (Join-Path $tmp 'out\check.ino.hex'))) { throw 'تجربة الترجمة ماطلعتش ملف hex' }

    Set-Content -Path $stampFile -Value $Stamp -Encoding ascii
    Compile-Samples
    $mb = [math]::Round(((Get-ChildItem $Dest -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB)
    Write-Host "✓ أدوات أردوينو اتجهزت في $Dest ($mb MB) — $Stamp"
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
