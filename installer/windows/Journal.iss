; Обёртка поставляет зависимости; установкой/удалением/обновлением Journal владеет Velopack.
#ifndef JournalVersion
  #error JournalVersion is required
#endif

[Setup]
AppId=DenisPapusha.Journal.OfflineBootstrap
AppName=Journal
AppVersion={#JournalVersion}
AppPublisher=DenisPapusha
AppPublisherURL=https://github.com/DenisPapushaJava/journal-v2.0
CreateAppDir=no
Uninstallable=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
MinVersion=10.0.19045
SetupMutex=DenisPapusha.Journal.OfflineSetup
AppMutex=Local\DenisPapusha.Journal.Running
OutputBaseFilename=Journal-{#JournalVersion}-win-x64-OfflineSetup
SetupIconFile=..\..\docs\design\assets\journal-icon.ico
WizardStyle=modern
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
Compression=lzma2
SolidCompression=yes
AllowCancelDuringInstall=no
RestartIfNeededByRun=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
ReadyLabel1=Всё готово к установке Journal
ReadyLabel2a=Нужные компоненты уже включены в установщик. Интернет для установки не требуется.%n%nСуществующая база, старая программа и Office сохраняются. При необходимости Windows запросит разрешение установить среду .NET.
FinishedLabel=Journal установлен. Выберите существующую базу в приложении через кнопку «База данных».

[Files]
Source: "{#RuntimeX64}"; DestDir: "{tmp}"; DestName: "runtime-x64.exe"; Flags: dontcopy
Source: "{#RuntimeX86}"; DestDir: "{tmp}"; DestName: "runtime-x86.exe"; Flags: dontcopy
Source: "{#AccessHost}\*"; DestDir: "{tmp}\AccessHost"; Flags: dontcopy recursesubdirs
Source: "{#AppSetup}"; DestDir: "{tmp}"; DestName: "Journal-AppSetup.exe"; Flags: dontcopy

[Run]
Filename: "{code:LaunchPath}"; Description: "Запустить Journal"; Flags: postinstall nowait skipifsilent

[Code]
var
  RuntimeRestartRequired: Boolean;

function InstallRoot: String;
begin
  Result := ExpandConstant('{param:JOURNALDIR|{localappdata}\DenisPapusha.Journal}');
  if (Pos('"', Result) > 0) or (ExtractFileDrive(Result) = '') then
    RaiseException('Некорректная папка установки Journal.');
end;

function LaunchPath(Param: String): String;
begin
  Result := InstallRoot + '\Jornal.Desktop.exe';
end;

function CompatibleRuntimeVersion(Value: String): Boolean;
begin
  Result := (Copy(Value, 1, 5) = '10.0.') and
    (StrToIntDef(Copy(Value, 6, Length(Value)), -1) >= {#RuntimePatch});
end;

function HasRuntime(Architecture: String): Boolean;
var
  Versions: TArrayOfString;
  Index: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\' + Architecture + '\sharedfx\Microsoft.NETCore.App', Versions) then
    for Index := 0 to GetArrayLength(Versions) - 1 do
      if CompatibleRuntimeVersion(Versions[Index]) then begin Result := True; Exit; end;
  if RegGetValueNames(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\' + Architecture + '\sharedfx\Microsoft.NETCore.App', Versions) then
    for Index := 0 to GetArrayLength(Versions) - 1 do
      if CompatibleRuntimeVersion(Versions[Index]) then begin Result := True; Exit; end;
end;

function EnsureRuntime(Architecture: String): String;
var
  Code: Integer;
  FileName: String;
begin
  Result := '';
  if HasRuntime(Architecture) then begin Log('Совместимая .NET ' + Architecture + ' уже установлена.'); Exit; end;
  FileName := 'runtime-' + Architecture + '.exe';
  ExtractTemporaryFile(FileName);
  WizardForm.StatusLabel.Caption := 'Устанавливаем необходимые компоненты…';
  if not ShellExec('runas', ExpandConstant('{tmp}\') + FileName, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code) then
  begin
    Result := 'Не удалось установить .NET. Разрешите установку компонентов в запросе Windows и повторите попытку. Код: ' + IntToStr(Code);
    Exit;
  end;
  Log('.NET ' + Architecture + ', код завершения: ' + IntToStr(Code));
  if Code = 3010 then RuntimeRestartRequired := True
  else if Code <> 0 then begin
    Result := 'Не удалось установить .NET, код: ' + IntToStr(Code) + '. Journal и база не изменены.';
    Exit;
  end;
  if not HasRuntime(Architecture) then
    Result := '.NET пока недоступна. Перезапустите Windows и повторите установку Journal.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := EnsureRuntime('x64');
  if Result <> '' then Exit;
  Result := EnsureRuntime('x86');
  if Result <> '' then Exit;
  ExtractTemporaryFiles('{tmp}\AccessHost\*');
  if not Exec(ExpandConstant('{tmp}\AccessHost\Jornal.AccessHost.exe'), '--probe', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  if Code <> 0 then
    Result := 'Встроенный компонент Windows для базы MDB недоступен. Journal не установлен. Требуется восстановление компонентов Windows; Office и база не изменены. Код: ' + IntToStr(Code);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then begin
    ExtractTemporaryFile('Journal-AppSetup.exe');
    if not Exec(ExpandConstant('{tmp}\Journal-AppSetup.exe'), '--silent --installto "' + InstallRoot + '"', '', SW_SHOW, ewWaitUntilTerminated, Code) then
      RaiseException('Не удалось запустить установку Journal. Код: ' + IntToStr(Code));
    if Code <> 0 then RaiseException('Установка Journal не завершена. Код: ' + IntToStr(Code));
    if not FileExists(LaunchPath('')) then RaiseException('Не найдена установленная программа Journal. Повторите установку.');
  end;
end;

function NeedRestart: Boolean;
begin
  Result := RuntimeRestartRequired;
end;
