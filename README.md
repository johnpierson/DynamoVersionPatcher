# DynamoVersionPatcher

A small command-line utility that upgrades the **DynamoCore runtime** bundled with Autodesk products to version **3.6.2** — no reinstall, no waiting for a service pack.

Supported hosts:
- **Autodesk Revit 2026**
- **Autodesk Civil 3D 2026**

> **Use at your own risk.** This tool modifies files inside your Autodesk installation. It is unofficial and unaffiliated with Autodesk or the Dynamo team. A backup is taken automatically before any changes are made.

## Why this exists

Autodesk ships a specific version of DynamoCore with each product release, and it doesn't always keep pace with the latest fixes and improvements coming out of the Dynamo open-source project. This tool lets you apply a newer runtime yourself — immediately, on your own terms.

It was built to answer the question "what if" and shared here in the hope that others find it useful too.

## What it does

1. Checks that you are running as Administrator and that the target application is closed.
2. Backs up your current Dynamo installation to `Documents\Dynamo<Host>_Backup`.
3. Downloads the official `DynamoCoreRuntime3.6.2` zip from the [DynamoDS GitHub releases page](https://github.com/DynamoDS/Dynamo/releases).
4. Extracts the runtime into the Dynamo directory for your chosen host.
5. Verifies the installed version matches the expected target.

Host-specific bridge files (e.g. `DynamoRevitDS.dll`) are left untouched — only the core runtime is updated.

## Requirements

- Windows 10/11 (64-bit)
- Autodesk Revit **2026** or Civil 3D **2026** with Dynamo installed
- Administrator privileges
- An internet connection (or a pre-downloaded zip — see `--zip-path`)

## Quick start

1. **Close Revit or Civil 3D** completely.
2. Download `DynamoCoreUpdate.exe` from the [Releases](../../releases) page.
3. Right-click the `.exe` and choose **Run as administrator**.
4. Select your host application from the menu and follow the on-screen prompts.

The tool will download the runtime (~50 MB), extract it, and confirm the new version.

### Windows SmartScreen

You'll likely see a SmartScreen prompt the first time you run it — this is normal for any unsigned `.exe` from the internet. Click **More info → Run anyway** to proceed. If you'd rather not bypass it, feel free to [build from source](#building-from-source) and review the code yourself.

## Options

Run from an administrator command prompt for more control:

```
DynamoCoreUpdate.exe [--host <revit|civil3d>] [--install-dir <path>] [--zip-path <file>] [--backup-dir <path>] [--no-backup] [--force]
```

| Flag | Description |
|------|-------------|
| `--host <revit\|civil3d>` | Target host application. If omitted, an interactive menu is shown |
| `--install-dir <path>` | Path to the Dynamo folder. Defaults to the standard location for the selected host |
| `--zip-path <file>` | Use a locally downloaded zip instead of downloading from GitHub |
| `--backup-dir <path>` | Override the default backup location |
| `--no-backup` | Skip the backup step entirely |
| `--force` | Re-install even if already on the target version |

### Default install directories

| Host | Default path |
|------|-------------|
| Revit 2026 | `C:\Program Files\Autodesk\Revit 2026\AddIns\DynamoForRevit` |
| Civil 3D 2026 | `C:\Program Files\Autodesk\AutoCAD 2026\C3D\Dynamo\Core` |

### Examples

```
# Interactive host selection
DynamoCoreUpdate.exe

# Target Revit directly (no menu)
DynamoCoreUpdate.exe --host revit

# Target Civil 3D directly (no menu)
DynamoCoreUpdate.exe --host civil3d

# Use a local zip (useful on air-gapped machines)
DynamoCoreUpdate.exe --host revit --zip-path "D:\Downloads\DynamoCoreRuntime3.6.2.11575.zip"

# Non-standard install location
DynamoCoreUpdate.exe --host civil3d --install-dir "D:\Autodesk\Civil3D 2026\Dynamo"
```

## Restoring from backup

Only the files that the zip would overwrite are backed up. A timestamp is appended to the folder name on each run (e.g. `DynamoForRevit_Backup_20250401_143022`) so reruns never conflict.

To restore, copy the backup folder contents back over the install directory using Windows Explorer, or via the command line:

```
xcopy /E /H /Y "%USERPROFILE%\Documents\DynamoForRevit_Backup_<timestamp>\*" "C:\Program Files\Autodesk\Revit 2026\AddIns\DynamoForRevit\"
```

## Notes

- Targets 2026 releases by default. Other versions may work with `--install-dir` but haven't been tested.
- This tool operates independently of Autodesk's update mechanism — a product repair or update may revert the patched files.

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

Feedback, bug reports, and pull requests are all welcome — especially for supporting other Autodesk versions. [Open an issue](../../issues) to get the conversation started.

## License

MIT — see [LICENSE](LICENSE).

---

*This project was created independently in the author's personal time and is entirely separate from any official development by the Dynamo team or Autodesk. It is not affiliated with, endorsed by, or supported by Autodesk, Inc. in any way. Dynamo, Revit, and Civil 3D are trademarks of Autodesk, Inc.*
