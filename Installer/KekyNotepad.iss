; Keky Notepad installer. Build: dotnet publish -p:PublishProfile=Portable, then compile this with ISCC.
#define AppName "Keky Notepad"
#define AppExe "KekyNotepad.exe"
#define AppVersion "1.0.0"
#define AppUrl ""
#ifndef AppGuid
#define AppGuid "6B1F3C52-7E0A-4B6D-9A41-4B454B594E50}"
#endif
#ifndef OutName
#define OutName "KekyNotepad-Setup"
#endif
#define Source "..\bin\Portable"

[Setup]
AppId={{{#AppGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Keky
#if AppUrl != ""
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
#endif
UninstallDisplayName={cm:AppDisplay}
DefaultDirName={autopf}\KekyNotepad
DefaultGroupName={cm:AppDisplay}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\Assets\app.ico
WizardImageFile=wizard-large-100.bmp,wizard-large-150.bmp,wizard-large-200.bmp
WizardSmallImageFile=wizard-small-100.bmp,wizard-small-150.bmp,wizard-small-200.bmp
OutputDir=..\bin\Setup
OutputBaseFilename={#OutName}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ChangesEnvironment=yes
WizardStyle=modern
DisableWelcomePage=no
LicenseFile=..\Tools\licenses\scrcpy-LICENSE.txt
VersionInfoVersion={#AppVersion}
VersionInfoCompany=Keky
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[CustomMessages]
english.AppDisplay=Keky Notepad
english.AddPathTask=Add scrcpy / adb and Keky Notepad to the PATH (use them from any terminal)
english.EnvGroup=Environment:
english.Website=Project page on GitHub
english.ConfirmTitle=Remove Keky Notepad?
english.ConfirmText=Keky Notepad and its components will be removed from this computer.
english.BtnRemove=Remove
english.BtnCancel=Cancel
english.DataTitle=Delete your settings too?
english.DataText=Settings and unsaved tabs are stored in:%n%1%n%nKeep them if you plan to reinstall.
english.BtnDelete=Delete
english.BtnKeep=Keep
arabic.AppDisplay=كي كي
arabic.AddPathTask=إضافة scrcpy وadb وبرنامج كي كي إلى متغير PATH (لاستخدامها من أي نافذة أوامر)
arabic.EnvGroup=متغيرات البيئة:
arabic.Website=صفحة المشروع على GitHub
arabic.ConfirmTitle=هل تريد إزالة كي كي؟
arabic.ConfirmText=ستُزال كي كي وجميع مكوناتها من هذا الحاسوب.
arabic.BtnRemove=إزالة
arabic.BtnCancel=إلغاء
arabic.DataTitle=هل تريد حذف إعداداتك أيضًا؟
arabic.DataText=الإعدادات والتبويبات غير المحفوظة مخزنة في:%n%1%n%nاحتفظ بها إن كنت ستعيد التثبيت.
arabic.BtnDelete=حذف
arabic.BtnKeep=إبقاء

[Messages]
arabic.SetupWindowTitle=معالج تثبيت - كي كي
arabic.WelcomeLabel1=مرحبًا بك في معالج تثبيت كي كي
arabic.WelcomeLabel2=سيقوم هذا المعالج بتثبيت كي كي على حاسوبك.%n%nيُنصح بإغلاق بقية التطبيقات قبل المتابعة.
arabic.SelectDirDesc=أين تريد تثبيت كي كي؟
arabic.SelectDirLabel3=سيُثبَّت كي كي في المجلد التالي.
arabic.ReadyLabel1=أصبح المعالج جاهزًا لبدء تثبيت كي كي على حاسوبك.
arabic.InstallingLabel=يرجى الانتظار أثناء تثبيت كي كي على حاسوبك.
arabic.FinishedHeadingLabel=اكتمل تثبيت كي كي
arabic.FinishedLabel=تم تثبيت كي كي على حاسوبك. يمكنك تشغيله من الاختصارات المثبتة.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "addpath"; Description: "{cm:AddPathTask}"; GroupDescription: "{cm:EnvGroup}"

[Files]
Source: "{#Source}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Source}\scrcpy\*"; DestDir: "{app}\scrcpy"; Flags: ignoreversion recursesubdirs
Source: "..\Tools\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\{cm:AppDisplay}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{cm:AppDisplay}}"; Filename: "{uninstallexe}"; Parameters: "/SILENT /ASK=1"
#if AppUrl != ""
Name: "{group}\{cm:Website}"; Filename: "{#AppUrl}"
#endif
Name: "{autodesktop}\{cm:AppDisplay}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; User PATH (or system PATH when installed for all users). Removed again on uninstall.
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\scrcpy;{app}"; Check: NeedsPath('{app}\scrcpy') and not IsAdminInstallMode; Tasks: addpath
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\scrcpy;{app}"; Check: NeedsPath('{app}\scrcpy') and IsAdminInstallMode; Tasks: addpath

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{cm:AppDisplay}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop everything that keeps files locked, so nothing is left behind.
Filename: "{app}\scrcpy\adb.exe"; Parameters: "kill-server"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillAdb"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM scrcpy.exe"; Flags: runhidden; RunOnceId: "KillScrcpy"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function PathKey(): string;
begin
  if IsAdminInstallMode then
    Result := 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
  else
    Result := 'Environment';
end;

function PathRoot(): Integer;
begin
  if IsAdminInstallMode then
    Result := HKEY_LOCAL_MACHINE
  else
    Result := HKEY_CURRENT_USER;
end;

function NeedsPath(Param: string): Boolean;
var
  Existing: string;
begin
  if not RegQueryStringValue(PathRoot(), PathKey(), 'Path', Existing) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Lowercase(ExpandConstant(Param)) + ';', ';' + Lowercase(Existing) + ';') = 0;
end;

procedure RemoveFromPath(Entry: string);
var
  Existing, Lower, Needle: string;
  P: Integer;
begin
  if not RegQueryStringValue(PathRoot(), PathKey(), 'Path', Existing) then
    exit;
  Needle := ';' + Lowercase(Entry);
  Lower := ';' + Lowercase(Existing);
  P := Pos(Needle + ';', Lower + ';');
  if P > 0 then
  begin
    Existing := ';' + Existing;
    Delete(Existing, P, Length(Entry) + 1);
    if (Length(Existing) > 0) and (Existing[1] = ';') then
      Delete(Existing, 1, 1);
    RegWriteExpandStringValue(PathRoot(), PathKey(), 'Path', Existing);
  end;
end;

{ Windows' own message box shows Yes/No in the system language, so the uninstaller runs silently
  (no built-in prompt) and asks through a task dialog whose buttons use the installer language. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  Key: string;
begin
  if CurStep = ssPostInstall then
  begin
    Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1';
    Key := Copy(Key, 1, Pos('{{', Key) - 1) + Copy(Key, Pos('{{', Key) + 1, MaxInt);
    RegWriteStringValue(PathRoot(), Key, 'UninstallString', '"' + ExpandConstant('{uninstallexe}') + '" /SILENT /ASK=1');
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if ExpandConstant('{param:ASK|0}') = '1' then
  begin
    Result := TaskDialogMsgBox(
      CustomMessage('ConfirmTitle'), CustomMessage('ConfirmText'),
      mbConfirmation, MB_YESNO, [CustomMessage('BtnRemove'), CustomMessage('BtnCancel')], 0) = IDYES;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveFromPath(ExpandConstant('{app}\scrcpy'));
    RemoveFromPath(ExpandConstant('{app}'));

    Data := ExpandConstant('{userappdata}\KekyNotepad');
    if DirExists(Data) and (ExpandConstant('{param:ASK|0}') = '1') then
    begin
      if TaskDialogMsgBox(
        CustomMessage('DataTitle'), FmtMessage(CustomMessage('DataText'), [Data]),
        mbConfirmation, MB_YESNO, [CustomMessage('BtnDelete'), CustomMessage('BtnKeep')], 0) = IDYES then
        DelTree(Data, True, True, True);
    end;
  end;
end;
