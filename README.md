# Ubisoft Auto Login

Small Windows tray app that listens passively for Ubisoft Connect login windows and fills the saved Ubisoft password only when the foreground window is verified as Ubisoft-owned.

## Requirements

- Windows 10/11
- .NET 8 SDK or newer
- Ubisoft Connect installed

## Build

```powershell
dotnet restore
dotnet build -c Release
```

## Run

```powershell
dotnet run -c Release
```

On first run, the tray app prompts for username/email and password. They are stored in Windows Credential Manager under:

- `UbisoftAutoLogin:Username`
- `UbisoftAutoLogin:Password`

Non-secret settings are stored at:

```text
%AppData%\UbisoftAutoLogin\config.json
```

Logs are written to:

```text
%AppData%\UbisoftAutoLogin\logs\app.log
```

The log intentionally does not write credential values.

## Tray Menu

- `Set / Update Credentials`
- `Test Fill Current Ubisoft Window`
- `Exit`

## Config

Default `config.json`:

```json
{
  "PasswordBoxXPercent": 0.5,
  "PasswordBoxYPercent": 0.58,
  "DelayBeforeFillMs": 2500,
  "DebounceMs": 15000
}
```

Coordinate fallback only runs after the exact Ubisoft window is activated and re-verified as foreground. The app checks foreground ownership before every synthetic text character and before pressing Enter.

## Release Process

1. Update `VERSION` with the new SemVer value.
2. Add a matching `## [x.y.z] - YYYY-MM-DD` section to `CHANGELOG.md`.
3. Push to `main` or `master`.

GitHub Actions builds the app, creates a `vx.y.z` tag and GitHub release if that version has not already been released, uses the matching changelog section as the release notes, and attaches both the Windows zip and `CHANGELOG-vx.y.z.md`.
