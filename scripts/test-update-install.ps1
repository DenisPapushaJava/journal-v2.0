$ErrorActionPreference = 'Stop'
# Проверка настоящего установщика/Update.exe в отдельной папке. Никаких рабочих MDB или профилей.
$root = Join-Path ([IO.Path]::GetTempPath()) ('journal-update-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$env:JOURNAL_UPDATE_PROBE_ROOT = $root
$releases = Join-Path $root 'releases'
$installed = Join-Path $root 'installed'
$database = Join-Path $root 'OrderDB.mdb'
$config = Join-Path $root 'data-source.json'
[IO.File]::WriteAllText($database, 'ARTIFICIAL SENTINEL, NOT AN ACCESS DATABASE')
[IO.File]::WriteAllText($config, '{"DatabasePath":"existing-OrderDB.mdb"}')
$databaseHash = (Get-FileHash $database).Hash
$configHash = (Get-FileHash $config).Hash
& dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
foreach ($version in @('2026.1.0', '2026.2.0')) {
    $publish = Join-Path $root "publish-$version"
    & dotnet publish tests/Fixtures/UpdateProbe/Jornal.UpdateProbe.csproj -c Release -r win-x64 --self-contained false "-p:Version=$version" -p:RestoreLockedMode=true -p:DebugType=None -p:DebugSymbols=false -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Probe publish failed.' }
    & dotnet tool run vpk pack --packId Journal.UpdateProbe --packVersion $version --packDir $publish --mainExe Jornal.UpdateProbe.exe --runtime win-x64 --channel win-x64 --outputDir $releases --noPortable --framework net10.0-x64-runtime
    if ($LASTEXITCODE -ne 0) { throw 'Probe packaging failed.' }
    if ($version -eq '2026.1.0') {
        $setup = Get-ChildItem $releases -Filter '*Setup.exe' | Select-Object -First 1
        if (-not $setup) { throw 'No probe installer.' }
        Copy-Item $setup.FullName (Join-Path $root 'v1-setup.exe')
    }
}
# Проверяем обновление с тем же минимальным набором, который публикуется в GitHub.
Get-ChildItem $releases -File | Where-Object { $_.Name -ne 'releases.win-x64.json' -and $_.Name -notlike '*-full.nupkg' } | Remove-Item
$setupProcess = Start-Process (Join-Path $root 'v1-setup.exe') -ArgumentList @('--silent', '--installto', ('"' + $installed + '"')) -Wait -PassThru
if ($setupProcess.ExitCode -ne 0) { throw "Installer failed: $($setupProcess.ExitCode). Test files: $root" }
Start-Sleep -Seconds 2
if (-not (Test-Path (Join-Path $root 'started-v1'))) {
    Start-Process (Join-Path $installed 'current/Jornal.UpdateProbe.exe') | Out-Null
}
$deadline = [DateTime]::UtcNow.AddSeconds(90)
while (-not (Test-Path (Join-Path $root 'result.json'))) {
    if (Test-Path (Join-Path $root 'failed.txt')) { throw (Get-Content (Join-Path $root 'failed.txt') -Raw) }
    if ([DateTime]::UtcNow -gt $deadline) { throw "Update timed out. Test files: $root" }
    Start-Sleep -Seconds 1
}
$result = Get-Content (Join-Path $root 'result.json') -Raw | ConvertFrom-Json
if ($result.version -ne '2026.2.0' -or -not $result.restored) { throw 'Wrong restarted version or draft.' }
if ((Get-FileHash $database).Hash -ne $databaseHash -or (Get-FileHash $config).Hash -ne $configHash) { throw 'External data changed.' }
Write-Host 'Установка 2026.1.0 → загрузка → Update.exe → перезапуск 2026.2.0: успешно. Результат восстановлен; внешние данные и настройки не изменились.'
# Папка остаётся на одноразовом runner для диагностики до завершения задания.
