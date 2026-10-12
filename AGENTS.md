# DesktopHelper development

- Preserve the user's desktop layout and original shortcut targets. Never delete target files when removing launcher entries.
- Default behavior is a desktop-level region, not an always-on-top overlay. Region size is manual; group and icon layout wraps inside it.
- Never reparent WPF into Explorer. Version 0.1.0 caused real desktop rendering problems despite passing HWND checks; use an independent layered window and manage only our own z-order.
- Freeze the installed/running version before asking the user to inspect it. Do not continue edits or installations while waiting for their answer.
- Keep Windows rendering checks separate from model tests. WPF renders do not prove the final desktop compositor or real mouse input.
- Use isolated `--self-test` / `--integration-test` data. The latter requires an interactive Windows desktop and launches Calculator.
- Build to `artifacts/`; exclude binaries, local layouts, logs and personal screenshots from Git.
- Use `scripts/install.ps1` for the stable local installation and verify installed hashes and the running path.
- Before committing: review scoped changes, run appropriate tests and `git diff --check`. Commit subjects should contain the version and a concrete Chinese summary. Push only when requested.
