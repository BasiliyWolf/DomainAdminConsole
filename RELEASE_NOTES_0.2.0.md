# Domain Admin Console 0.2.0

Крупное функциональное обновление административной консоли для Windows/Active Directory.

## Новое

- Избранные компьютеры и группы с хранением в `%LOCALAPPDATA%`.
- Избранные ПК в tray-меню.
- RDP Shadow с получением Session ID через Windows Terminal Services API.
- Удалённый реестр с навигацией по подразделам и редактированием значений.
- Управление Scheduled Tasks.
- Просмотр установленных x64/x86 программ и подтверждаемое удаление MSI.
- Локальные пользователи и полный список прямых членов локальной Administrators.
- Управление членством в Administrators по SID, без зависимости от языка Windows.
- Сетевые интерфейсы, DNS, шлюзы и IPv4-маршруты.
- Windows Update: HotFix, службы, Pending Reboot, поиск ожидающих обновлений, StartScan/StartInstall.
- Локальный audit-log действий администратора.
- Контекстное меню для ПК из Active Directory.
- WinRM fallback HTTP 5985 → HTTPS 5986.
- Online-детект теперь учитывает Ping, WinRM, SMB/445 и RDP/3389.
- При доменном сканировании WinRM-доступных машин определяется текущий пользователь для быстрого фильтра.
- Поиск пользователя по домену получил WTS/RDP fallback, когда WinRM недоступен.
- Приложение запускается с `requireAdministrator`.
- Добавлен GitHub Actions workflow для Windows build/publish.

## Совместимость

- Visual Studio 2022
- .NET 8
- Windows 10/11
- Windows Server 2016+ для основных административных функций
- Active Directory Domain Services
