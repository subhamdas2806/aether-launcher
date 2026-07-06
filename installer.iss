; Inno Setup Compiler Script for Aether
[Setup]
AppName=Aether
AppVersion=1.0.0
DefaultDirName={localappdata}\Programs\Aether
DefaultGroupName=Aether
UninstallDisplayIcon={app}\Aether.exe
OutputDir=setup
OutputBaseFilename=AetherSetup
Compression=lzma
SolidCompression=yes
SetupIconFile=Assets\AppIcon.ico
DisableProgramGroupPage=yes
PrivilegesRequired=lowest

[Files]
Source: "bin\Release\net8.0-windows10.0.26100.0\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{localappdata}\Programs\Aether\Aether"; Filename: "{app}\Aether.exe"
Name: "{userdesktop}\Aether"; Filename: "{app}\Aether.exe"
Name: "{userstartmenu}\Programs\Aether"; Filename: "{app}\Aether.exe"

[Run]
Filename: "{app}\Aether.exe"; Description: "Launch Aether"; Flags: nowait postinstall skipifsilent
