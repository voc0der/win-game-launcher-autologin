# Changelog

All notable changes to this project are documented here.

## [0.1.3] - 2026-08-14

- Made configuration tests runnable without the Windows Desktop runtime.
- Fixed the Playnite Fullscreen launch deadlock by safely minimizing a verified Playnite foreground window before Ubisoft coordinate entry.
- Allowed element-scoped UI Automation submission without requiring foreground focus while preserving foreground checks for global input.
- Added bounded fill retries, immediate retry when Ubisoft is manually foregrounded, overlap prevention, and foreground-owner diagnostics.

## [0.1.2] - 2026-06-01

- Added a custom tray icon asset and README branding.
- Added unit tests for configuration defaults and safety normalization.
- Added test execution to the release workflow.

## [0.1.1] - 2026-06-01

- Fixed credential saving when the credentials dialog clears the password textbox during cleanup.

## [0.1.0] - 2026-06-01

- Initial tray app with passive WinEvent hooks for Ubisoft Connect windows.
- Added Windows Credential Manager storage for Ubisoft username and password.
- Added UI Automation password filling with guarded coordinate fallback.
- Added AppData JSON configuration and non-secret diagnostics logging.
