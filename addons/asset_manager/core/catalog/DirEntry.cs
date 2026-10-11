namespace AssetManager.Core;

/// One entry of a folder listing. Size and mtime (Unix seconds) come with the
/// listing, so nothing has to stat the file afterwards.
public readonly record struct DirEntry(string Name, bool IsDirectory, long Size, long MTime);
