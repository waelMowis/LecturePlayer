; مثبّت «مسار» (Inno Setup 6)
; بيتبني تلقائيًا من GitHub Actions، أو بإيدك:
;   dotnet publish src/LecturePlayer -c Release -r win-x64 --self-contained true -o publish
;   ISCC installer\LecturePlayer.iss /DSourceDir=..\publish /DAppVersion=4.0.0
;
; ملحوظة: البرنامج بيتثبت في فولدر المستخدم (مش Program Files) عشان يقدر يكتب
; فولدر Data جنبه ويفضل «محمول».

#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#ifndef AppVersion
  #define AppVersion "4.0.0"
#endif

[Setup]
AppId={{B3E8E8A2-6E3B-4C5B-9C7E-2F0A9B1D4C11}
AppName=مسار
AppVersion={#AppVersion}
AppVerName=مسار {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher=د. وائل عويس (Dr. Wael Owais)
AppCopyright=© 2026 د. وائل عويس
VersionInfoCompany=د. وائل عويس
VersionInfoDescription=مثبّت مسار
DefaultDirName={localappdata}\Programs\LecturePlayer
DefaultGroupName=مسار
; الاسم اتغيّر من «مشغل المحاضرات»: مانرجعش لفولدر قايمة Start القديم
UsePreviousGroup=no
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=Masar-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
ChangesAssociations=yes
SetupIconFile=..\src\LecturePlayer\LecturePlayer.ico
UninstallDisplayIcon={app}\LecturePlayer.exe
; التحديث: نفس الـ AppId ← بيتثبت فوق القديم في نفس الفولدر، وفولدر Data (بياناتك) مش بيتلمس.
; لو البرنامج مفتوح المثبّت بيطلب يقفله الأول.
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assoc"; Description: "فتح ملفات القوائم (.lpl) بالبرنامج"; GroupDescription: "ملفات"

[Files]
; Excludes: لو حد شغّل البرنامج من فولدر files قبل التغليف، فولدر Data (مكتبتك وإعداداتك) مايدخلش المثبّت أبدًا
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "\Data\*,\Data"; Flags: ignoreversion recursesubdirs createallsubdirs

; مكتبات VLC لازم تكون موجودة، وإلا المثبّت يفشل وقت البناء بدل ما يطلع برنامج مابيفتحش
#if !FileExists(AddBackslash(SourceDir) + "libvlc\win-x64\libvlc.dll")
  #error مكتبات VLC (libvlc\win-x64) مش موجودة في ملفات النشر
#endif

[InstallDelete]
; اختصارات الاسم القديم «مشغل المحاضرات» (البيانات والإعدادات مش بتتلمس)
Type: files; Name: "{autodesktop}\مشغل المحاضرات.lnk"
Type: files; Name: "{autoprograms}\مشغل المحاضرات\مشغل المحاضرات.lnk"
Type: dirifempty; Name: "{autoprograms}\مشغل المحاضرات"

[Icons]
Name: "{group}\مسار"; Filename: "{app}\LecturePlayer.exe"
Name: "{autodesktop}\مسار"; Filename: "{app}\LecturePlayer.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\.lpl"; ValueType: string; ValueName: ""; ValueData: "LecturePlayer.Library"; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\LecturePlayer.Library"; ValueType: string; ValueName: ""; ValueData: "قائمة محاضرات"; Flags: uninsdeletekey; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\LecturePlayer.Library\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\LecturePlayer.exe"" ""%1"""; Tasks: assoc

; «فتح بواسطة» لملفات الفيديو والصوت: البرنامج بيظهر في القايمة، من غير ما ياخد البرنامج الافتراضي بالعافية
; (لو عايزه الافتراضي: فتح بواسطة ← اختار برنامج تاني ← مسار ← «استخدم دايمًا»)
Root: HKCU; Subkey: "Software\Classes\LecturePlayer.Video"; ValueType: string; ValueName: ""; ValueData: "ملف فيديو"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\LecturePlayer.Video"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "ملف فيديو"
Root: HKCU; Subkey: "Software\Classes\LecturePlayer.Video\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\LecturePlayer.exe,0"
Root: HKCU; Subkey: "Software\Classes\LecturePlayer.Video\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\LecturePlayer.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "مسار"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\LecturePlayer.exe,0"
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\LecturePlayer.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mp4"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mkv"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".avi"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mov"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".wmv"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".flv"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".webm"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".m4v"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mpg"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mpeg"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".ts"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".m2ts"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mts"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".3gp"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".ogv"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".vob"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".mp3"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".m4a"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".aac"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".wav"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".flac"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".ogg"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".opus"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\LecturePlayer.exe\SupportedTypes"; ValueType: string; ValueName: ".wma"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\.mp4\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.mkv\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.avi\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.mov\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.wmv\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.flv\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.webm\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.m4v\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.mpg\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.mpeg\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.ts\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.m2ts\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.mts\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.3gp\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.ogv\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.vob\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.opus\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.wma\OpenWithProgids"; ValueType: string; ValueName: "LecturePlayer.Video"; ValueData: ""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\LecturePlayer.exe"; Description: "{cm:LaunchProgram,مسار}"; Flags: nowait postinstall skipifsilent
; التحديث التلقائي (البرنامج بيشغّل المثبّت بـ /SILENT /UPDATE=1): نفتح البرنامج تاني بعد ما يخلص
Filename: "{app}\LecturePlayer.exe"; Flags: nowait; Check: IsAutoUpdate

[Code]
function IsAutoUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

[UninstallDelete]
; فولدر Data (القوائم والإعدادات) مش بيتمسح، عشان بياناتك تفضل لو رجعت ثبّتت
