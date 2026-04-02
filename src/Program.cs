using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Principal;
using System.Xml.Linq;

// ── host configuration ────────────────────────────────────────────────────────

        BackupFolderName:     "DynamoForRevit_Backup",
HostConfig[] knownHosts = DiscoverHosts();

if (knownHosts.Length == 0)
{
    Console.Title = "DynamoVersionPatcher";
    WriteHeader();
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("ERROR: No supported Dynamo installations found under C:\\Program Files\\Autodesk.");
    Console.WriteLine("       Ensure Revit or Civil 3D is installed with the Dynamo for Revit add-in.");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
    return 1;
}

// ── argument parsing ──────────────────────────────────────────────────────────

string? hostKey           = null;
string? versionArg        = null;
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
        case "--version"     when i + 1 < args.Length: versionArg        = args[++i]; break;
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

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("User-Agent", "DynamoVersionPatcher/1.0");

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

// ── acquire zip ───────────────────────────────────────────────────────────────

string  localZip;
string? targetVersion;
string? downloadUrl;

if (zipPath is not null)
{
    Step("Using provided zip");
    if (!File.Exists(zipPath))
        Abort($"Zip file not found: {zipPath}");
    localZip      = zipPath;
    targetVersion = versionArg;  // null unless --version also supplied; version checks are skipped
    downloadUrl   = null;
    Ok(Path.GetFileName(zipPath));
}
else
{
    var build = await FetchAndPickBuildAsync(http, versionArg);
    targetVersion = build.Version;
    downloadUrl   = build.DownloadUrl;

    if (currentVersion == targetVersion && !force)
    {
        Warn($"DynamoCore.dll is already at version {targetVersion}. Use --force to reinstall.");
        Pause();
        return 0;
    }

    Step($"Downloading DynamoCoreRuntime {targetVersion}");
    string tempDir = Path.Combine(Path.GetTempPath(), $"DynamoCoreRuntime_{targetVersion}");
    Directory.CreateDirectory(tempDir);
    localZip = Path.Combine(tempDir, $"DynamoCoreRuntime{targetVersion}.zip");

    try
    {
        await DownloadWithProgressAsync(http, downloadUrl!, localZip);
        Ok("Download complete");
    }
    catch (Exception ex)
    {
        Abort($"Download failed: {ex.Message}\n\n  Download manually from:\n  {downloadUrl}\n  then re-run with --zip-path <file>");
    }
}

// ── backup (only files the zip will overwrite) ────────────────────────────────

if (!string.IsNullOrEmpty(backupDir))
{
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

if (targetVersion is not null && newVersion != targetVersion)
    Abort($"Version mismatch after extraction.\n  Expected: {targetVersion}\n  Found:    {newVersion}");
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

static async Task<BuildInfo> FetchAndPickBuildAsync(HttpClient http, string? versionArg)
{
    Step("Fetching available Dynamo builds");

    // Same S3 bucket used by dynamobuilds.com
    const string s3Base = "https://downloads.dynamobuilds.com/";
    XNamespace   ns     = "http://s3.amazonaws.com/doc/2006-03-01/";

    var stable = new List<BuildInfo>();
    var daily  = new List<BuildInfo>();

    string? marker    = null;
    bool    truncated = true;

    while (truncated)
    {
        var reqUrl = $"{s3Base}?prefix=DynamoCoreRuntime&max-keys=1000";
        if (marker is not null) reqUrl += $"&marker={Uri.EscapeDataString(marker)}";

        string xml;
        try   { xml = await http.GetStringAsync(reqUrl); }
        catch (Exception ex) { Abort($"Failed to fetch build list: {ex.Message}"); return null!; }

        var doc = XDocument.Parse(xml);

        string? lastKey = null;
        foreach (var item in doc.Root!.Elements(ns + "Contents"))
        {
            var key     = item.Element(ns + "Key")!.Value;
            var lastMod = DateTime.Parse(item.Element(ns + "LastModified")!.Value,
                              null, System.Globalization.DateTimeStyles.RoundtripKind);
            var size    = long.Parse(item.Element(ns + "Size")!.Value);
            lastKey = key;

            if (!key.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

            // Daily builds use underscores:  DynamoCoreRuntime_4.1.0.4550_20260401T0353.zip
            // Stable builds don't:            DynamoCoreRuntime3.6.2.11575.zip
            bool   isDaily    = key.StartsWith("DynamoCoreRuntime_", StringComparison.OrdinalIgnoreCase);
            string prefix     = isDaily ? "DynamoCoreRuntime_" : "DynamoCoreRuntime";
            string versionStr = key[prefix.Length..^".zip".Length];

            // Skip entries that don't start with a digit after the prefix
            if (versionStr.Length == 0 || !char.IsDigit(versionStr[0])) continue;

            var build = new BuildInfo(versionStr, s3Base + key, lastMod, size, isDaily);
            if (isDaily) daily.Add(build);
            else         stable.Add(build);
        }

        truncated = doc.Root!.Element(ns + "IsTruncated")?.Value == "true";
        marker    = lastKey;
    }

    // Sort: stable by parsed version descending, daily by date descending
    stable.Sort((a, b) => ParseVersion(b.Version).CompareTo(ParseVersion(a.Version)));
    daily.Sort((a, b) => b.PublishedAt.CompareTo(a.PublishedAt));

    if (stable.Count == 0 && daily.Count == 0)
        Abort("No DynamoCoreRuntime builds found in the build repository.");

    Ok($"Found {stable.Count} stable, {daily.Count} daily build(s)");

    // --version handling
    if (versionArg is not null)
    {
        if (versionArg.Equals("latest", StringComparison.OrdinalIgnoreCase))
            return stable.FirstOrDefault() ?? daily.First();

        var all   = stable.Concat(daily).ToList();
        var match = all.FirstOrDefault(b =>
            b.Version.StartsWith(versionArg, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            Abort($"Version '{versionArg}' not found. Use --version latest for the newest stable release.");
        return match!;
    }

    return PickBuild(stable, daily);
}

static Version ParseVersion(string v)
{
    // Strip timestamp suffix on daily builds (e.g. "4.1.0.4550_20260401T0353" → "4.1.0.4550")
    var clean = v.Split('_')[0];
    return System.Version.TryParse(clean, out var parsed) ? parsed : new System.Version(0, 0);
}

static BuildInfo PickBuild(List<BuildInfo> stable, List<BuildInfo> daily)
{
    bool showDaily = false;

    while (true)
    {
        var    current = showDaily ? daily : stable;
        string label   = showDaily ? "Daily builds" : "Stable releases";

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("==> ");
        Console.ResetColor();
        Console.WriteLine($"Select Dynamo runtime  ({label})");
        Console.WriteLine();

        int display = Math.Min(current.Count, 9);
        for (int i = 0; i < display; i++)
        {
            var b = current[i];
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"  [{i + 1}]");
            Console.ResetColor();
            Console.Write($"  {b.Version,-26}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  {b.PublishedAt:yyyy-MM-dd}");
            Console.Write($"  ({b.Size / 1_048_576} MB)");
            Console.ResetColor();
            Console.WriteLine();
        }

        if (current.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  (none available)");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(showDaily ? "  [S]  stable releases" : "  [D]  daily builds");
        Console.ResetColor();
        Console.WriteLine();
        Console.Write("  Enter selection: ");

        bool redraw = false;
        while (!redraw)
        {
            var  key = Console.ReadKey(intercept: true);
            char c   = char.ToUpperInvariant(key.KeyChar);

            if (c == 'D' && !showDaily || c == 'S' && showDaily)
            {
                Console.WriteLine();
                showDaily = !showDaily;
                redraw = true;
            }
            else if (int.TryParse(key.KeyChar.ToString(), out int choice) && choice >= 1 && choice <= display)
            {
                Console.WriteLine(key.KeyChar);
                var selected = current[choice - 1];
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  [OK] ");
                Console.ResetColor();
                Console.WriteLine($"Selected: DynamoCoreRuntime {selected.Version}");
                return selected;
            }
        }
    }
}

static HostConfig[] DiscoverHosts()
{
    const string autodesk = @"C:\Program Files\Autodesk";
    var hosts = new List<HostConfig>();

    if (!Directory.Exists(autodesk))
        return hosts.ToArray();

    foreach (var dir in Directory.GetDirectories(autodesk).OrderBy(d => d))
    {
        var folderName = Path.GetFileName(dir);

        // Revit YYYY  →  ...\Revit 2026\AddIns\DynamoForRevit
        if (folderName.StartsWith("Revit ", StringComparison.OrdinalIgnoreCase))
        {
            var year   = folderName["Revit ".Length..].Trim();
            var dynDir = Path.Combine(dir, @"AddIns\DynamoForRevit");
            if (Directory.Exists(dynDir))
            {
                hosts.Add(new HostConfig(
                    Key:                  $"revit-{year}",
                    DisplayName:          $"Autodesk Revit {year}",
                    DefaultInstallDir:    dynDir,
                    ProcessName:          "Revit",
                    BackupFolderName:     $"DynamoForRevit_{year}_Backup",
                    BridgeDllPath:        @"Revit\DynamoRevitDS.dll",
                    BridgeDllDisplayName: "DynamoRevitDS.dll"));
            }
        }

        // AutoCAD YYYY (Civil 3D)  →  ...\AutoCAD 2026\C3D\Dynamo\Core
        else if (folderName.StartsWith("AutoCAD ", StringComparison.OrdinalIgnoreCase))
        {
            var year   = folderName["AutoCAD ".Length..].Trim();
            var dynDir = Path.Combine(dir, @"C3D\Dynamo\Core");
            if (Directory.Exists(dynDir))
            {
                hosts.Add(new HostConfig(
                    Key:                  $"civil3d-{year}",
                    DisplayName:          $"Autodesk Civil 3D {year}",
                    DefaultInstallDir:    dynDir,
                    ProcessName:          "acad",
                    BackupFolderName:     $"DynamoForCivil3D_{year}_Backup",
                    BridgeDllPath:        null,
                    BridgeDllDisplayName: null));
            }
        }
    }

    return hosts.ToArray();
}

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

static async Task DownloadWithProgressAsync(HttpClient client, string url, string dest)
{
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

record BuildInfo(
    string   Version,
    string   DownloadUrl,
    DateTime PublishedAt,
    long     Size,
    bool     IsDaily);
