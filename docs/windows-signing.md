# Подпись и диагностика Windows

## Наблюдения первого прогона

Собственные EXE публичной `2026.1.0-alpha.2` не имеют встроенной Authenticode-подписи. Установщики .NET подписаны Microsoft; это не подписывает Journal. SHA-256 и проверка обновления обеспечивают целостность пакета, но не доверие Windows к издателю.

На снимках Windows 11 две отдельные ситуации:

- Нет прав записи в Program Files. Текущий OfflineSetup использует `PrivilegesRequired=lowest` и пользовательскую установку Velopack. Системный режим с UAC/обновлением защищённой папки ещё не реализован. [Модель Velopack](https://docs.velopack.io/packaging/operating-systems/windows).
- Windows блокирует корневой launcher `Jornal.Desktop.exe`: CreateProcess 4551, сообщение о политике управления приложениями. До MDB процесс не дошёл. Конкретная политика уточняется по CodeIntegrity, наличие Касперского само по себе не устанавливает причину. [Microsoft: диагностика](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/appcontrol-debugging-and-troubleshooting), [Kaspersky: собственный контроль приложений и отчёт](https://support.kaspersky.com/help/KSC/15.1/en-US/184079.htm).

## Обновление до alpha.4: заблокирован AccessHost — 16.10.2026

По отчёту пользователя обновление через колокольчик установило `2026.1.0-alpha.4`; основной `Jornal.Desktop.exe` запущен, но Windows запрещает запуск `current\AccessHost\Jornal.AccessHost.exe`. В CodeIntegrity указаны события 3033/3077 и политика `VerifiedAndReputableDesktop`, GUID `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`. Microsoft относит эту политику к включённому Smart App Control; событие 3077 означает действующую блокировку. Это уточняет причину текущего отказа MDB-компонента; причину прежнего отказа launcher в alpha.2 отдельно не доказывает. [Встроенные политики Microsoft](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/inbox-appcontrol-policies), [события и поля CodeIntegrity](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/appcontrol-debugging-and-troubleshooting).

Выполненная локально сверка с опубликованной [alpha.4](https://github.com/DenisPapushaJava/journal-v2.0/releases/tag/v2026.1.0-alpha.4):

- SHA-256 `AccessHost/Jornal.AccessHost.exe` в ZIP и `lib/app/AccessHost/Jornal.AccessHost.exe` в пакете обновления совпадает с `SHA256 Flat` из отчёта: `23416c546ea6236f20bc97a18dc3ff73c429ea6221e1f1567df0c8bda20f23a4`.
- Размер EXE — 131 584 байта; таблица сертификатов PE пуста. Это согласуется с `Authenticode: NotSigned` на Windows.
- SHA-256 обоих архивов совпал с `digest` опубликованных GitHub Release assets. Следов изменения проверенного EXE относительно выпуска нет. Эта сверка не проверяет содержимое MDB или остальные файлы на компьютере пользователя.

`Requested Signing Level` — требование политики к запускаемому коду, а не запрос Journal на повышение прав. `SHA256` в событии — Authenticode-хеш, `SHA256 Flat` — хеш всего файла; их различие само по себе не указывает на повреждение. `OriginalFileName` берётся из ресурсов PE и может отличаться от имени заблокированного файла на диске.

По сообщению пользователя пробная самоподпись выполнялась только на временной копии; установленный EXE не менялся, временный сертификат удалён. Для Smart App Control нужна доверенная подпись или положительное решение службы репутации; самоподпись этого не обеспечивает. Microsoft не предусматривает обычного исключения для одного приложения в Smart App Control. Не смешивать это с разрешающими правилами отдельной корпоративной политики App Control. [Требования к подписи](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control), [FAQ Smart App Control](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions).

Повторная установка того же неподписанного EXE не устраняет установленную причину блокировки. Сам факт доставки обновления не подтверждает работоспособность MDB: для неё должен успешно запускаться AccessHost. Подписанный выпуск и проверка на этом ПК ещё предстоят. Текущая обработка ошибок запуска хоста выдаёт общее сообщение о восстановлении установки; отдельное понятное сообщение о блокировке Windows пока не реализовано.

## Диагностический отчёт

Скопируйте `scripts/Get-JournalDiagnostics.ps1` на проблемный ПК и выполните в обычном PowerShell, если запуск скриптов разрешён организацией:

```powershell
powershell.exe -NoProfile -File .\Get-JournalDiagnostics.ps1
```

Для нестандартной папки добавьте `-InstallDirectory "D:\Journal"`. JSON создаётся на рабочем столе; можно указать `-OutputPath` и `-HoursBack` (1–72, по умолчанию 2). Существующий файл не перезаписывается. Если политика запрещает скрипт, ИТ проверяет и запускает его разрешённым способом.

Отчёт содержит версию Windows, названия зарегистрированных в Windows защитных продуктов, SHA-256 и Authenticode EXE/DLL/PS1, состояние Smart App Control и относящиеся к Journal события CodeIntegrity. Скрипт не запускает Journal, не открывает MDB, не меняет сертификаты/политики и никуда не отправляет отчёт. Локальные пути и текст событий могут содержать персональные/корпоративные сведения — просмотрите отчёт перед передачей. Недоступный журнал или отсутствие событий не доказывают отсутствие блокировки.

Для ИТ важны событие 3077, PolicyName/PolicyID и точный файл. Проверяются корневой launcher, Update, код в `current`, AccessHost и библиотеки. Разрешение только хеша предыдущего выпуска может перестать действовать после обновления; правила издателя сопровождаются проще, если политика организации их допускает. [Правила App Control](https://learn.microsoft.com/en-us/windows/security/threat-protection/windows-defender-application-control/select-types-of-rules-to-create).

## Выбор сертификата — пока не выполнен

| Вариант | Условия |
| --- | --- |
| Организационный сертификат | ИТ выдаёт сертификат подписи кода и согласует доверие/правила на управляемых ПК. Это внутреннее доверие, не универсальное публичное. |
| SignPath Foundation | Бесплатно для принятых открытых проектов: OSS-лицензия, проверяемая сборка из исходников, политика подписи и ручное одобрение выпусков. Приём нового проекта/страны заявителя и совместимость с нашим Velopack ещё нужно подтвердить. Владелец выбрал MIT; лицензия добавлена в корень, подготовлена [Code signing policy](code-signing-policy.md). Заявка отправлена 16.10.2026, приём формы подтверждён страницей; решение сервиса и подключение услуги ещё предстоят. [Условия](https://signpath.org/terms.html), [заявка](https://signpath.org/apply.html). |
| Azure Artifact Signing | Public Trust для частных разработчиков ограничен США/Канадой; доступность для организаций шире. Для частного лица из РФ сейчас не подходит. [Условия](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart), [цены](https://azure.microsoft.com/en-us/products/artifact-signing). |
| Публичный удостоверяющий центр | До покупки подтвердить выдачу для страны/типа заявителя и автоматизацию. Certum опубликовал приостановку выдачи для организаций и граждан РФ/Беларуси. [Сообщение Certum](https://www.certum.eu/en/news/change-in-the-rules-of-providing-trust-services-and-their-sale-on-the-territory-of-the-russian-federation-and-the-republic-of-belarus/). |

Самоподписанный сертификат сам по себе не создаёт публичного доверия. Не устанавливать корневые сертификаты или исключения защиты автоматически.

## Процесс после выбора провайдера — план

1. Подписывать собственные EXE/DLL, включая `Jornal.AccessHost.exe`, и создаваемые launchers через интеграцию Velopack: Setup/Update меняются при упаковке. Отдельно подписывать готовый OfflineSetup. До подключения SignPath согласовать покрытие шаблонов/компонентов Velopack и ограничения на подпись чужих бинарных файлов; не подписывать все зависимости своим сертификатом без разбора.
2. Authenticode SHA-256 и метка времени, защищённое хранилище ключа. Доступ к подписи не выдаётся внешним PR; приватный ключ не хранится в Git.
3. Проверять доверие, издателя и покрытие исполняемых файлов. Создавать ZIP, хеши и feeds из окончательных подписанных файлов. Для исправления опубликованного пакета выпускать новую версию.
4. Проверять Windows 11 с действующей политикой, MDB и обновление. Подпись не гарантирует отсутствие SmartScreen-предупреждений или разрешение корпоративной политики.

Этот раздел — план, не отчёт о выполненной подписи. Опубликованные `alpha.2`–`alpha.4` остаются без подписи собственного кода. [Velopack: подписание](https://docs.velopack.io/packaging/signing), [Microsoft: Smart App Control](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control).
