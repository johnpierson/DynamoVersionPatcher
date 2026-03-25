using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Principal;

const string DownloadUrl  = "https://github.com/DynamoDS/Dynamo/releases/download/v3.6.2/DynamoCoreRuntime3.6.2.11575.zip";
const string TargetVersion = "3.6.2.11575";
const string DefaultInstallDir = @"C:\Program Files\Autodesk\Revit 2026\AddIns\DynamoForRevit";

// ── argument parsing ──────────────────────────────────────────────────────────

string installDir = DefaultInstallDir;
string? zipPath   = null;
string  backupDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "DynamoForRevit_Backup");
bool    force     = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--zip-path"    when i + 1 < args.Length: zipPath    = args[++i]; break;
        case "--install-dir" when i + 1 < args.Length: installDir = args[++i]; break;
        case "--backup-dir"  when i + 1 < args.Length: backupDir  = args[++i]; break;
        case "--no-backup":                              backupDir  = "";        break;
        case "--force":                                  force      = true;      break;
    }
}

// ── header ────────────────────────────────────────────────────────────────────

Console.Title = "DynamoCore 3.6.2 Update for Revit 2026";
WriteHeader();

// ── prerequisites ─────────────────────────────────────────────────────────────

Step("Checking prerequisites");

if (!IsAdministrator())
    Abort("This tool must be run as Administrator. Right-click and choose 'Run as administrator'.");
Ok("Running as Administrator");

var revitProcs = Process.GetProcessesByName("Revit");
if (revitProcs.Length > 0)
    Abort("Revit is currently running. Close Revit before running this installer.");
Ok("Revit is not running");

if (!Directory.Exists(installDir))
    Abort($"DynamoForRevit directory not found:\n  {installDir}\n  Ensure Revit 2026 with DynamoForRevit is installed.");
Ok($"DynamoForRevit found");

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

// ── backup ────────────────────────────────────────────────────────────────────

if (!string.IsNullOrEmpty(backupDir))
{
    Step($"Backing up existing installation");
    Console.WriteLine($"  → {backupDir}");
    if (Directory.Exists(backupDir))
        Abort($"Backup directory already exists: {backupDir}\n  Delete it or specify a different path with --backup-dir, or skip backup with --no-backup.");
    CopyDirectory(installDir, backupDir);
    Ok("Backup complete");
}
else
{
    Warn("Skipping backup (--no-backup specified)");
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

        // Rewrite progress on the same line
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

string revitDll = Path.Combine(installDir, @"Revit\DynamoRevitDS.dll");
if (File.Exists(revitDll))
    Ok($"DynamoRevitDS.dll:   {GetFileVersion(revitDll)} (preserved)");
else
    Warn("DynamoRevitDS.dll not found — Revit-specific files may need reinstalling.");

// ── done ──────────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"  Installation complete!");
Console.WriteLine($"  DynamoCore upgraded: {currentVersion} → {newVersion}");
Console.ResetColor();

Pause();
return 0;

// ── helpers ───────────────────────────────────────────────────────────────────

static bool IsAdministrator() =>
    new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);

static string? GetFileVersion(string path) =>
    File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion : null;

static void CopyDirectory(string src, string dst)
{
    Directory.CreateDirectory(dst);
    foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
    {
        string rel  = Path.GetRelativePath(src, file);
        string dest = Path.Combine(dst, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(file, dest, overwrite: true);
    }
}

static async Task DownloadWithProgressAsync(string url, string dest)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Add("User-Agent", "DynamoCoreUpdate/3.6.2");

    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();

    long? total = response.Content.Headers.ContentLength;
    using var src    = await response.Content.ReadAsStreamAsync();
    using var output = File.Create(dest);

    var buffer    = new byte[81920];
    long received = 0;
    int  read;

    while ((read = await src.ReadAsync(buffer)) > 0)
    {
        await output.WriteAsync(buffer.AsMemory(0, read));
        received += read;

        if (total.HasValue)
        {
            int pct = (int)(received * 100 / total.Value);
            int bar = pct / 4; // 25-char bar
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
    Console.WriteLine("DynamoCore 3.6.2 Update for Revit 2026");
    Console.WriteLine("=======================================");
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
