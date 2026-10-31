namespace AssetManager.Core;

/// A file the scanner saw, with the size and mtime its directory listing
/// already carried.
public readonly record struct FoundFile(string RelPath, long Size, long MTime);
