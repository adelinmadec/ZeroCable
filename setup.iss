[Setup]
AppName=ZeroCable
AppVersion=1.0
AppPublisher=Adelin Madec
DefaultDirName={autopf}\ZeroCable
DefaultGroupName=ZeroCable
OutputDir=Output
OutputBaseFilename=ZeroCable_Setup
SetupIconFile=app.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; C'est ici qu'on dit d'inclure tous les fichiers générés par .NET
Source: "bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ZeroCable"; Filename: "{app}\ZeroCable.exe"
Name: "{autodesktop}\ZeroCable"; Filename: "{app}\ZeroCable.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ZeroCable.exe"; Description: "{cm:LaunchProgram,ZeroCable}"; Flags: nowait postinstall skipifsilent
