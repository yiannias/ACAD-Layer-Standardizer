; ACAD Layer Standardizer - Inno Setup Script
; Build with: ISCC.exe ACADLayerStandardizer.iss

#define MyAppName "ACAD Layer Standardizer"
#define MyAppPublisher "CGY"
#define MyAppURL "https://github.com/yiannias/ACAD-Layer-Standardizer"
#define MyAppVersion GetEnv('MYAPPVERSION')
#define MyAppAcadVersion GetEnv('MYAPPACADVERSION')

[Setup]
AppId={{E5B07CB1-B0A8-40B7-B225-3DAF58FCAF57}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\AcLayerStandardizer.bundle
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=AcLayerStandardizer_{#MyAppAcadVersion}
SetupIconFile=assets\LayerStandardizer.ico
WizardSmallImageFile=assets\LayerStandardizer_header.png
WizardImageFile=assets\LayerStandardizer_sidebar.png
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\Contents\Windows\64-bit\AcLayerStandardizer.dll
WizardStyle=modern dark

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\src\AcLayerStandardizer\bin\Release\net10.0-windows\AcLayerStandardizer.dll"; DestDir: "{app}\Contents\Windows\64-bit"; Flags: ignoreversion
Source: "..\src\AcLayerStandardizer\bin\Release\net10.0-windows\Nodify.dll"; DestDir: "{app}\Contents\Windows\64-bit"; Flags: ignoreversion
Source: "..\dist\PackageContents.xml"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\config.json"; DestDir: "{userappdata}\AcLayerStandardizer"; Flags: ignoreversion onlyifdoesntexist
Source: "assets\LayerStandardizer.cuix"; DestDir: "{userappdata}\AcLayerStandardizer"; Flags: ignoreversion

[Dirs]
Name: "{userappdata}\AcLayerStandardizer"; Flags: uninsalwaysuninstall

[Icons]
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\AcLayerStandardizer"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\AcLayerStandardizer"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

[Code]
var
  InstallRibbon: Boolean;
  InstallMenu: Boolean;
  CustomizationPageID: Integer;
  InfoPageID: Integer;
  RibbonCheck: TNewCheckBox;
  MenuCheck: TNewCheckBox;

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
  CustomizationLbl: TNewStaticText;
begin
  InfoPage := CreateCustomPage(wpWelcome,
    'About ACAD Layer Standardizer',
    'This application is free to use, open source, and MIT licensed.');
  InfoPageID := InfoPage.ID;

  InfoLbl := TNewStaticText.Create(InfoPage);
  InfoLbl.Parent := InfoPage.Surface;
  InfoLbl.Left := 0;
  InfoLbl.Top := 8;
  InfoLbl.Width := 400;
  InfoLbl.Height := 60;
  InfoLbl.AutoSize := False;
  InfoLbl.Caption := 'ACAD Layer Standardizer analyzes DWG layers against a template and maps them to a master standard layer set.';

  GitHubLink := TNewStaticText.Create(InfoPage);
  GitHubLink.Parent := InfoPage.Surface;
  GitHubLink.Left := 0;
  GitHubLink.Top := 72;
  GitHubLink.Width := 400;
  GitHubLink.Height := 20;
  GitHubLink.Caption := 'GitHub: https://github.com/yiannias/ACAD-Layer-Standardizer';
  GitHubLink.Font.Color := clBlue;
  GitHubLink.Font.Style := [fsUnderline];
  GitHubLink.Cursor := crHand;
  GitHubLink.OnClick := @GitHubLinkClick;

  EmailLink := TNewStaticText.Create(InfoPage);
  EmailLink.Parent := InfoPage.Surface;
  EmailLink.Left := 0;
  EmailLink.Top := 96;
  EmailLink.Width := 400;
  EmailLink.Height := 20;
  EmailLink.Caption := 'Contact: yiannias@gmail.com';
  EmailLink.Font.Color := clBlue;
  EmailLink.Font.Style := [fsUnderline];
  EmailLink.Cursor := crHand;
  EmailLink.OnClick := @EmailLinkClick;

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

procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigFile: String;
  ConfigContent: String;
begin
  if CurStep = ssPostInstall then
  begin
    ConfigFile := ExpandConstant('{userappdata}\AcLayerStandardizer\config.json');
    ConfigContent := '{' + #13#10;
    ConfigContent := ConfigContent + '  "TemplateDwgPath": "",' + #13#10;
    ConfigContent := ConfigContent + '  "MemoryFilePath": ""' + #13#10;
    ConfigContent := ConfigContent + '}';
    SaveStringToFile(ConfigFile, ConfigContent, False);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ConfigDir: String;
  ConfigFile: String;
  CuiFile: String;
  MemFile: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    ConfigDir := ExpandConstant('{userappdata}\AcLayerStandardizer');

    MemFile := ConfigDir + '\translations.json';
    if FileExists(MemFile) then
    begin
      if MsgBox('Remove memory/translations file?' + #13#10#13#10 +
        'This contains your custom layer mappings.',
        mbConfirmation, MB_YESNO) = IDYES then
        DeleteFile(MemFile);
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
