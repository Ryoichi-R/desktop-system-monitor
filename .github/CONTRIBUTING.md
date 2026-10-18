# Contributing

Contributions are accepted through focused issues and pull requests after the
repository becomes public. Keep behavior changes separate from release and
documentation changes. Participation in project spaces is subject to the
[Code of Conduct](CODE_OF_CONDUCT.md).

```powershell
dotnet restore .\DesktopSystemMonitor.slnx
dotnet build .\DesktopSystemMonitor.slnx -c Release --no-restore
dotnet format .\DesktopSystemMonitor.slnx --verify-no-changes --severity error
pwsh .\scripts\test-desktop-system-monitor.ps1 -All
pwsh .\scripts\coverage-desktop-system-monitor.ps1
```

Do not commit local settings, logs, raw readiness evidence, build output, test
machine identifiers, or secrets. Hardware findings must distinguish unsupported
`N/A` from an incorrect measured value. By contributing, you agree that your
contribution is licensed under the repository's [MIT License](../LICENSE).
