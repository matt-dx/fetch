namespace Fetch.Core.Services;

public static class ChunkNaming
{
    /// <summary>
    /// Returns the chunk temp file path for a given output file and chunk index.
    /// When hidden, the filename is dot-prefixed (e.g. ".file.zip.000001").
    /// </summary>
    public static string GetChunkPath(string outputFilePath, int index, bool hidden)
    {
        var dir = Path.GetDirectoryName(outputFilePath) ?? ".";
        var fileName = Path.GetFileName(outputFilePath);
        var chunkName = hidden
            ? $".{fileName}.{index:D6}"
            : $"{fileName}.{index:D6}";
        return Path.Combine(dir, chunkName);
    }

    /// <summary>
    /// Returns the manifest file path for a given output file.
    /// When hidden, the filename is dot-prefixed.
    /// </summary>
    public static string GetManifestPath(string outputFilePath, bool hidden)
    {
        var dir = Path.GetDirectoryName(outputFilePath) ?? ".";
        var fileName = Path.GetFileName(outputFilePath);
        var manifestName = hidden
            ? $".{fileName}.fetch-manifest.json"
            : $"{fileName}.fetch-manifest.json";
        return Path.Combine(dir, manifestName);
    }

    /// <summary>
    /// Sets or clears FILE_ATTRIBUTE_HIDDEN on Windows. No-op on other platforms.
    /// </summary>
    public static void SetHiddenAttribute(string path, bool hidden)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!File.Exists(path)) return;

        var attrs = File.GetAttributes(path);
        if (hidden)
            File.SetAttributes(path, attrs | FileAttributes.Hidden);
        else
            File.SetAttributes(path, attrs & ~FileAttributes.Hidden);
    }

    /// <summary>
    /// Finds all existing chunk files for the given output path, searching both
    /// dot-prefixed (hidden) and non-prefixed (visible) patterns.
    /// </summary>
    public static IEnumerable<(string Path, int Index, bool IsHidden)> FindExistingChunks(string outputFilePath)
    {
        var dir = Path.GetDirectoryName(outputFilePath) ?? ".";
        if (!Directory.Exists(dir)) yield break;
        var fileName = Path.GetFileName(outputFilePath);

        foreach (var (pattern, isHidden) in new[]
        {
            ($"{fileName}.??????", false),
            ($".{fileName}.??????", true)
        })
        {
            foreach (var file in Directory.EnumerateFiles(dir, pattern))
            {
                var ext = Path.GetExtension(file);
                if (ext.Length == 7 && int.TryParse(ext.AsSpan(1), out _))
                    yield return (file, int.Parse(ext.AsSpan(1)), isHidden);
            }
        }
    }

    /// <summary>
    /// Finds an existing manifest file (hidden or visible) for the given output path.
    /// Returns the path if found, or null.
    /// </summary>
    public static string? FindExistingManifest(string outputFilePath)
    {
        var dir = Path.GetDirectoryName(outputFilePath) ?? ".";
        var fileName = Path.GetFileName(outputFilePath);

        var visible = Path.Combine(dir, $"{fileName}.fetch-manifest.json");
        if (File.Exists(visible)) return visible;

        var hidden = Path.Combine(dir, $".{fileName}.fetch-manifest.json");
        if (File.Exists(hidden)) return hidden;

        return null;
    }

    /// <summary>
    /// Migrates chunk and manifest files to match the desired visibility.
    /// Scans the disk for existing chunk files at both hidden and visible paths,
    /// renames them to the desired convention, and sets/clears hidden attributes.
    /// Updates chunk.TempFilePath on each chunk in the manifest.
    /// Returns the (possibly updated) manifest path.
    /// </summary>
    public static string MigrateVisibility(
        Models.DownloadManifest manifest, string outputFilePath,
        string currentManifestPath, bool hidden)
    {
        // Build a lookup of all existing chunk files on disk (both hidden and visible)
        var existingOnDisk = FindExistingChunks(outputFilePath)
            .ToLookup(c => c.Index);

        // Migrate chunk files
        foreach (var chunk in manifest.Chunks)
        {
            var desiredPath = GetChunkPath(outputFilePath, chunk.Index, hidden);

            // Find any existing file for this chunk index (at the "wrong" path)
            if (existingOnDisk.Contains(chunk.Index))
            {
                foreach (var (existingPath, _, _) in existingOnDisk[chunk.Index])
                {
                    if (existingPath != desiredPath && File.Exists(existingPath))
                    {
                        File.Move(existingPath, desiredPath, overwrite: false);
                        break; // Only one source file per chunk index
                    }
                }
            }

            chunk.TempFilePath = desiredPath;

            if (File.Exists(desiredPath))
                SetHiddenAttribute(desiredPath, hidden);
        }

        // Migrate manifest file
        var desiredManifestPath = GetManifestPath(outputFilePath, hidden);
        if (currentManifestPath != desiredManifestPath && File.Exists(currentManifestPath))
        {
            File.Move(currentManifestPath, desiredManifestPath, overwrite: false);
            SetHiddenAttribute(desiredManifestPath, hidden);
        }
        else if (File.Exists(desiredManifestPath))
        {
            SetHiddenAttribute(desiredManifestPath, hidden);
        }

        return desiredManifestPath;
    }
}
