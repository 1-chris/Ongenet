; Ongenet Windows installer (Inno Setup 6)
; Build: scripts/build-windows-installer.sh (requires iscc on PATH)
; Pass version: iscc /DMyAppVersion=0.40.0 ongenet.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName "Ongenet"
#define MyAppPublisher "Ongenet"
#define MyAppURL "https://onge.net/"
#define MyAppExeName "Ongenet.exe"
; Fixed GUID — never change (enables in-place upgrade)
#define MyAppId "{{A7B3C4D5-E6F7-4890-ABCD-EF1234567890}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL=https://github.com/1-chris/Ongenet/releases
DefaultDirName={autopf}\Ongenet
DefaultGroupName=Ongenet
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir=..\..\dist
OutputBaseFilename=Ongenet-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\icons\ongenet.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
UsePreviousAppDir=yes
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\..\Ongenet.Desktop\bin\Release\net10.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Ongenet"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Ongenet"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigPath: String;
  Manifest: String;
begin
  if CurStep = ssPostInstall then
  begin
    ConfigPath := ExpandConstant('{userappdata}\Ongenet');
    ForceDirectories(ConfigPath);
    Manifest := '{' + #13#10 +
      '  "method": "inno",' + #13#10 +
      '  "installPath": "' + ExpandConstant('{app}') + '",' + #13#10 +
      '  "version": "{#MyAppVersion}",' + #13#10 +
      '  "installedAt": "' + GetDateTimeString('yyyy"-"mm"-"dd"T"hh":"nn":"ss"Z"', #0, #0) + '"' + #13#10 +
      '}';
    SaveStringToFile(ConfigPath + '\install.json', Manifest, False);
  end;
end;
