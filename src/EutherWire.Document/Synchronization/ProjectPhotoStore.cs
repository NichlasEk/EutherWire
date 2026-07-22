using System.Security.Cryptography;
using EutherWire.Document.Model;

namespace EutherWire.Document.Synchronization;

public sealed record ProjectPhotoImport(
    string Reference,
    string FullPath,
    string Sha256,
    long Length,
    bool Created);

public static class ProjectPhotoStore
{
    public const long MaximumPhotoBytes = 64L * 1024 * 1024;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.Ordinal)
    {
        "jpg", "jpeg", "png", "webp", "heic", "heif",
    };

    public static ProjectPhotoImport Import(string projectDirectory, ObjectId objectId, Stream source, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(source);
        string normalizedExtension = NormalizeExtension(extension);
        string projectRoot = Path.GetFullPath(projectDirectory);
        if (!Directory.Exists(projectRoot)) throw new DirectoryNotFoundException($"Project directory does not exist: {projectRoot}");
        string photoDirectory = Path.Combine(projectRoot, "photos");
        Directory.CreateDirectory(photoDirectory);
        string temporaryPath = Path.Combine(photoDirectory, $".import-{Guid.NewGuid():N}.tmp");
        long length = 0;
        string sha256;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[64 * 1024];
                while (true)
                {
                    int read = source.Read(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    length += read;
                    if (length > MaximumPhotoBytes)
                        throw new InvalidDataException($"Photo exceeds the {MaximumPhotoBytes} byte limit.");
                    output.Write(buffer, 0, read);
                    hash.AppendData(buffer, 0, read);
                }
                output.Flush(flushToDisk: true);
            }
            if (length == 0) throw new InvalidDataException("Photo is empty.");
            sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            string fileName = $"{objectId.Value}-{sha256}.{normalizedExtension}";
            string reference = $"photos/{fileName}";
            string destination = Resolve(projectRoot, reference);
            if (File.Exists(destination))
            {
                File.Delete(temporaryPath);
                return new ProjectPhotoImport(reference, destination, sha256, length, Created: false);
            }
            File.Move(temporaryPath, destination);
            return new ProjectPhotoImport(reference, destination, sha256, length, Created: true);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    public static string Resolve(string projectDirectory, string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        string portable = reference.Replace('\\', '/');
        if (!portable.StartsWith("photos/", StringComparison.Ordinal) || Path.IsPathRooted(portable))
            throw new InvalidDataException($"Photo reference '{reference}' must be project-relative under photos/.");
        string projectRoot = Path.GetFullPath(projectDirectory);
        string fullPath = Path.GetFullPath(Path.Combine(projectRoot, portable.Replace('/', Path.DirectorySeparatorChar)));
        string containedPrefix = projectRoot.EndsWith(Path.DirectorySeparatorChar)
            ? projectRoot
            : projectRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(containedPrefix, StringComparison.Ordinal))
            throw new InvalidDataException($"Photo reference '{reference}' escapes the project directory.");
        return fullPath;
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string normalized = extension.Trim().TrimStart('.').ToLowerInvariant();
        if (!SupportedExtensions.Contains(normalized))
            throw new InvalidDataException($"Unsupported photo type '.{normalized}'. Use JPEG, PNG, WebP, HEIC, or HEIF.");
        return normalized == "jpeg" ? "jpg" : normalized;
    }
}
