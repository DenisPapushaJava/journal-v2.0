$ErrorActionPreference = 'Stop'
# Только одноразовая Windows CI-машина, искусственная MDB и случайная папка установки.
if (-not $env:CI) { throw 'Сценарий установки/удаления разрешён только на изолированной CI-машине.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('journal-offline-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$setup = @(Get-ChildItem artifacts/release -Filter '*-OfflineSetup.exe')
if ($setup.Count -ne 1) { throw 'Ожидается один полный установщик.' }
$installed = Join-Path $root 'Журнал заявок {app}'
$fixture = Join-Path $root 'synthetic.mdb'
& "$env:WINDIR/SysWOW64/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -File tests/Fixtures/New-AccessFixture.ps1 -Destination $fixture -RequireProvider
if ($LASTEXITCODE -ne 0) { throw 'Не удалось подготовить искусственную MDB.' }
$fixtureHash = (Get-FileHash $fixture).Hash
$profileRoot = Join-Path $env:LOCALAPPDATA 'Journal'
New-Item -ItemType Directory -Force -Path $profileRoot | Out-Null
$profile = Join-Path $profileRoot 'data-source.json'
$updatePreferences = Join-Path $profileRoot 'update-settings.json'
if (Test-Path $profile) { throw 'Тест не должен заменять существующий профиль.' }
if (Test-Path $updatePreferences) { throw 'Тест не должен заменять настройки обновлений.' }
[IO.File]::WriteAllText($profile, '{"Version":1,"DatabasePath":null}', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($updatePreferences, '{"Version":1,"IncludePreview":true}', [Text.UTF8Encoding]::new($false))
$profileHash = (Get-FileHash $profile).Hash
$updatePreferencesHash = (Get-FileHash $updatePreferences).Hash
$app = $null
function Invoke-Setup([string] $log, [string[]] $extra, [bool] $expectSuccess) {
    $process = Start-Process $setup[0].FullName -ArgumentList (@('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ('/LOG="' + $log + '"')) + $extra) -PassThru
    if (-not $process.WaitForExit(180000)) { Stop-Process -Id $process.Id -Force; throw "Installer timeout. Logs: $root" }
    $process.Refresh()
    $success = $process.ExitCode -in @(0, 3010)
    if ($success -ne $expectSuccess) {
        Get-Content $log -ErrorAction SilentlyContinue | Write-Host
        throw "Unexpected installer result: $($process.ExitCode), expected success: $expectSuccess. Logs: $root"
    }
    if ((Get-FileHash $fixture).Hash -ne $fixtureHash -or (Get-FileHash $profile).Hash -ne $profileHash) { throw 'Внешняя MDB/профиль изменились при установке.' }
    if ((Get-FileHash $updatePreferences).Hash -ne $updatePreferencesHash) { throw 'Канал обновлений изменился при установке.' }
}
try {
    # Папка с искусственной MDB должна быть отклонена до установки зависимостей/приложения.
    Invoke-Setup (Join-Path $root 'reject-data-directory.log') @(('/JOURNALDIR="' + $root + '"')) $false
    if (Test-Path (Join-Path $root 'Update.exe')) { throw 'Установщик принял папку с пользовательскими данными.' }
    Invoke-Setup (Join-Path $root 'reject-drive-root.log') @(('/JOURNALDIR="' + [IO.Path]::GetPathRoot($root) + '"')) $false
    Invoke-Setup (Join-Path $root 'reject-program-files.log') @(('/JOURNALDIR="' + (Join-Path $env:ProgramFiles 'Journal per-user test') + '"')) $false
    foreach ($attempt in 1..2) {
        $log = Join-Path $root "setup-$attempt.log"
        # Первый запуск получает выбранную папку, второй находит её в регистрации Velopack.
        $directoryArgument = if ($attempt -eq 1) { @(('/JOURNALDIR="' + $installed + '"')) } else { @() }
        Invoke-Setup $log $directoryArgument $true
        $registered = Get-ItemProperty 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/DenisPapusha.Journal'
        if ($registered.InstallLocation.TrimEnd('\') -ne $installed) { throw 'Velopack зарегистрировал неверную папку.' }
    }
    Invoke-Setup (Join-Path $root 'reject-second-location.log') @(('/JOURNALDIR="' + (Join-Path $root 'another-location') + '"')) $false
    $hostPath = Join-Path $installed 'current/AccessHost/Jornal.AccessHost.exe'
    & $hostPath --probe
    if ($LASTEXITCODE -ne 0) { throw 'Установленный модуль Jet x86 не запускается.' }
    $env:JOURNAL_ACCESS_HOST = $hostPath
    $env:JOURNAL_SYNTHETIC_MDB = $fixture
    & dotnet test tests/Jornal.Tests/Jornal.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~AccessHostTests.NativeX64Client' --logger 'trx;LogFileName=access-host.trx' --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Нативная проверка x64 → x86 не прошла.' }
    [xml]$results = Get-Content artifacts/test-results/access-host.trx
    if ([int]$results.TestRun.ResultSummary.Counters.passed -ne 1) { throw 'Обязательная проверка хоста не выполнена.' }
    $app = Start-Process (Join-Path $installed 'current/Jornal.Desktop.exe') -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 500
        $app.Refresh()
        if ($app.HasExited) { throw "Установленное приложение завершилось до открытия окна: $($app.ExitCode)." }
    } while ($app.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($app.MainWindowHandle -eq 0) { throw 'Главное окно установленного приложения не открылось.' }
    if (-not $app.CloseMainWindow() -or -not $app.WaitForExit(10000)) { throw 'Приложение не завершилось обычным закрытием окна.' }
    $app = $null
    $diagnostic = Join-Path $root 'journal-diagnostics.json'
    & ./scripts/Get-JournalDiagnostics.ps1 -InstallDirectory $installed -OutputPath $diagnostic
    $diagnosticReport = Get-Content -LiteralPath $diagnostic -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($diagnosticReport.Version -ne 1 -or $diagnosticReport.Files.Count -eq 0) { throw 'Диагностический отчёт не содержит файлов установленной программы.' }
    foreach ($relative in @('Jornal.Desktop.exe', 'Update.exe', 'current\Jornal.Desktop.exe', 'current\AccessHost\Jornal.AccessHost.exe')) {
        if (-not @($diagnosticReport.Files | Where-Object { $_.RelativePath -eq $relative -and $_.SHA256.Length -eq 64 -and $_.SignatureStatus }).Count) {
            throw "Диагностика не проверила подпись/хеш $relative."
        }
    }
    $remove = Start-Process (Join-Path $installed 'Update.exe') -ArgumentList @('uninstall', '--silent') -Wait -PassThru
    if ($remove.ExitCode -ne 0) { throw "Uninstall failed: $($remove.ExitCode)." }
    if ((Get-FileHash $fixture).Hash -ne $fixtureHash -or (Get-FileHash $profile).Hash -ne $profileHash) { throw 'Внешняя MDB/профиль изменились при удалении.' }
    if ((Get-FileHash $updatePreferences).Hash -ne $updatePreferencesHash) { throw 'Канал обновлений изменился при запуске/удалении.' }
    Write-Host 'Офлайн-комплект: выбранная папка с пробелами/кириллицей, повторная установка без параметров, защита чужой папки, Jet x86 из x64, запуск/закрытие окна и удаление прошли. MDB и профиль сохранены.'
    Write-Host "Полный установщик: $([Math]::Round($setup[0].Length / 1MB, 2)) МиБ. Диагностика: $root"
}
finally {
    if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id -Force }
    Remove-Item Env:JOURNAL_ACCESS_HOST -ErrorAction SilentlyContinue
    Remove-Item Env:JOURNAL_SYNTHETIC_MDB -ErrorAction SilentlyContinue
    if (Test-Path $profile) { Remove-Item $profile }
    if (Test-Path $updatePreferences) { Remove-Item $updatePreferences }
    New-Item -ItemType Directory -Force artifacts/installer-logs | Out-Null
    Copy-Item (Join-Path $root '*.log') artifacts/installer-logs -ErrorAction SilentlyContinue
}
