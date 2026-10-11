using System;
using System.Collections.Generic;

namespace AssetManager.Core;

/// One folder of the library, shared by every record in it. 1.77M files sit
/// in 84k folders, so a record holding its folder rather than its full path
/// keeps one copy of each path instead of twenty, and anything decided by the
/// folder alone (scan coverage, prefix filters, the version key) is worked
/// out once per folder.
public sealed class AssetFolder
{
	internal AssetFolder(string path) => Path = path;

	/// Workspace-relative, "/"-separated, first segment is the type bucket.
	public string Path { get; }

	/// The catalog's records in this folder, by file name. Catalog guards it.
	internal Dictionary<string, AssetRecord> Files { get; } = new(StringComparer.Ordinal);

	// VersionKey.FolderOf this folder, for the type it was worked out for. A
	// reference, so a reader on another thread sees a whole pair or none.
	private sealed record CachedKey(int Type, string Key);
	private CachedKey? _versionFolder;

	/// True for `prefix` itself and every folder below it; "" is the whole
	/// workspace.
	public bool IsAtOrUnder(string prefix) =>
		prefix.Length == 0
		|| (Path.Length == prefix.Length && Path == prefix)
		|| (Path.Length > prefix.Length && Path[prefix.Length] == '/' && Path.StartsWith(prefix, StringComparison.Ordinal));

	/// This folder minus its format folders, lowercased, for `type`. Kept,
	/// since a folder's files all ask for it; benign if two threads race.
	internal string VersionFolder(int typeIndex, AssetType type)
	{
		var cached = _versionFolder;
		if (cached != null && cached.Type == typeIndex)
			return cached.Key;
		var key = VersionKey.FolderOf(Path, type);
		_versionFolder = new CachedKey(typeIndex, key);
		return key;
	}
}
