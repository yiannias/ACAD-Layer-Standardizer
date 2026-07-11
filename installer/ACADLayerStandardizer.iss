; ACAD Layer Standardizer - Inno Setup Script
; Build with: ISCC.exe ACADLayerStandardizer.iss

#define MyAppName "ACAD Layer Standardizer"
#define MyAppPublisher "CGY"
#define MyAppURL "https://github.com/yiannias/ACAD-Layer-Standardizer"
#define MyAppVersion GetEnv('MYAPPVERSION')
; Target framework moniker of the build being packaged (e.g. net10.0-windows
; for AutoCAD 2027, net8.0-windows for 2026/2025) -- must match whichever
; -AcadVersion build.ps1 actually built, since AutoCAD 2027's .NET 10 host is
; not binary-compatible with 2026/2025's .NET 8 host. Only one TFM is
; packaged at a time today; see PackageContents.xml for how to add a second
; ComponentEntry if both need to ship in one installer.
#define MyAppTfm GetEnv('MYAPPTFM')
; schemaVersion of the bundled assets\layer_dictionary.json, read by
; build.ps1 (PowerShell has a real JSON parser; Pascal Script doesn't) --
; baked in at compile time so ShouldInstallDictionary below can compare it
; against an installed copy's schemaVersion without reading the (nonexistent
; on the end user's machine) original source path at runtime.
#define MyDictSchemaVersion GetEnv('MYDICTSCHEMAVERSION')

[Setup]
AppId={{E5B07CB1-B0A8-40B7-B225-3DAF58FCAF57}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
; Installs to the machine-wide Program Files ApplicationPlugins folder, not
; the per-user AppData one -- as of AutoCAD 2026, only the Program Files
; location is trusted by default for autoloading (SECURELOAD). Installing to
; AppData silently fails to autoload with no error shown. Requires admin
; elevation as a result; see memory.md for the investigation.
DefaultDirName={commonpf64}\Autodesk\ApplicationPlugins\AcLayerStandardizer.bundle
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=AcLayerStandardizer_{#StringChange(MyAppVersion, "/", "-")}
SetupIconFile=assets\LayerStandardizer.ico
WizardSmallImageFile=assets\LayerStandardizer_header.png
WizardImageFile=assets\LayerStandardizer_sidebar.png
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Contents\Windows\64-bit\AcLayerStandardizer.dll
WizardStyle=modern dark

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\src\AcLayerStandardizer\bin\Release\{#MyAppTfm}\AcLayerStandardizer.dll"; DestDir: "{app}\Contents\Windows\64-bit"; Flags: ignoreversion
Source: "..\src\AcLayerStandardizer\bin\Release\{#MyAppTfm}\Nodify.dll"; DestDir: "{app}\Contents\Windows\64-bit"; Flags: ignoreversion
Source: "..\dist\PackageContents.xml"; DestDir: "{app}"; Flags: ignoreversion
; config.json: plain app settings (paths, thresholds, checkbox state), not a
; versioned content schema -- PluginConfig.Load() already tolerates missing
; fields via C# property defaults on deserialize, so there's nothing here
; that a silent "keep the user's file" ever loses. onlyifdoesntexist stays.
Source: "assets\config.json"; DestDir: "{userappdata}\AcLayerStandardizer"; Flags: ignoreversion onlyifdoesntexist
; layer_dictionary.json IS a user-editable, user-tunable content schema (see
; the file's own "description" field) -- onlyifdoesntexist used to mean an
; installed copy was NEVER updated, which is exactly how the 2026-07-11
; sortGroup tier fields got stranded on an already-installed machine (the
; new schema shipped in the repo, but the on-disk file just never changed).
; ShouldInstallDictionary (see [Code] below) replaces the static flag with a
; schemaVersion-gated prompt: fresh installs get it silently, upgrades only
; get asked (and only overwritten, after a .bak backup) when the bundled
; copy is actually newer than what's on disk.
Source: "assets\layer_dictionary.json"; DestDir: "{userappdata}\AcLayerStandardizer"; Flags: ignoreversion; Check: ShouldInstallDictionary
Source: "assets\LayerStandardizer.cuix"; DestDir: "{userappdata}\AcLayerStandardizer"; Flags: ignoreversion

[Dirs]
Name: "{userappdata}\AcLayerStandardizer"; Flags: uninsalwaysuninstall

[Icons]
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKLM; Subkey: "Software\AcLayerStandardizer"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\AcLayerStandardizer"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

[Code]
// PrivilegesRequired=admin means every launch goes through UAC elevation,
// and Windows frequently leaves an elevated process's window behind
// whatever already had focus instead of bringing it forward -- a known
// Windows quirk, not something Inno handles on its own. SetForegroundWindow
// alone is unreliable here: Windows' anti-focus-stealing rules can silently
// ignore it, especially right after a UAC elevation. Toggling the window
// TOPMOST via SetWindowPos isn't subject to that restriction, so do both.
function SetForegroundWindow(hWnd: LongInt): LongInt;
  external 'SetForegroundWindow@user32.dll stdcall';
function SetWindowPos(hWnd, hWndInsertAfter, X, Y, cx, cy, uFlags: LongInt): LongInt;
  external 'SetWindowPos@user32.dll stdcall';

// HWND_TOPMOST=-1, HWND_NOTOPMOST=-2, SWP_NOMOVE=$0002, SWP_NOSIZE=$0001
// -- inlined as literals rather than named consts (Inno's Pascal Script
// parser rejected a multi-entry const block here).
procedure ForceWindowToFront(H: LongInt);
begin
  SetWindowPos(H, -1, 0, 0, 0, 0, $0002 or $0001);
  SetWindowPos(H, -2, 0, 0, 0, 0, $0002 or $0001);
  SetForegroundWindow(H);
end;

// Extracts an integer JSON field's value via plain text search -- Pascal
// Script has no JSON parser, and this only ever needs one scalar field
// (schemaVersion) out of a file whose format we control, so a full parser
// would be overkill. Looks for "FieldName" then the first run of digits
// after it (skips the colon/whitespace in between). Returns DefaultValue if
// the file can't be read or the field isn't found.
function GetJsonIntField(const FilePath, FieldName: String; DefaultValue: Integer): Integer;
var
  RawContent: AnsiString;
  Content, Needle, Digits: String;
  P: Integer;
begin
  Result := DefaultValue;
  // LoadStringFromFile's output param is specifically AnsiString (a type
  // mismatch against the default Unicode String otherwise); JSON content
  // here is plain ASCII, so the AnsiString->String conversion is lossless.
  if not LoadStringFromFile(FilePath, RawContent) then
    Exit;
  Content := String(RawContent);

  Needle := '"' + FieldName + '"';
  P := Pos(Needle, Content);
  if P = 0 then Exit;

  P := P + Length(Needle);
  while (P <= Length(Content)) and not ((Content[P] >= '0') and (Content[P] <= '9')) do
    P := P + 1;

  Digits := '';
  while (P <= Length(Content)) and (Content[P] >= '0') and (Content[P] <= '9') do
  begin
    Digits := Digits + Content[P];
    P := P + 1;
  end;

  if Digits <> '' then
    Result := StrToIntDef(Digits, DefaultValue);
end;

var
  DictCheckDone: Boolean;
  DictCheckResult: Boolean;

// [Files] Check function for layer_dictionary.json, replacing the old
// static onlyifdoesntexist flag (see the [Files] section comment for why).
// Cached behind DictCheckDone/DictCheckResult because Inno can invoke a
// Check function more than once per file (e.g. recomputing install size on
// wizard page changes) -- without caching, a user could see the prompt
// twice, or worse, get backed up/overwritten twice, for one install run.
function ShouldInstallDictionary(): Boolean;
var
  DestFile, BackupFile: String;
  InstalledVersion: Integer;
begin
  if DictCheckDone then
  begin
    Result := DictCheckResult;
    Exit;
  end;

  DestFile := ExpandConstant('{userappdata}\AcLayerStandardizer\layer_dictionary.json');

  if not FileExists(DestFile) then
    Result := True { fresh install, no prompt }
  else
  begin
    InstalledVersion := GetJsonIntField(DestFile, 'schemaVersion', 0);

    if {#MyDictSchemaVersion} <= InstalledVersion then
      Result := False { installed copy is already current (or newer/custom) -- leave it alone, no prompt }
    else if MsgBox('A newer version of the layer dictionary (used by the Target Filter panel) is available.' + #13#10#13#10 +
      'Overwrite your current layer_dictionary.json with the updated one? Your existing file will be backed up first as layer_dictionary.json.bak.' + #13#10#13#10 +
      'Choose No to keep your current file, including any customizations, as-is.',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      BackupFile := DestFile + '.bak';
      FileCopy(DestFile, BackupFile, False);
      Result := True;
    end
    else
      Result := False;
  end;

  DictCheckDone := True;
  DictCheckResult := Result;
end;

var
  InstallRibbon: Boolean;
  InstallMenu: Boolean;
  CustomizationPageID: Integer;
  InfoPageID: Integer;
  RibbonCheck: TNewCheckBox;
  MenuCheck: TNewCheckBox;
  ForceCleanReinstall: Boolean;

{ Shows a 4-button choice when an existing installation is detected.
  Returns mrYes (Update), mrNo (Reinstall), mrCancel (Uninstall), or
  mrAbort (cancel setup entirely / dialog closed). }
function ShowUpdateChoiceDialog(const CurrentVersion: String): Integer;
var
  Form: TSetupForm;
  Lbl: TNewStaticText;
  BtnUpdate, BtnReinstall, BtnUninstall, BtnCancel: TNewButton;
  ButtonTop, ButtonWidth, ButtonHeight, Gap: Integer;
begin
  { CreateCustomForm is Inno's own factory for script-created dialogs.
    TSetupForm.Create(nil) fails at runtime with "Resource TSetupForm not
    found" -- the raw Delphi constructor tries to stream in a designed
    form resource that script forms don't have. This Inno version's
    signature (read from ISCmplr.dll) is (ClientWidth, ClientHeight,
    KeepSizeX, KeepSizeY) -- KeepSize=True since the size is pre-scaled. }
  Form := CreateCustomForm(ScaleX(460), ScaleY(150), True, True);
  try
    Form.Caption := 'ACAD Layer Standardizer - Existing Installation Detected';
    Form.Position := poScreenCenter;
    Form.BorderStyle := bsDialog;

    Lbl := TNewStaticText.Create(Form);
    Lbl.Parent := Form;
    Lbl.Left := ScaleX(16);
    Lbl.Top := ScaleY(16);
    Lbl.Width := Form.ClientWidth - ScaleX(32);
    Lbl.WordWrap := True;
    Lbl.AutoSize := True;
    Lbl.Caption := 'An existing installation (version ' + CurrentVersion + ') was found.' + #13#10 +
      'What would you like to do?';

    ButtonWidth := ScaleX(96);
    ButtonHeight := ScaleY(23);
    Gap := ScaleX(8);
    ButtonTop := Form.ClientHeight - ButtonHeight - ScaleY(16);

    BtnUpdate := TNewButton.Create(Form);
    BtnUpdate.Parent := Form;
    BtnUpdate.Caption := '&Update';
    BtnUpdate.Width := ButtonWidth;
    BtnUpdate.Height := ButtonHeight;
    BtnUpdate.Left := ScaleX(16);
    BtnUpdate.Top := ButtonTop;
    BtnUpdate.ModalResult := mrYes;

    BtnReinstall := TNewButton.Create(Form);
    BtnReinstall.Parent := Form;
    BtnReinstall.Caption := '&Reinstall';
    BtnReinstall.Width := ButtonWidth;
    BtnReinstall.Height := ButtonHeight;
    BtnReinstall.Left := BtnUpdate.Left + ButtonWidth + Gap;
    BtnReinstall.Top := ButtonTop;
    BtnReinstall.ModalResult := mrNo;

    BtnUninstall := TNewButton.Create(Form);
    BtnUninstall.Parent := Form;
    BtnUninstall.Caption := '&Uninstall';
    BtnUninstall.Width := ButtonWidth;
    BtnUninstall.Height := ButtonHeight;
    BtnUninstall.Left := BtnReinstall.Left + ButtonWidth + Gap;
    BtnUninstall.Top := ButtonTop;
    BtnUninstall.ModalResult := mrCancel;

    BtnCancel := TNewButton.Create(Form);
    BtnCancel.Parent := Form;
    BtnCancel.Caption := 'Cancel';
    BtnCancel.Width := ButtonWidth;
    BtnCancel.Height := ButtonHeight;
    BtnCancel.Left := BtnUninstall.Left + ButtonWidth + Gap;
    BtnCancel.Top := ButtonTop;
    BtnCancel.ModalResult := mrAbort;
    BtnCancel.Cancel := True;

    Form.ActiveControl := BtnUpdate;
    ForceWindowToFront(Form.Handle);
    Result := Form.ShowModal;
  finally
    Form.Free;
  end;
end;

function InitializeSetup(): Boolean;
var
  ExistingVersion, InstallPath, UninstallExe: String;
  Choice, ResultCode: Integer;
begin
  Result := True;
  ForceCleanReinstall := False;

  if RegQueryStringValue(HKLM, 'Software\AcLayerStandardizer', 'Version', ExistingVersion) then
  begin
    Choice := ShowUpdateChoiceDialog(ExistingVersion);
    case Choice of
      mrYes:
        { Update: proceed with normal install, existing config/memory files
          are left alone (config.json is only scaffolded if missing). };
      mrNo:
        begin
          { Reinstall: same as Update, but force a clean copy of the plugin
            binaries first (see PrepareToInstall). User config/memory in the
            AppData config folder is untouched either way. }
          ForceCleanReinstall := True;
        end;
      mrCancel:
        begin
          { Uninstall: hand off to the existing uninstaller, then abort this
            setup run entirely. }
          if RegQueryStringValue(HKLM, 'Software\AcLayerStandardizer', 'InstallPath', InstallPath) then
          begin
            UninstallExe := InstallPath + '\unins000.exe';
            if FileExists(UninstallExe) then
              Exec(UninstallExe, '', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
          end;
          Result := False;
        end;
    else
      { Cancel button or dialog closed: abort setup without changing anything. }
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if ForceCleanReinstall then
    DelTree(ExpandConstant('{app}\Contents'), True, True, True);
end;

procedure GitHubLinkClick(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', 'https://github.com/yiannias/ACAD-Layer-Standardizer', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure EmailLinkClick(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', 'mailto:yiannias@gmail.com', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure InitializeWizard;
var
  InfoPage: TWizardPage;
  CustomizationPage: TWizardPage;
  InfoLbl: TNewStaticText;
  GitHubLink: TNewStaticText;
  EmailLink: TNewStaticText;
  AboutImg: TBitmapImage;
begin
  { Defensive defaults matching the checkboxes' initial Checked state, in
    case ssPostInstall is ever reached without visiting CustomizationPage. }
  InstallRibbon := True;
  InstallMenu := True;

  { The small header image (WizardSmallImageFile) lives inside the wizard's
    fixed-height top bar and always renders clipped/cramped there, so hide
    the control entirely -- the About page shows the icon large instead.
    The WizardSmallImageFile directive stays: its decoded bitmap is what
    the About page image reuses below. }
  WizardForm.WizardSmallBitmapImage.Visible := False;

  InfoPage := CreateCustomPage(wpWelcome,
    'About ACAD Layer Standardizer',
    'This application is free to use, open source, and MIT licensed.');
  InfoPageID := InfoPage.ID;

  InfoLbl := TNewStaticText.Create(InfoPage);
  InfoLbl.Parent := InfoPage.Surface;
  InfoLbl.Left := 0;
  InfoLbl.Top := 8;
  InfoLbl.Width := InfoPage.Surface.Width - ScaleX(110);
  InfoLbl.WordWrap := True;
  InfoLbl.AutoSize := True;
  InfoLbl.Font.Size := 10;
  InfoLbl.Caption := 'ACAD Layer Standardizer analyzes DWG layers against a template and maps them to a master standard layer set.';

  GitHubLink := TNewStaticText.Create(InfoPage);
  GitHubLink.Parent := InfoPage.Surface;
  GitHubLink.Left := 0;
  GitHubLink.Top := InfoLbl.Top + InfoLbl.Height + ScaleY(16);
  GitHubLink.Width := InfoLbl.Width;
  GitHubLink.Height := 20;
  GitHubLink.Caption := 'GitHub: https://github.com/yiannias/ACAD-Layer-Standardizer';
  GitHubLink.Font.Color := clBlue;
  GitHubLink.Font.Style := [fsUnderline];
  GitHubLink.Cursor := crHand;
  GitHubLink.OnClick := @GitHubLinkClick;

  EmailLink := TNewStaticText.Create(InfoPage);
  EmailLink.Parent := InfoPage.Surface;
  EmailLink.Left := 0;
  EmailLink.Top := GitHubLink.Top + GitHubLink.Height + ScaleY(4);
  EmailLink.Width := InfoLbl.Width;
  EmailLink.Height := 20;
  EmailLink.Caption := 'Contact: yiannias@gmail.com';
  EmailLink.Font.Color := clBlue;
  EmailLink.Font.Style := [fsUnderline];
  EmailLink.Cursor := crHand;
  EmailLink.OnClick := @EmailLinkClick;

  { Large app icon under the text, centered in the remaining page space.
    Reuses the bitmap the runtime already decoded from WizardSmallImageFile
    (PNG, alpha intact) rather than loading a file -- script-side
    LoadFromFile PNG support varies by Inno version, this doesn't. }
  AboutImg := TBitmapImage.Create(InfoPage);
  AboutImg.Parent := InfoPage.Surface;
  AboutImg.Bitmap := WizardForm.WizardSmallBitmapImage.Bitmap;
  AboutImg.Stretch := True;
  AboutImg.Width := ScaleX(170);
  AboutImg.Height := ScaleY(168);
  AboutImg.Left := (InfoPage.Surface.Width - AboutImg.Width) div 2;
  AboutImg.Top := EmailLink.Top + EmailLink.Height + ScaleY(16);
  { If the page is too short for the full size, shrink to fit what's left. }
  if AboutImg.Top + AboutImg.Height > InfoPage.Surface.Height then
  begin
    AboutImg.Height := InfoPage.Surface.Height - AboutImg.Top - ScaleY(4);
    AboutImg.Width := AboutImg.Height;
    AboutImg.Left := (InfoPage.Surface.Width - AboutImg.Width) div 2;
  end;

  CustomizationPage := CreateCustomPage(wpSelectDir,
    'UI Customization',
    'Select which UI customizations to install. The LSR command is always available from the AutoCAD command line.');
  CustomizationPageID := CustomizationPage.ID;

  RibbonCheck := TNewCheckBox.Create(CustomizationPage);
  RibbonCheck.Parent := CustomizationPage.Surface;
  RibbonCheck.Left := 0;
  RibbonCheck.Top := 8;
  RibbonCheck.Width := 350;
  RibbonCheck.Height := 20;
  RibbonCheck.Caption := 'Install ribbon button (Add-ins tab)';
  RibbonCheck.Checked := True;

  MenuCheck := TNewCheckBox.Create(CustomizationPage);
  MenuCheck.Parent := CustomizationPage.Surface;
  MenuCheck.Left := 0;
  MenuCheck.Top := 36;
  MenuCheck.Width := 350;
  MenuCheck.Height := 20;
  MenuCheck.Caption := 'Install menu item (Custom Apps > Layer Standardizer)';
  MenuCheck.Checked := True;

  WizardForm.BringToFront;
  ForceWindowToFront(WizardForm.Handle);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = CustomizationPageID then
  begin
    InstallRibbon := RibbonCheck.Checked;
    InstallMenu := MenuCheck.Checked;
  end;
end;

function BoolToJson(Value: Boolean): String;
begin
  if Value then
    Result := 'true'
  else
    Result := 'false';
end;

{ Scans every AutoCAD profile in the registry for settings that silently
  disable the bundle Autoloader -- if APPAUTOLOAD is 0, AutoCAD never
  loads ApplicationPlugins bundles at startup and the plugin appears
  "installed but dead" with no error anywhere. Warns the user so they can
  fix it (SECURELOAD/TRUSTEDPATHS don't need checking: this installer
  targets the machine-wide Program Files plugins folder, which is trusted
  at every SECURELOAD level). }
procedure WarnIfAutoloadDisabled;
var
  SeriesKeys, ProductKeys, ProfileKeys: TArrayOfString;
  i, j, k: Integer;
  Base, VarsKey, Value: String;
begin
  Base := 'Software\Autodesk\AutoCAD';
  if not RegGetSubkeyNames(HKCU, Base, SeriesKeys) then Exit;
  for i := 0 to GetArrayLength(SeriesKeys) - 1 do
  begin
    if not RegGetSubkeyNames(HKCU, Base + '\' + SeriesKeys[i], ProductKeys) then Continue;
    for j := 0 to GetArrayLength(ProductKeys) - 1 do
    begin
      if not RegGetSubkeyNames(HKCU, Base + '\' + SeriesKeys[i] + '\' + ProductKeys[j] + '\Profiles', ProfileKeys) then Continue;
      for k := 0 to GetArrayLength(ProfileKeys) - 1 do
      begin
        VarsKey := Base + '\' + SeriesKeys[i] + '\' + ProductKeys[j] + '\Profiles\' + ProfileKeys[k] + '\Variables';
        if RegQueryStringValue(HKCU, VarsKey, 'APPAUTOLOAD', Value) and (Value = '0') then
        begin
          MsgBox('Heads up: the AutoCAD profile "' + ProfileKeys[k] + '" (' +
            SeriesKeys[i] + ') has plugin auto-loading disabled ' +
            '(APPAUTOLOAD = 0).' + #13#10#13#10 +
            'ACAD Layer Standardizer installed correctly, but AutoCAD will ' +
            'not load it at startup until auto-loading is re-enabled. ' +
            'In AutoCAD, type APPAUTOLOAD and set it back to 14 (the default).',
            mbInformation, MB_OK);
          Exit; { one warning is enough }
        end;
      end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigFile: String;
  ConfigContent: String;
begin
  if CurStep = ssPostInstall then
  begin
    WarnIfAutoloadDisabled;
    ConfigFile := ExpandConstant('{userappdata}\AcLayerStandardizer\config.json');
    { Only scaffold config.json on first install. On upgrade, leave the
      user's existing TemplateDwgPath/MemoryFilePath/etc. alone rather than
      clobbering them back to empty strings every time. }
    if not FileExists(ConfigFile) then
    begin
      ConfigContent := '{' + #13#10;
      ConfigContent := ConfigContent + '  "TemplateDwgPath": "",' + #13#10;
      ConfigContent := ConfigContent + '  "MemoryFilePath": "",' + #13#10;
      ConfigContent := ConfigContent + '  "InstallRibbon": ' + BoolToJson(InstallRibbon) + ',' + #13#10;
      ConfigContent := ConfigContent + '  "InstallMenu": ' + BoolToJson(InstallMenu) + #13#10;
      ConfigContent := ConfigContent + '}';
      SaveStringToFile(ConfigFile, ConfigContent, False);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ConfigDir: String;
  LocalDir: String;
  ConfigFile: String;
  CuiFile: String;
  MemFile: String;
  UiPrefsFile: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    ConfigDir := ExpandConstant('{userappdata}\AcLayerStandardizer');
    LocalDir := ExpandConstant('{localappdata}\AcLayerStandardizer');

    { Translation memory: default filename set by PluginConfig/EntryPoint.
      Keeping this name in sync with EntryPoint.cs's default is important —
      it was previously checking a stale filename ("translations.json") that
      never matched what the app actually writes, so this prompt never fired
      and the file was silently left behind. }
    MemFile := ConfigDir + '\standards_memory.json';
    if FileExists(MemFile) then
    begin
      if MsgBox('Remove layer mapping memory file?' + #13#10#13#10 +
        'This contains your learned/custom layer mappings (standards_memory.json).',
        mbConfirmation, MB_YESNO) = IDYES then
        DeleteFile(MemFile);
    end;

    { UI preferences (mapping-editor window size/zoom/pan): machine-specific,
      so UserPreferences.cs writes it under LocalAppData rather than the
      Roaming ConfigDir used for config.json/standards_memory.json. }
    UiPrefsFile := LocalDir + '\ui_preferences.json';
    if FileExists(UiPrefsFile) then
    begin
      if MsgBox('Remove saved UI preferences file?' + #13#10#13#10 +
        'This contains your personal UI settings (ui_preferences.json).',
        mbConfirmation, MB_YESNO) = IDYES then
        DeleteFile(UiPrefsFile);
    end;

    CuiFile := ConfigDir + '\LayerStandardizer.cuix';
    if FileExists(CuiFile) then
      DeleteFile(CuiFile);

    ConfigFile := ConfigDir + '\config.json';
    if FileExists(ConfigFile) then
      DeleteFile(ConfigFile);

    if DirExists(ConfigDir) then
      RemoveDir(ConfigDir);
  end;
end;
