using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EutherWire.Document.Model;
using EutherWire.Document.Serialization;

namespace EutherWire.Document.Synchronization;

public sealed record ProjectSnapshotInfo(
    string Path,
    int ProjectSchemaVersion,
    int FileCount,
    long TotalBytes);

public static class ProjectSnapshot
{
    public const string ManifestName = "snapshot.json";
    public const string FormatName = "eutherwire-portable-snapshot";
    public const int FormatVersion = 1;

    private const long MaximumFileBytes = 64L * 1024 * 1024;
    private const long MaximumSnapshotBytes = 256L * 1024 * 1024;
    private const int MaximumEntryCount = 4096;
    private static readonly DateTimeOffset StableZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static ProjectSnapshotInfo Export(string projectDirectory, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string projectRoot = Path.GetFullPath(projectDirectory);
        ProjectDocument document = ProjectToml.Load(projectRoot);
        string destination = Path.GetFullPath(outputPath);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException($"Snapshot destination already exists: {destination}");

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ProjectToml.FileName] = Encoding.UTF8.GetBytes(ProjectToml.Serialize(document)),
        };
        string journalPath = Path.Combine(projectRoot, InstallationJournal.FileName);
        if (File.Exists(journalPath))
        {
            if (new FileInfo(journalPath).LinkTarget is not null)
                throw new InvalidDataException($"Journal '{InstallationJournal.FileName}' cannot be a symbolic link.");
            _ = InstallationJournal.Read(journalPath);
            files.Add(InstallationJournal.FileName, ReadLimitedFile(journalPath, InstallationJournal.FileName));
        }
        foreach (string reference in document.InstallationRecords.Values
                     .SelectMany(record => record.PhotoReferences)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            string portablePath = RequirePortablePath(reference, "photo reference");
            if (portablePath is ManifestName or ProjectToml.FileName or InstallationJournal.FileName)
                throw new InvalidDataException($"Photo reference '{reference}' uses a reserved snapshot path.");
            string sourcePath = ResolveContainedRegularFile(projectRoot, portablePath);
            files.Add(portablePath, ReadLimitedFile(sourcePath, portablePath));
        }
        long totalFileBytes = 0;
        foreach ((string portablePath, byte[] contents) in files)
        {
            if (contents.LongLength > MaximumFileBytes)
                throw new InvalidDataException($"Snapshot file '{portablePath}' exceeds the {MaximumFileBytes} byte limit.");
            if (contents.LongLength > MaximumSnapshotBytes - totalFileBytes)
                throw new InvalidDataException($"Snapshot exceeds the {MaximumSnapshotBytes} byte uncompressed limit.");
            totalFileBytes += contents.LongLength;
        }
        if (files.Count + 1 > MaximumEntryCount)
            throw new InvalidDataException($"Snapshot exceeds the {MaximumEntryCount} entry limit.");

        List<SnapshotEntryFile> entries = files
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new SnapshotEntryFile
            {
                Path = item.Key,
                Length = item.Value.LongLength,
                Sha256 = Sha256(item.Value),
            })
            .ToList();
        var manifest = new SnapshotManifestFile
        {
            Format = FormatName,
            FormatVersion = FormatVersion,
            ProjectSchemaVersion = document.SchemaVersion,
            Files = entries,
        };
        byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, JsonOptions) + "\n");

        string? outputDirectory = Path.GetDirectoryName(destination);
        if (outputDirectory is not null) Directory.CreateDirectory(outputDirectory);
        string temporaryPath = destination + $".tmp-{Guid.NewGuid():N}";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteEntry(archive, ManifestName, manifestBytes);
                foreach ((string path, byte[] contents) in files.OrderBy(item => item.Key, StringComparer.Ordinal))
                    WriteEntry(archive, path, contents);
            }
            File.Move(temporaryPath, destination);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
        return new ProjectSnapshotInfo(destination, document.SchemaVersion, files.Count, totalFileBytes);
    }

    public static ProjectSnapshotInfo Import(string snapshotPath, string targetProjectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProjectDirectory);
        string source = Path.GetFullPath(snapshotPath);
        string target = Path.GetFullPath(targetProjectDirectory);
        if (!File.Exists(source)) throw new FileNotFoundException("Snapshot does not exist.", source);
        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException($"Import target already exists: {target}");
        string parent = Path.GetDirectoryName(target) ?? throw new InvalidOperationException("Import target has no parent directory.");
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, $".{Path.GetFileName(target)}.import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            Dictionary<string, byte[]> archiveFiles = ReadAndValidateArchive(source, out SnapshotManifestFile manifest);
            foreach ((string portablePath, byte[] contents) in archiveFiles.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                string destination = Path.Combine(staging, portablePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, contents);
            }
            ProjectDocument document = ProjectToml.Load(staging);
            if (document.SchemaVersion != manifest.ProjectSchemaVersion)
                throw new InvalidDataException($"Snapshot manifest declares project schema {manifest.ProjectSchemaVersion}, but the project loads as schema {document.SchemaVersion}.");
            string importedJournal = Path.Combine(staging, InstallationJournal.FileName);
            if (File.Exists(importedJournal)) _ = InstallationJournal.Read(importedJournal);
            ValidateEvidencePresent(document, archiveFiles.Keys);
            Directory.Move(staging, target);
            return new ProjectSnapshotInfo(target, manifest.ProjectSchemaVersion, archiveFiles.Count,
                archiveFiles.Values.Sum(contents => contents.LongLength));
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private static Dictionary<string, byte[]> ReadAndValidateArchive(string path, out SnapshotManifestFile manifest)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > MaximumEntryCount)
            throw new InvalidDataException($"Snapshot exceeds the {MaximumEntryCount} entry limit.");
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string portablePath = RequirePortablePath(entry.FullName, "ZIP entry");
            if (entry.Length > MaximumFileBytes)
                throw new InvalidDataException($"Snapshot entry '{portablePath}' exceeds the {MaximumFileBytes} byte limit.");
            if (entry.Length > MaximumSnapshotBytes - totalBytes)
                throw new InvalidDataException($"Snapshot exceeds the {MaximumSnapshotBytes} byte uncompressed limit.");
            totalBytes += entry.Length;
            using Stream entryStream = entry.Open();
            using var memory = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
            entryStream.CopyTo(memory);
            if (!entries.TryAdd(portablePath, memory.ToArray()))
                throw new InvalidDataException($"Snapshot contains duplicate entry '{portablePath}'.");
        }
        if (!entries.Remove(ManifestName, out byte[]? manifestBytes))
            throw new InvalidDataException($"Snapshot is missing '{ManifestName}'.");
        try
        {
            manifest = JsonSerializer.Deserialize<SnapshotManifestFile>(manifestBytes, JsonOptions)
                ?? throw new InvalidDataException("Snapshot manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Snapshot manifest is invalid JSON.", exception);
        }
        if (manifest.Format != FormatName || manifest.FormatVersion != FormatVersion)
            throw new InvalidDataException($"Unsupported snapshot format '{manifest.Format}' version {manifest.FormatVersion}.");
        if (manifest.ProjectSchemaVersion <= 0)
            throw new InvalidDataException("Snapshot project_schema_version must be positive.");
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (SnapshotEntryFile declaredEntry in manifest.Files ?? throw new InvalidDataException("Snapshot manifest files are missing."))
        {
            string declaredPath = RequirePortablePath(declaredEntry.Path, "manifest file");
            if (!declared.Add(declaredPath)) throw new InvalidDataException($"Manifest contains duplicate path '{declaredPath}'.");
            if (!entries.TryGetValue(declaredPath, out byte[]? contents))
                throw new InvalidDataException($"Manifest references missing entry '{declaredPath}'.");
            if (declaredEntry.Length != contents.LongLength ||
                !string.Equals(declaredEntry.Sha256, Sha256(contents), StringComparison.Ordinal))
                throw new InvalidDataException($"Snapshot integrity check failed for '{declaredPath}'.");
        }
        if (!declared.SetEquals(entries.Keys)) throw new InvalidDataException("Snapshot contains files not declared by its manifest.");
        if (!entries.ContainsKey(ProjectToml.FileName)) throw new InvalidDataException($"Snapshot is missing '{ProjectToml.FileName}'.");
        return entries;
    }

    private static void ValidateEvidencePresent(ProjectDocument document, IEnumerable<string> importedPaths)
    {
        var paths = importedPaths.ToHashSet(StringComparer.Ordinal);
        foreach (string reference in document.InstallationRecords.Values.SelectMany(record => record.PhotoReferences))
        {
            string portablePath = RequirePortablePath(reference, "photo reference");
            if (portablePath is ManifestName or ProjectToml.FileName or InstallationJournal.FileName)
                throw new InvalidDataException($"Photo reference '{reference}' uses a reserved snapshot path.");
            if (!paths.Contains(portablePath)) throw new InvalidDataException($"Snapshot is missing referenced photo '{portablePath}'.");
        }
    }

    private static string ResolveContainedRegularFile(string projectRoot, string portablePath)
    {
        string rootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string resolved = Path.GetFullPath(Path.Combine(projectRoot, portablePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            throw new InvalidDataException($"Evidence path '{portablePath}' escapes the project directory.");
        string current = projectRoot;
        foreach (string segment in portablePath.Split('/'))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.Exists && info.LinkTarget is not null)
                throw new InvalidDataException($"Evidence path '{portablePath}' contains a symbolic link.");
        }
        if (!File.Exists(resolved)) throw new FileNotFoundException($"Referenced photo '{portablePath}' does not exist.", resolved);
        return resolved;
    }

    private static string RequirePortablePath(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('/') || value.Contains('\\') || Path.IsPathRooted(value) ||
            value.Any(character => char.IsControl(character) || character is ':' or '*' or '?' or '"' or '<' or '>' or '|'))
            throw new InvalidDataException($"Invalid {field} path '{value}'.");
        string[] segments = value.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidDataException($"Invalid {field} path '{value}'.");
        return string.Join('/', segments);
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] contents)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = StableZipTimestamp;
        using Stream stream = entry.Open();
        stream.Write(contents);
    }

    private static byte[] ReadLimitedFile(string path, string portablePath)
    {
        long length = new FileInfo(path).Length;
        if (length > MaximumFileBytes)
            throw new InvalidDataException($"Snapshot file '{portablePath}' exceeds the {MaximumFileBytes} byte limit.");
        byte[] contents = File.ReadAllBytes(path);
        if (contents.LongLength > MaximumFileBytes)
            throw new InvalidDataException($"Snapshot file '{portablePath}' exceeds the {MaximumFileBytes} byte limit.");
        return contents;
    }

    private static string Sha256(byte[] contents) => Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant();

    private sealed class SnapshotManifestFile
    {
        public string Format { get; set; } = string.Empty;
        public int FormatVersion { get; set; }
        public int ProjectSchemaVersion { get; set; }
        public List<SnapshotEntryFile>? Files { get; set; }
    }

    private sealed class SnapshotEntryFile
    {
        public string Path { get; set; } = string.Empty;
        public long Length { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}
