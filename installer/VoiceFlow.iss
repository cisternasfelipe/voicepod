; Inno Setup script for VoiceFlow.
; Build the payload first:
;   dotnet publish VoiceFlow.App\VoiceFlow.App.csproj -c Release -r win-x64 --self-contained true -o publish
; Then compile this script with Inno Setup 6:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\VoiceFlow.iss

#define AppName "VoiceFlow"
#define AppVersion "0.1.0"
#define AppPublisher "VoiceFlow"
#define AppExeName "VoiceFlow.exe"
#define PublishDir "..\publish"

[Setup]
AppId={{7C2C2B0E-6C8B-4F0F-9E5E-3B6A9E1F1A21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=VoiceFlow-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; VoiceFlow is x64 only and never needs elevation at runtime.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\VoiceFlow.App\Resources\voiceflow.ico

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
spanish.CreateDesktopIcon=Crear un acceso directo en el escritorio
spanish.StartWithWindows=Iniciar VoiceFlow con Windows (en la bandeja del sistema)
spanish.LaunchAfterInstall=Ejecutar VoiceFlow
spanish.RemoveUserData=Borrar también el historial, los ajustes y el modelo descargado
spanish.RemoveUserDataTitle=Datos de usuario
english.CreateDesktopIcon=Create a desktop shortcut
english.StartWithWindows=Start VoiceFlow with Windows (in the system tray)
english.LaunchAfterInstall=Launch VoiceFlow
english.RemoveUserData=Also delete history, settings and the downloaded model
english.RemoveUserDataTitle=User data

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Written only when the user ticks the startup task; the app manages this key too.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "VoiceFlow"; ValueData: """{app}\{#AppExeName}"" --tray"; \
    Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Nothing here by default: user data is only removed when explicitly requested below.

[Code]
var
  RemoveDataPage: TInputOptionWizardPage;

procedure InitializeWizard;
begin
  RemoveDataPage := nil;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\VoiceFlow');

    if DirExists(DataDir) then
    begin
      // History, settings and the downloaded model survive an uninstall unless asked otherwise.
      if MsgBox(ExpandConstant('{cm:RemoveUserData}'), mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
