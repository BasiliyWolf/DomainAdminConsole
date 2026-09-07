# WinRM hotfix 0.3.1

Исправления затрагивают подключение к PowerShell Remoting под текущей доменной учётной записью.

## Что было не так

В 0.3.0 `WSManConnectionInfo.AuthenticationMechanism` всегда задавался как `Negotiate`, даже когда приложение не передавало `PSCredential` и рассчитывало на текущую Windows-учётную запись. В PowerShell SDK для такого сценария предусмотрен `NegotiateWithImplicitCredential`.

Кроме того, `ExecuteOneShotTextAsync` создавал linked CancellationTokenSource и вызывал `CancelAfter(timeoutMs)`. Если первая попытка подключения занимала таймаут, перед проверкой следующего endpoint генерировался `OperationCanceledException`.

## Что изменено

- Текущая учётная запись Windows: `AuthenticationMechanism.NegotiateWithImplicitCredential`.
- Явно переданный `PSCredential`: `AuthenticationMechanism.Negotiate`.
- Внутренний `CancelAfter` убран; используются `OpenTimeout` и `OperationTimeout` WSMan.
- Перед подключением быстро проверяются TCP 5985/5986.
- Ошибка теперь показывает причину отдельно для HTTP/5985 или HTTPS/5986.

## Быстрая проверка Windows

Запустить Windows PowerShell 5.1 от администратора:

```powershell
Test-NetConnection $env:COMPUTERNAME -Port 5985
Test-WSMan $env:COMPUTERNAME
New-PSSession -ComputerName $env:COMPUTERNAME
Invoke-Command -ComputerName $env:COMPUTERNAME -ScriptBlock { hostname; whoami }
```

Для FQDN:

```powershell
$fqdn = "$env:COMPUTERNAME.$env:USERDNSDOMAIN"
Test-WSMan $fqdn
Invoke-Command -ComputerName $fqdn -ScriptBlock { hostname; whoami }
```
