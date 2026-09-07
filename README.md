# Domain Admin Console 0.3.4

WinForms-приложение на C# для централизованного удалённого администрирования Windows-компьютеров в Active Directory.

Проект рассчитан на Visual Studio 2022 / .NET 8. Основной транспорт управления — PowerShell Remoting / WinRM. Приложение запускается с повышением прав через UAC.

## Основные возможности

### Active Directory и активные ПК

- автоматическое определение текущего домена и `defaultNamingContext`;
- получение всех включённых объектов Computer из Active Directory;
- параллельная проверка доступности доменных ПК;
- независимые индикаторы `Ping`, `WinRM`, `SMB/445` и `RDP/3389`;
- WinRM HTTP/5985 с fallback на HTTPS/5986;
- определение IPv4;
- определение console/RDP-пользователей на WinRM-доступных машинах;
- фильтр `Только активные`;
- поиск по имени ПК, DNS, IP и пользователю;
- остановка длительного сканирования.

ПК считается активным, если отвечает хотя бы один из признаков: ICMP, WinRM, SMB/445 или RDP/3389. Поэтому запрет ICMP не приводит к автоматическому статусу Offline.

### Поиск пользователь ↔ ПК по всему домену

- `Пользователь → ПК` — поиск пользователя по всем компьютерам Active Directory;
- проверка console и RDP-сеансов;
- ограничение параллельных запросов;
- возможность остановить поиск;
- `ПК → пользователи` — просмотр пользователей конкретной машины;
- двойной клик по результату подключает ПК в основном интерфейсе.

### Удалённый PowerShell

- подключение по DNS или IP;
- reverse DNS для IP, чтобы по возможности использовать hostname/Kerberos;
- постоянный PowerShell Runspace;
- сохранение состояния сеанса между командами;
- вывод stdout и PowerShell errors.

### Базовые административные модули

- обзор ОС, CPU, RAM, uptime и дисков;
- процессы: просмотр, завершение, запуск процесса и запуск GUI-приложения в пользовательском сеансе;
- службы: Start / Stop / Restart;
- Event Logs: System / Application / Security;
- TCP LISTEN и UDP endpoints;
- Ping и Tracert локально либо с удалённого ПК;
- пользовательские/RDP-сеансы;
- сетевые интерфейсы, IPv4, шлюзы, DNS, MAC и маршруты;
- Windows Update / HotFix / pending reboot;
- локальные пользователи и локальные Administrators;
- Task Scheduler;
- установленные программы;
- удалённый реестр;
- RDP Shadow;
- избранные ПК и группы;
- audit log действий администратора.

## Новое и исправленное в 0.3.4

- Контекстные меню таблиц теперь содержат не только операции копирования, но и те же командные кнопки, которые расположены над соответствующей таблицей. Правый клик сначала выбирает строку, поэтому действия выполняются над выбранным объектом.
- Добавлен общий экран ожидания для удалённых WinRM-операций: при загрузке данных поверх рабочей области показываются сообщение `Загрузка данных...` и анимированный ProgressBar.
- На кнопки добавлены стандартные системные Windows-иконки. Иконки также используются в зеркальных пунктах контекстного меню.
- ProgressBar сканирования Active Directory и статистика проверки ПК перенесены из левой панели в нижний `StatusStrip`.
- В `StatusStrip` также показываются текущее подключение и имя домена.
- Левая панель стала компактнее: в ней остались фильтр, переключатель `Только активные`, кнопки обновления/остановки и список компьютеров.
- Контекстное меню списка доменных ПК дополнено командами `Обновить домен` и `Стоп`.
- Все возможности и исправления 0.3.3 сохранены.

## Новое и исправленное в 0.3.3

- Все таблицы используют сортировку по клику на заголовок и растягиваются на доступную ширину окна.
- Во всех таблицах есть контекстное меню: копирование ячейки, строки, всей таблицы и восстановление режима растягивания столбцов.
- При подключении/переподключении данные старого ПК очищаются. Текущая вкладка загружается сразу, остальные автоматически обновляются при первом переходе на них и при каждом последующем выборе. Кнопка `Обновить` сверху сбрасывает кэш вкладок и перечитывает данные текущего ПК.
- В списке Active Directory отображаются как online, так и offline ПК. Offline отмечаются красным, online — зелёным; Ping, WinRM, SMB и RDP показываются текстовыми цветными индикаторами.
- Добавлен ProgressBar сканирования домена и счётчики online/offline.
- Устранены дубли пользователей вида `DOMAIN\user, user`: учётки нормализуются по короткому имени с приоритетом доменного формата.
- В таблице портов добавлено текстовое описание известных служб (`DNS`, `LDAP`, `SMB`, `RDP`, `WinRM`, `SQL`, `PostgreSQL` и др.).
- Исправлена десериализация PowerShell `DateTime` в файловом менеджере, сертификатах и других JSON-таблицах; поддерживаются ISO 8601 и формат `/Date(...) /`.
- Исправлена кодировка вывода native Windows-команд. `systeminfo`, `ipconfig`, `ping`, `tracert`, `gpresult`, `driverquery`, `quser` и другие типовые EXE из PowerShell-консоли автоматически запускаются через OEM-aware обработчик.
- `Системная информация` больше не зависит от локализованного вывода `systeminfo.exe`: данные собираются через CIM/PowerShell и формируются Unicode-текстом.
- Отправка сообщения сначала использует WTS API, а при блокировке WTS/RPC автоматически пробует `msg.exe` на подключённом компьютере через WinRM.
- Сохранены исправления WinRM 0.3.1 и безопасной инициализации `SplitContainer`.

## Новое в 0.3.0

### Массовые действия

Отдельная вкладка позволяет загрузить:

- все ПК из AD;
- только активные ПК;
- сохранённую группу избранного.

Можно отметить нужные компьютеры и выполнить действие параллельно с настраиваемым ограничением одновременных запросов.

Встроенные действия:

- `gpupdate /force`;
- Restart Print Spooler;
- Flush DNS;
- Windows Update Scan;
- Restart Computer;
- Shutdown Computer;
- произвольная PowerShell-команда.

Для перезагрузки, выключения и произвольной команды требуется дополнительное подтверждение. По каждой цели выводится отдельный результат и пишется audit-запись.

### Двухпанельный файловый менеджер

Левая панель — локальная машина администратора, правая — подключённый удалённый ПК.

Поддерживается:

- просмотр файлов и каталогов;
- переход по каталогам двойным кликом;
- копирование локальный → удалённый ПК;
- копирование удалённый → локальный ПК;
- рекурсивное копирование каталогов;
- создание папок;
- переименование;
- удаление с подтверждением;
- открытие текущего каталога в Explorer.

Передача выполняется через административные SMB-шары (`C$`, `D$` и т. п.), а перечисление удалённого каталога — через WinRM.

### Wake-on-LAN

- сохранение известных MAC-адресов при просмотре сетевых интерфейсов ПК;
- ручной ввод MAC;
- выбор broadcast address;
- выбор UDP-порта;
- отправка стандартного Magic Packet.

База известных MAC хранится в:

```text
%LOCALAPPDATA%\DomainAdminConsole\known_macs.json
```

### BitLocker / TPM

BitLocker:

- список томов;
- состояние шифрования;
- ProtectionStatus;
- LockStatus;
- EncryptionMethod;
- EncryptionPercentage;
- Key Protector Types;
- `Suspend-BitLocker` на одну перезагрузку;
- `Resume-BitLocker`.

TPM:

- TpmPresent;
- TpmReady;
- TpmEnabled;
- TpmActivated;
- TpmOwned;
- RestartPending;
- Manufacturer / Version / SpecVersion.

Операции очистки TPM и автоматического отключения/расшифровки BitLocker намеренно не добавлены.

### Принтеры

- список установленных принтеров;
- драйвер;
- порт;
- ShareName;
- состояние;
- очередь печати;
- пользователь и документ;
- отмена выбранного задания;
- перезапуск Print Spooler.

### Сертификаты компьютера

Просмотр `Cert:\LocalMachine` для хранилищ:

- My;
- Root;
- CA;
- WebHosting;
- TrustedPeople.

Показываются Subject, Issuer, Thumbprint, срок действия, FriendlyName, DNS names и наличие private key.

Поддерживается экспорт **только публичной части** сертификата в `.cer`. Private key приложение не экспортирует.

### Windows Firewall

- состояния профилей Domain / Private / Public;
- список правил;
- фильтр;
- Direction / Action / Profile;
- Protocol / LocalPort / RemotePort;
- Program / Service;
- Enable / Disable выбранного правила;
- создание простого разрешающего Inbound TCP/UDP правила по порту;
- удаление правила с подтверждением.

### SMB Sessions / Open Files

- активные SMB-сессии;
- клиентский ПК;
- пользователь;
- SessionId;
- Dialect;
- Encryption;
- число открытых файлов;
- список открытых SMB-файлов;
- FileId / SessionId / Path / Locks;
- принудительное закрытие сессии;
- принудительное закрытие файла.

Закрытие SMB-сессии или файла требует подтверждения, потому что пользователь может потерять несохранённые данные.

### Устройства / драйверы

- устройства PnP;
- Status;
- Class;
- FriendlyName;
- InstanceId;
- Manufacturer;
- DriverProvider;
- DriverVersion;
- DriverDate;
- INF;
- фильтр устройств;
- Enable / Disable устройства;
- `pnputil /scan-devices`.

Перед Disable показывается предупреждение, поскольку отключение сетевого или дискового устройства может оборвать удалённое соединение.

### Библиотека PowerShell-скриптов

Локальная библиотека содержит:

- название;
- категорию;
- описание;
- текст PowerShell-скрипта;
- дату изменения.

Возможности:

- создать;
- изменить;
- удалить;
- запустить на текущем подключённом ПК;
- передать текст скрипта в `Массовые действия`.

Файл библиотеки:

```text
%LOCALAPPDATA%\DomainAdminConsole\scripts.json
```

В audit log сохраняется имя запускаемого скрипта, но не полный текст скрипта.

## Возможности 0.2.0, сохранённые в 0.3.0

### Избранные ПК и группы

- группы;
- примечания;
- добавление текущего/выбранного ПК;
- редактирование и удаление;
- двойной клик для подключения;
- RDP;
- быстрый доступ из tray.

```text
%LOCALAPPDATA%\DomainAdminConsole\favorites.json
```

### RDP Shadow

Сеансы получаются через Windows Terminal Services API (`wtsapi32.dll`), а не через парсинг локализованного `quser`.

Поддерживаются:

- Session ID;
- DOMAIN\User;
- WinStation;
- State;
- просмотр;
- `/control`;
- опциональный `/noConsentPrompt`.

Пример вручную:

```cmd
mstsc.exe /v:COMPUTER01 /shadow:2 /control
```

`/noConsentPrompt` не обходит GPO и права RDS.

### Удалённый реестр

- просмотр ключей/значений;
- создание ключей;
- String / ExpandString / MultiString / DWord / QWord / Binary;
- изменение и удаление значений.

Пример:

```text
HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion
```

Работа идёт через PowerShell Remoting, поэтому служба Remote Registry для этого модуля не обязательна.

### Task Scheduler

- список заданий;
- State / Author / TaskPath;
- Run / Stop;
- Enable / Disable.

### Установленные программы

- x64 и x86 Uninstall registry;
- имя, версия, издатель, дата установки, архитектура;
- фильтр;
- подтверждаемое удаление MSI по ProductCode.

### Локальные пользователи / Administrators

- список локальных учётных записей;
- Enabled / Disabled;
- включение/отключение;
- состав локальной группы Administrators;
- добавление/удаление пользователя.

Administrators определяется по SID `S-1-5-32-544`, поэтому название группы не зависит от языка Windows.

### Windows Update

- HotFix;
- `wuauserv`, `BITS`, `CryptSvc`, `UsoSvc`;
- Pending Reboot;
- поиск ожидающих обновлений через Windows Update Agent COM;
- `UsoClient StartScan`;
- `UsoClient StartInstall`.

Фактическая доступность действий зависит от версии Windows и GPO/WSUS.

### Audit Log

```text
%LOCALAPPDATA%\DomainAdminConsole\audit.jsonl
```

Записываются дата/время, ПК, действие, краткие детали и Success/Failure. Полный текст произвольной PowerShell-команды в audit log не сохраняется.

## Дополнительные утилиты

- RDP;
- `\\PC\C$`;
- Computer Management;
- Event Viewer;
- `gpupdate /force`;
- `ipconfig /flushdns`;
- `ipconfig /all`;
- `systeminfo`;
- reboot;
- `msg *`.

## Tray

При сворачивании приложение скрывается в область уведомлений.

Из tray доступны:

- открыть приложение;
- RDP к текущему ПК;
- Ping;
- `C$`;
- сохранённые группы/ПК;
- выход.

## Требования

- Windows 10/11 или Windows Server;
- Visual Studio 2022;
- .NET 8 SDK;
- доступ к `https://api.nuget.org/v3/index.json` во время Restore;
- доменная учётная запись с необходимыми административными правами;
- LDAP-доступ к Active Directory;
- WinRM на управляемых ПК;
- SMB admin shares для файлового менеджера;
- необходимые GPO/права для RDP Shadow.

## NuGet

Используются обычные NuGet-зависимости:

```xml
<PackageReference Include="Microsoft.PowerShell.SDK" Version="7.4.15" />
<PackageReference Include="System.DirectoryServices" Version="8.0.1" />
```


## Рекомендуемая настройка WinRM через GPO

На управляемых компьютерах рекомендуется включить:

1. `Windows Remote Management (WinRM) -> WinRM Service -> Allow remote server management through WinRM`.
2. Firewall rule `Windows Remote Management (HTTP-In)` для Domain profile.
3. Службу `Windows Remote Management (WS-Management)`.
4. Необходимые административные права для используемой учётной записи.

Проверка:

```powershell
Test-WSMan COMPUTER01
Enter-PSSession COMPUTER01
```

Для тестовой машины:

```powershell
Enable-PSRemoting -Force
```

## Сборка

Visual Studio 2022:

```text
DomainAdminConsole.sln
```

PowerShell:

```powershell
.\build.ps1
```

Publish win-x64 single-file framework-dependent:

```powershell
.\build.ps1 -Publish
```

Self-contained:

```powershell
.\build.ps1 -Publish -SelfContained
```

Результат:

```text
.\publish
```

Также в `.github/workflows/build.yml` присутствует GitHub Actions build для Windows.

## Структура проекта

```text
DomainAdminConsole/
├─ DomainAdminConsole.sln
├─ DomainAdminConsole.csproj
├─ Program.cs
├─ MainForm.cs
├─ MainForm.Advanced.cs
├─ MainForm.V030.Core.cs
├─ MainForm.V030.Modules.cs
├─ Models/
│  ├─ AdminModels.cs
│  ├─ AdvancedModels.cs
│  └─ V030Models.cs
├─ Services/
│  ├─ DomainService.cs
│  ├─ RemotePowerShellService.cs
│  ├─ RdpSessionService.cs
│  └─ AppDataStore.cs
├─ .github/workflows/build.yml
├─ app.manifest
├─ build.ps1
├─ README.md
├─ CHANGELOG.md
└─ RELEASE_NOTES_0.3.0.md
```

## Важное замечание о правах

Domain Admin Console не пытается обходить ACL, UAC, WinRM, SMB, RDP или доменные политики. Если функция недоступна, необходимо проверить права используемой учётной записи, WinRM/GPO, Windows Firewall и локальные политики целевой машины.
