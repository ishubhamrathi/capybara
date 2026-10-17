<CodeBlock language="ini" editable>
#define MyAppName "CapyBara"
#define MyAppVersion GetEnv("APP_VERSION")
#define MyAppExeName GetEnv("APP_EXE_NAME")
#define PublishDir GetEnv("PUBLISH_DIR")

[Setup]
AppId={{A81C5B70-7D20-4F31-9B9C-5D78F17A2E61}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=CapyBara
DefaultDirName={autopf}\CapyBara
DefaultGroupName=CapyBara
DisableProgramGroupPage=yes
UninstallDisplayName=CapyBara
Uninstallable=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=artifacts\installer
OutputBaseFilename=CapyBara-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
[Files]
Source: "{#PublishDir}*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\CapyBara"; Filename: "{app}{#MyAppExeName}"
Name: "{autodesktop}\CapyBara"; Filename: "{app}{#MyAppExeName}"; Tasks: desktopicon
[Run]
Filename: "{app}{#MyAppExeName}"; Description: "Launch CapyBara"; Flags: postinstall nowait skipifsilent
</CodeBlock>