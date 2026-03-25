# DynamoRevitPatcher

A small command-line utility that upgrades the **DynamoCore runtime** bundled with Revit 2026 to version **3.6.2** — no Revit reinstall, no waiting for a service pack.

> **Use at your own risk.** This tool modifies files inside your Revit installation. It is unofficial and unaffiliated with Autodesk or the Dynamo team. A backup is taken automatically before any changes are made.

## Why this exists

Autodesk ships a specific version of DynamoCore with each Revit release, and it doesn't always keep pace with the latest fixes and improvements coming out of the Dynamo open-source project. This tool lets you apply a newer runtime yourself — immediately, on your own terms.

It was built to answer the question "what if" and shared here in the hope that others find it useful too.

## What it does

1. Checks that you are running as Administrator and that Revit is closed.
2. Backs up your current DynamoForRevit installation to `Documents\DynamoForRevit_Backup`.
3. Downloads the official `DynamoCoreRuntime3.6.2` zip from the [DynamoDS GitHub releases page](https://github.com/DynamoDS/Dynamo/releases).
4. Extracts the runtime into your Revit 2026 DynamoForRevit directory.
5. Verifies the installed version matches the expected target.

The Revit-specific Dynamo bridge files (e.g. `DynamoRevitDS.dll`) are left untouched — only the core runtime is updated.

## Requirements

- Windows 10/11 (64-bit)
- Autodesk Revit **2026** with DynamoForRevit installed
- Administrator privileges
- An internet connection (or a pre-downloaded zip — see `--zip-path`)

## Quick start

1. **Close Revit** completely.
2. Download `DynamoCoreUpdate.exe` from the [Releases](../../releases) page.
3. Right-click the `.exe` and choose **Run as administrator**.
4. Follow the on-screen prompts.

The tool will download the runtime (~50 MB), extract it, and confirm the new version.

### Windows SmartScreen

You'll likely see a SmartScreen prompt the first time you run it — this is normal for any unsigned `.exe` from the internet. Click **More info → Run anyway** to proceed. If you'd rather not bypass it, feel free to [build from source](#building-from-source) and review the code yourself.

## Options

Run from an administrator command prompt for more control:

```
DynamoCoreUpdate.exe [--install-dir <path>] [--zip-path <file>] [--backup-dir <path>] [--no-backup] [--force]
```

| Flag | Description |
|------|-------------|
| `--install-dir <path>` | Path to the DynamoForRevit folder. Defaults to `C:\Program Files\Autodesk\Revit 2026\AddIns\DynamoForRevit` |
| `--zip-path <file>` | Use a locally downloaded zip instead of downloading from GitHub |
| `--backup-dir <path>` | Override the default backup location (`Documents\DynamoForRevit_Backup`) |
| `--no-backup` | Skip the backup step entirely |
| `--force` | Re-install even if already on the target version |

### Examples

```
# Default run — backs up to Documents\DynamoForRevit_Backup automatically
DynamoCoreUpdate.exe

# Override the backup location
DynamoCoreUpdate.exe --backup-dir "C:\DynamoBackup"

# Skip the backup
DynamoCoreUpdate.exe --no-backup

# Use a local zip (useful on air-gapped machines)
DynamoCoreUpdate.exe --zip-path "D:\Downloads\DynamoCoreRuntime3.6.2.11575.zip"

# Non-standard Revit install location
DynamoCoreUpdate.exe --install-dir "D:\Autodesk\Revit 2026\AddIns\DynamoForRevit"
```

## Restoring from backup

The backup is saved to `Documents\DynamoForRevit_Backup` by default. To restore:

```
xcopy /E /H /Y "%USERPROFILE%\Documents\DynamoForRevit_Backup\*" "C:\Program Files\Autodesk\Revit 2026\AddIns\DynamoForRevit\"
```

Or just copy the backup folder contents back using Windows Explorer.

## Notes

- Targets Revit 2026 by default. Other versions may work with `--install-dir` but haven't been tested.
- This tool operates independently of Autodesk's update mechanism — a Revit repair or update may revert the patched files.

## Building from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

```bash
# Standard build
dotnet build src/DynamoCoreUpdate.csproj -c Release

# Single self-contained .exe
dotnet publish src/DynamoCoreUpdate.csproj -c Release /p:PublishSingleFile=true
```

Output lands in `src/bin/Release/net9.0-windows/win-x64/`.

## Contributing / Issues

Feedback, bug reports, and pull requests are all welcome — especially for supporting other Revit versions. [Open an issue](../../issues) to get the conversation started.

## License

MIT — see [LICENSE](LICENSE).

---

*This project was created independently in the author's personal time and is entirely separate from any official development by the Dynamo team or Autodesk. It is not affiliated with, endorsed by, or supported by Autodesk, Inc. in any way. Dynamo and Revit are trademarks of Autodesk, Inc.*
