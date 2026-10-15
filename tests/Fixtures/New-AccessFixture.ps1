param([Parameter(Mandatory = $true)][string]$Destination)
$ErrorActionPreference = 'Stop'

# Только искусственные данные. База создаётся во временной папке runner, в Git её нет.
$provider = @('Microsoft.ACE.OLEDB.16.0', 'Microsoft.ACE.OLEDB.12.0') |
    Where-Object { $null -ne [Type]::GetTypeFromProgID($_) } | Select-Object -First 1
if (-not $provider) {
    Write-Host '::notice::Совместимый ACE x64 отсутствует. Интеграционный тест Access будет пропущен; сборка адаптера проверяется.'
    exit 0
}

$catalog = $null
$connection = $null
try {
    $catalog = New-Object -ComObject ADOX.Catalog
    $catalog.Create("Provider=$provider;Data Source=$Destination;Jet OLEDB:Engine Type=5;")
    $connection = $catalog.ActiveConnection
    $null = $connection.Execute(@'
CREATE TABLE [Orders] (
 [Код] COUNTER PRIMARY KEY, [Дата] DATETIME, [Менеджер] TEXT(255), [Спецификация] TEXT(255),
 [Заказчик] TEXT(255), [Вид_резки] TEXT(255), [Марка_стали] TEXT(255), [Размер_листа] TEXT(255),
 [Кол_во_листов] TEXT(255), [Стоимость] CURRENCY, [Толщина] DOUBLE, [Ход] LONG,
 [Вес] TEXT(255), [БЛЦБ] TEXT(255), [Color] LONG, [Коэффициент] TEXT(255))
'@)
    $null = $connection.Execute(@'
INSERT INTO [Orders] ([Код], [Дата], [Менеджер], [Спецификация], [Заказчик], [Вид_резки], [Марка_стали], [Размер_листа],
 [Кол_во_листов], [Стоимость], [Толщина], [Ход], [Вес], [БЛЦБ], [Color], [Коэффициент])
VALUES (42, #01/02/2026 08:31:42#, 'Менеджер', '0102-42', 'Пример ''А'', детали', 'Лазер', '5083H111', '1500x6000',
 '02 / комплект', 1234.5678, 2.5, 31, '0012.350', 'БЛЦБ-000042', -16711936, ' 1,25 | 20 | 340 | 2 | N2 ')
'@)
    $null = $connection.Execute("INSERT INTO [Orders] ([Код], [Спецификация]) VALUES (43, 'без даты')")
    Write-Host "Создана искусственная Jet MDB через $provider."
}
finally {
    if ($connection) { $connection.Close(); $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($connection) }
    if ($catalog) { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($catalog) }
}
if ($env:GITHUB_ENV) {
    "JOURNAL_SYNTHETIC_MDB=$Destination" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
} else {
    Write-Host 'Для теста задайте JOURNAL_SYNTHETIC_MDB равным пути созданного файла.'
}
