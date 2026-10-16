# Подпись и диагностика Windows

## Наблюдения первого прогона

Собственные EXE публичной `2026.1.0-alpha.2` не имеют встроенной Authenticode-подписи. Установщики .NET подписаны Microsoft; это не подписывает Journal. SHA-256 и проверка обновления обеспечивают целостность пакета, но не доверие Windows к издателю.

На снимках Windows 11 две отдельные ситуации:

- Нет прав записи в Program Files. Текущий OfflineSetup использует `PrivilegesRequired=lowest` и пользовательскую установку Velopack. Системный режим с UAC/обновлением защищённой папки ещё не реализован. [Модель Velopack](https://docs.velopack.io/packaging/operating-systems/windows).
- Windows блокирует корневой launcher `Jornal.Desktop.exe`: CreateProcess 4551, сообщение о политике управления приложениями. До MDB процесс не дошёл. Конкретная политика уточняется по CodeIntegrity, наличие Касперского само по себе не устанавливает причину. [Microsoft: диагностика](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/appcontrol-debugging-and-troubleshooting), [Kaspersky: собственный контроль приложений и отчёт](https://support.kaspersky.com/help/KSC/15.1/en-US/184079.htm).

## Диагностический отчёт

Скопируйте `scripts/Get-JournalDiagnostics.ps1` на проблемный ПК и выполните в обычном PowerShell, если запуск скриптов разрешён организацией:

```powershell
powershell.exe -NoProfile -File .\Get-JournalDiagnostics.ps1
```

Для нестандартной папки добавьте `-InstallDirectory "D:\Journal"`. JSON создаётся на рабочем столе; можно указать `-OutputPath` и `-HoursBack` (1–72, по умолчанию 2). Существующий файл не перезаписывается. Если политика запрещает скрипт, ИТ проверяет и запускает его разрешённым способом.

Отчёт содержит версию Windows, названия защитных продуктов, SHA-256 и Authenticode EXE/DLL/PS1, состояние Smart App Control и относящиеся к Journal события CodeIntegrity. Скрипт не запускает Journal, не открывает MDB, не меняет сертификаты/политики и никуда не отправляет отчёт. Локальные пути и текст событий могут содержать персональные/корпоративные сведения — просмотрите отчёт перед передачей. Недоступный журнал или отсутствие событий не доказывают отсутствие блокировки.

Для ИТ важны событие 3077, PolicyName/PolicyID и точный файл. Проверяются корневой launcher, Update, код в `current`, AccessHost и библиотеки. Разрешение только хеша предыдущего выпуска может перестать действовать после обновления; правила издателя сопровождаются проще, если политика организации их допускает. [Правила App Control](https://learn.microsoft.com/en-us/windows/security/threat-protection/windows-defender-application-control/select-types-of-rules-to-create).

## Выбор сертификата — пока не выполнен

| Вариант | Условия |
| --- | --- |
| Организационный сертификат | ИТ выдаёт сертификат подписи кода и согласует доверие/правила на управляемых ПК. Это внутреннее доверие, не универсальное публичное. |
| SignPath Foundation | Бесплатно для принятых открытых проектов: OSS-лицензия, проверяемая сборка из исходников, политика подписи и ручное одобрение выпусков. Приём нового проекта/страны заявителя и совместимость с нашим Velopack ещё нужно подтвердить. В корне репозитория общей OSS-лицензии пока нет; её выбирает владелец. [Условия](https://signpath.org/terms.html), [заявка](https://signpath.org/apply.html). |
| Azure Artifact Signing | Public Trust для частных разработчиков ограничен США/Канадой; доступность для организаций шире. Для частного лица из РФ сейчас не подходит. [Условия](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart), [цены](https://azure.microsoft.com/en-us/products/artifact-signing). |
| Публичный удостоверяющий центр | До покупки подтвердить выдачу для страны/типа заявителя и автоматизацию. Certum опубликовал приостановку выдачи для организаций и граждан РФ/Беларуси. [Сообщение Certum](https://www.certum.eu/en/news/change-in-the-rules-of-providing-trust-services-and-their-sale-on-the-territory-of-the-russian-federation-and-the-republic-of-belarus/). |

Самоподписанный сертификат сам по себе не создаёт публичного доверия. Не устанавливать корневые сертификаты или исключения защиты автоматически.

## Процесс после выбора провайдера — план

1. Подписывать код и создаваемые launchers через интеграцию Velopack: Setup/Update меняются при упаковке. Отдельно подписывать готовый OfflineSetup.
2. Authenticode SHA-256 и метка времени, защищённое хранилище ключа. Доступ к подписи не выдаётся внешним PR; приватный ключ не хранится в Git.
3. Проверять доверие, издателя и покрытие исполняемых файлов. Создавать ZIP, хеши и feeds из окончательных подписанных файлов. Для исправления опубликованного пакета выпускать новую версию.
4. Проверять Windows 11 с действующей политикой, MDB и обновление. Подпись не гарантирует отсутствие SmartScreen-предупреждений или разрешение корпоративной политики.

Этот раздел — план, не отчёт о выполненной подписи. `alpha.2` остаётся без подписи. [Velopack: подписание](https://docs.velopack.io/packaging/signing), [Microsoft: Smart App Control](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control).
