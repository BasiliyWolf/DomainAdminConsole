# Domain Admin Console v0.3.1

Patch release for WinRM reliability.

- Fixed implicit domain authentication for WinRM (`NegotiateWithImplicitCredential`).
- Removed false `OperationCanceledException` caused by the internal one-shot timeout CTS.
- Added WinRM TCP endpoint probing before Runspace creation.
- Improved HTTP 5985 / HTTPS 5986 error diagnostics.
- Increased safe opening timeout for short domain probes.
- Includes the previous SplitContainer startup hotfix.
