#define MyAppName "别离检测工具"
#ifndef MyAppVersion
#define MyAppVersion "2.5.0"
#endif
#ifndef PublishDir
#define PublishDir "..\别离检测工具-多文件发布版-v2.5"
#endif
#ifndef InstallerOutputDir
#define InstallerOutputDir "..\安装程序"
#endif
#define MyAppPublisher "别离检测工具开源项目"
#define MyAppExeName "别离检测工具.exe"

[Setup]
AppId={{ADE0532A-05E8-42E0-8F9D-CBEF585160D6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename=别离检测工具_安装程序_v{#MyAppVersion}
SetupIconFile=..\Assets\app-icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion=2.5.0.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription=电脑硬件与温度检测工具安装程序
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=Copyright (C) 2026 shanyin119

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载{#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "运行{#MyAppName}"; Flags: nowait postinstall skipifsilent
