using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Principal;

// ── host configuration ────────────────────────────────────────────────────────

HostConfig[] knownHosts =
[
    new HostConfig(
        Key:                  "revit",
        DisplayName:          "Autodesk Revit 2026",
        DefaultInstallDir:    @"C:\Program Files\Autodesk\Revit 2026\AddIns\DynamoForRevit",
        ProcessName:          "Revit",
        BackupFolderName:     "DynamoForRevit_Backup",
        BridgeDllPath:        @"Revit\DynamoRevitDS.dll",
        BridgeDllDisplayName: "DynamoRevitDS.dll"),

    new HostConfig(
        Key:                  "civil3d",
        DisplayName:          "Autodesk Civil 3D 2026",
        DefaultInstallDir:    @"C:\Program Files\Autodesk\AutoCAD 2026\C3D\Dynamo\Core",
        ProcessName:          "acad",
        BackupFolderName:     "DynamoForCivil3D_Backup",
        BridgeDllPath:        null,
        BridgeDllDisplayName: null),
];

const string DownloadUrl   = "https://github.com/DynamoDS/Dynamo/releases/download/v3.6.2/DynamoCoreRuntime3.6.2.11575.zip";
const string TargetVersion = "3.6.2.11575";

// ── argument parsing ──────────────────────────────────────────────────────────

string? hostKey           = null;
string? installDirArg     = null;
string? zipPath           = null;
string? backupDirOverride = null;
bool    noBackup          = false;
bool    force             = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--host"        when i + 1 < args.Length: hostKey           = args[++i]; break;
        case "--zip-path"    when i + 1 < args.Length: zipPath           = args[++i]; break;
        case "--install-dir" when i + 1 < args.Length: installDirArg     = args[++i]; break;
        case "--backup-dir"  when i + 1 < args.Length: backupDirOverride = args[++i]; break;
        case "--no-backup":                              noBackup          = true;      break;
        case "--force":                                  force             = true;      break;
    }
}

// ── header & host selection ───────────────────────────────────────────────────

Console.Title = "DynamoVersionPatcher";
WriteHeader();

HostConfig host;

if (hostKey is not null)
{
    var matched = knownHosts.FirstOrDefault(h => h.Key.Equals(hostKey, StringComparison.OrdinalIgnoreCase));
    if (matched is null)
        Abort($"Unknown host '{hostKey}'. Valid values: {string.Join(", ", knownHosts.Select(h => h.Key))}");
    host = matched!;
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("==> ");
    Console.ResetColor();
    Console.WriteLine($"Host: {host.DisplayName}");
}
else
{
    host = PickHost(knownHosts);
}

Console.Title = $"DynamoVersionPatcher — {host.DisplayName}";

string installDir = installDirArg ?? host.DefaultInstallDir;
string backupDir  = noBackup ? "" :
    backupDirOverride ??
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), host.BackupFolderName);

// ── prerequisites ─────────────────────────────────────────────────────────────

Step("Checking prerequisites");

if (!IsAdministrator())
    Abort("This tool must be run as Administrator. Right-click and choose 'Run as administrator'.");
Ok("Running as Administrator");

var hostProcs = Process.GetProcessesByName(host.ProcessName);
if (hostProcs.Length > 0)
    Abort($"{host.DisplayName} is currently running. Close it before running this installer.");
Ok($"{host.DisplayName} is not running");

if (!Directory.Exists(installDir))
    Abort($"Dynamo directory not found:\n  {installDir}\n  Ensure {host.DisplayName} with Dynamo is installed, or use --install-dir to specify the path.");
Ok($"Dynamo directory found");

string coreDll        = Path.Combine(installDir, "DynamoCore.dll");
string currentVersion = GetFileVersion(coreDll)
    ?? Abort($"DynamoCore.dll not found in:\n  {installDir}");
Ok($"Current version: {currentVersion}");

if (currentVersion == TargetVersion && !force)
{
    Warn($"DynamoCore.dll is already at version {TargetVersion}. Use --force to reinstall.");
    Pause();
    return 0;
}

// ── acquire zip ───────────────────────────────────────────────────────────────

string localZip;

if (zipPath is not null)
{
    Step("Using provided zip");
    if (!File.Exists(zipPath))
        Abort($"Zip file not found: {zipPath}");
    localZip = zipPath;
    Ok(Path.GetFileName(zipPath));
}
else
{
    Step($"Downloading DynamoCoreRuntime {TargetVersion}");
    string tempDir = Path.Combine(Path.GetTempPath(), $"DynamoCoreRuntime_{TargetVersion}");
    Directory.CreateDirectory(tempDir);
    localZip = Path.Combine(tempDir, $"DynamoCoreRuntime{TargetVersion}.zip");

    try
    {
        await DownloadWithProgressAsync(DownloadUrl, localZip);
        Ok("Download complete");
    }
    catch (Exception ex)
    {
        Abort($"Download failed: {ex.Message}\n\n  Download manually from:\n  {DownloadUrl}\n  then re-run with --zip-path <file>");
    }
}

// ── backup (only files the zip will overwrite) ────────────────────────────────

if (!string.IsNullOrEmpty(backupDir))
{
    // Append timestamp so reruns never collide
    backupDir = $"{backupDir}_{DateTime.Now:yyyyMMdd_HHmmss}";

    Step("Backing up files to be replaced");
    Console.WriteLine($"  → {backupDir}");

    using var peekArchive = ZipFile.OpenRead(localZip);
    int backedUp = 0;
    foreach (var entry in peekArchive.Entries)
    {
        if (string.IsNullOrEmpty(entry.Name)) continue;
        string src = Path.Combine(installDir, entry.FullName);
        if (!File.Exists(src)) continue;

        string dst = Path.Combine(backupDir, entry.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, overwrite: true);
        backedUp++;
    }
    Ok($"Backed up {backedUp} file(s)");
}
else
{
    Warn("Skipping backup (--no-backup specified)");
}

// ── extract ───────────────────────────────────────────────────────────────────

Step("Extracting files");

using (var archive = ZipFile.OpenRead(localZip))
{
    int total  = archive.Entries.Count(e => !string.IsNullOrEmpty(e.Name));
    int copied = 0;

    foreach (var entry in archive.Entries)
    {
        if (string.IsNullOrEmpty(entry.Name)) continue;

        string dest    = Path.Combine(installDir, entry.FullName);
        string destDir = Path.GetDirectoryName(dest)!;
        if (!Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        entry.ExtractToFile(dest, overwrite: true);
        copied++;

        Console.Write($"\r  {copied,5}/{total} files");
    }
    Console.WriteLine();
    Ok($"Extracted {copied} files");
}

// Clean up temp download
if (zipPath is null && File.Exists(localZip))
{
    try { Directory.Delete(Path.GetDirectoryName(localZip)!, recursive: true); }
    catch { /* non-fatal */ }
}

// ── verify ────────────────────────────────────────────────────────────────────

Step("Verifying installation");

string newVersion = GetFileVersion(coreDll)
    ?? Abort("DynamoCore.dll missing after extraction — something went wrong.");

if (newVersion != TargetVersion)
    Abort($"Version mismatch after extraction.\n  Expected: {TargetVersion}\n  Found:    {newVersion}");
Ok($"DynamoCore.dll:      {newVersion}");

if (host.BridgeDllPath is not null)
{
    string bridgeDll = Path.Combine(installDir, host.BridgeDllPath);
    if (File.Exists(bridgeDll))
        Ok($"{host.BridgeDllDisplayName}:   {GetFileVersion(bridgeDll)} (preserved)");
    else
        Warn($"{host.BridgeDllDisplayName} not found — host-specific files may need reinstalling.");
}

// ── done ──────────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("  Installation complete!");
Console.WriteLine($"  DynamoCore upgraded: {currentVersion} → {newVersion}");
Console.ResetColor();

Pause();
return 0;

// ── helpers ───────────────────────────────────────────────────────────────────

static HostConfig PickHost(HostConfig[] hosts)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("==> ");
    Console.ResetColor();
    Console.WriteLine("Select host application");
    Console.WriteLine();

    for (int i = 0; i < hosts.Length; i++)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"  [{i + 1}]");
        Console.ResetColor();
        Console.WriteLine($"  {hosts[i].DisplayName}");
    }

    Console.WriteLine();
    Console.Write($"  Enter selection [1-{hosts.Length}]: ");

    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (int.TryParse(key.KeyChar.ToString(), out int choice) && choice >= 1 && choice <= hosts.Length)
        {
            Console.WriteLine(key.KeyChar);
            var selected = hosts[choice - 1];
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  [OK] ");
            Console.ResetColor();
            Console.WriteLine($"Selected: {selected.DisplayName}");
            return selected;
        }
    }
}

static bool IsAdministrator() =>
    new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);

static string? GetFileVersion(string path) =>
    File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion : null;


static async Task DownloadWithProgressAsync(string url, string dest)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Add("User-Agent", "DynamoVersionPatcher/3.6.2");

    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();

    long? total = response.Content.Headers.ContentLength;
    using var src    = await response.Content.ReadAsStreamAsync();
    using var output = File.Create(dest);

    var  buffer   = new byte[81920];
    long received = 0;
    int  read;

    while ((read = await src.ReadAsync(buffer)) > 0)
    {
        await output.WriteAsync(buffer.AsMemory(0, read));
        received += read;

        if (total.HasValue)
        {
            int pct = (int)(received * 100 / total.Value);
            int bar = pct / 4;
            Console.Write($"\r  [{new string('=', bar)}{new string(' ', 25 - bar)}] {pct,3}%  ({received / 1_048_576} MB / {total.Value / 1_048_576} MB)");
        }
        else
        {
            Console.Write($"\r  {received / 1_048_576} MB downloaded...");
        }
    }
    Console.WriteLine();
}

static void WriteHeader()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine();
    Console.WriteLine(@"  ██████╗ ██╗   ██╗███╗   ██╗ █████╗ ███╗   ███╗  ██████╗ ");
    Console.WriteLine(@"  ██╔══██╗╚██╗ ██╔╝████╗  ██║██╔══██╗████╗ ████║ ██╔═══██╗");
    Console.WriteLine(@"  ██║  ██║ ╚████╔╝ ██╔██╗ ██║███████║██╔████╔██║ ██║   ██║");
    Console.WriteLine(@"  ██║  ██║  ╚██╔╝  ██║╚██╗██║██╔══██║██║╚██╔╝██║ ██║   ██║");
    Console.WriteLine(@"  ██████╔╝   ██║   ██║ ╚████║██║  ██║██║ ╚═╝ ██║ ╚██████╔╝");
    Console.WriteLine(@"  ╚═════╝    ╚═╝   ╚═╝  ╚═══╝╚═╝  ╚═╝╚═╝     ╚═╝  ╚═════╝ ");
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine();
    Console.WriteLine(@"       ╔═══════════════════════════════════════════════╗");
    Console.WriteLine(@"       ║        V E R S I O N   P A T C H E R         ║");
    Console.WriteLine(@"       ║                    v 3 . 6 . 2               ║");
    Console.WriteLine(@"       ╚═══════════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine();
}

static void Step(string msg)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("==> ");
    Console.ResetColor();
    Console.WriteLine(msg);
}

static void Ok(string msg)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("  [OK] ");
    Console.ResetColor();
    Console.WriteLine(msg);
}

static void Warn(string msg)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write("  [WARN] ");
    Console.ResetColor();
    Console.WriteLine(msg);
}

static string Abort(string msg)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"ERROR: {msg}");
    Console.ResetColor();
    Pause();
    Environment.Exit(1);
    return null!; // unreachable
}

static void Pause()
{
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
}

record HostConfig(
    string Key,
    string DisplayName,
    string DefaultInstallDir,
    string ProcessName,
    string BackupFolderName,
    string? BridgeDllPath,
    string? BridgeDllDisplayName);
