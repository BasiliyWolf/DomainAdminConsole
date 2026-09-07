# Domain Admin Console v0.3.0

Крупное функциональное обновление инструмента удалённого администрирования доменных Windows-компьютеров.

## Новое

- Массовые действия по выбранным компьютерам, активным машинам, всему AD или группам избранного.
- Параллельное выполнение с отдельным результатом по каждому ПК.
- Двухпанельный файловый менеджер локальный ↔ удалённый ПК.
- Wake-on-LAN и запоминание известных MAC-адресов.
- BitLocker / TPM diagnostics и безопасные Suspend/Resume операции.
- Принтеры и очередь печати.
- LocalMachine certificates и экспорт публичной части сертификата.
- Windows Firewall profiles/rules и базовое управление правилами.
- SMB sessions / SMB open files.
- Device Manager / PnP devices / driver information.
- Библиотека сохранённых PowerShell-скриптов с запуском на текущем ПК и передачей в массовые операции.

## Безопасность операций

- Перезагрузка/выключение в массовом режиме требуют подтверждения.
- Удаление файлов, firewall rules, SMB sessions/open files и отключение устройств требуют подтверждения.
- TPM Clear не реализован.
- Автоматическое расшифрование BitLocker не реализовано.
- Private keys сертификатов не экспортируются.
- Полный текст произвольных PowerShell-скриптов не попадает в audit log.

## Требования

- Windows 10/11 или Windows Server;
- .NET 8;
- Active Directory;
- WinRM/PowerShell Remoting;
- административные права для выполняемых операций;
- SMB administrative shares для файлового менеджера.

Сборка использует NuGet-пакеты `Microsoft.PowerShell.SDK` и `System.DirectoryServices`.
