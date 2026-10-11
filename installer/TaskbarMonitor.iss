#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{B3F1C7A2-5D8E-4A6B-9C21-7E4F0A9D3B58}
AppName=Taskbar Monitor
AppVersion={#AppVersion}
AppPublisher=miguelzvd
DefaultDirName={autopf}\TaskbarMonitor
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\release
OutputBaseFilename=TaskbarMonitor-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Taskbar Monitor
CloseApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Taskbar Monitor"; Filename: "{app}\TaskbarMonitor.exe"

[Run]
Filename: "{app}\TaskbarMonitor.exe"; Description: "Abrir o Taskbar Monitor"; Flags: nowait postinstall skipifsilent

; O Inno Setup executa o [UninstallRun] de baixo para cima: o taskkill roda antes de apagar a tarefa.
[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN TaskbarMonitor /F"; Flags: runhidden; RunOnceId: "DelTask"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM TaskbarMonitor.exe"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
function IsDotNet8DesktopInstalled(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNet8DesktopInstalled() then
  begin
    MsgBox('O Taskbar Monitor precisa do .NET 8 Desktop Runtime.' + #13#10 +
           'Instale com: winget install Microsoft.DotNet.DesktopRuntime.8' + #13#10 +
           'e rode este instalador de novo.', mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM TaskbarMonitor.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    { Se a inicializacao automatica estava ativa, aponta a tarefa para o exe instalado. }
    if Exec(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN TaskbarMonitor', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
       and (ResultCode = 0) then
      Exec(ExpandConstant('{sys}\schtasks.exe'),
           ExpandConstant('/Change /TN TaskbarMonitor /TR "\"{app}\TaskbarMonitor.exe\""'),
           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
