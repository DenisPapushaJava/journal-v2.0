[Setup]
AppId=Journal.InstallerDiagnostics.Test
AppName=Journal Installer Diagnostics Test
AppVersion=1
CreateAppDir=no
Uninstallable=no
PrivilegesRequired=lowest
OutputBaseFilename=journal-installer-diagnostics-test

[Code]
#include "..\..\installer\windows\AccessHostDiagnostics.iss"

procedure Check(Value: Boolean; Message: String);
begin
  if not Value then RaiseException(Message);
end;

function InitializeSetup: Boolean;
var
  Started: Boolean;
  Code: Integer;
begin
  Check(IsAccessHostSecurityFailure(False, 4551), 'Missing integrity policy detection');
  Check(IsAccessHostSecurityFailure(False, 1260), 'Missing group policy detection');
  Check(IsAccessHostSecurityFailure(False, 577), 'Missing signature detection');
  Check(IsAccessHostSecurityFailure(True, -1058471934), 'Missing integrity exit detection');
  Check(IsAccessHostSecurityFailure(True, -1073740760), 'Missing signature exit detection');
  Check(not IsAccessHostSecurityFailure(False, 5), 'Access denied alone is not proof of a security policy');
  Check(not IsAccessHostSecurityFailure(True, 3), 'Jet availability must not be called a policy block');
  Check(not IsAccessHostSecurityFailure(True, 4551), 'Native exit code is not a CreateProcess error');
  Check(Pos('4551', AccessHostFailureText(False, 4551)) > 0, 'Native error was lost');
  Check(Pos('Smart App Control', AccessHostFailureText(False, 4551)) > 0, 'Missing policy explanation');
  Check(Pos('подпись или целостность', AccessHostFailureText(False, 577)) > 0, 'Misleading signature explanation');
  Check(Pos('Jet', AccessHostFailureText(True, 3)) > 0, 'Missing provider explanation');
  Check(AccessHostCodeText(-1058471934) = '0xC0E90002', 'Wrong NTSTATUS presentation');
  Started := Exec(ExpandConstant('{sys}\JournalMissingAccessHostFixture.exe'), '--probe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Check(not Started and (Code = 2), 'Exec must preserve the actual startup error');
  Check(Pos('Код: 2.', AccessHostFailureText(Started, Code)) > 0, 'Startup error was replaced');
  Log('JOURNAL_INSTALLER_DIAGNOSTICS_OK');
  { Только проверки; установка и изменение настроек не выполняются. }
  Result := False;
end;
