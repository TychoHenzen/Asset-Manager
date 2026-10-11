using System;

namespace AssetManager.Core;

/// One indexed file. Size and mtime come from the directory listing at scan
/// time, so nothing downstream ever has to stat the file again: they are
/// half of the thumbnail key and the whole of the size sort.
public sealed class AssetRecord
{
	public AssetRecord(string relPath, int type)
	{
		RelPath = relPath;
		Type = type;
		var slash = relPath.LastIndexOf('/');
		FileName = slash < 0 ? relPath : relPath[(slash + 1)..];
		FileNameLower = FileName.ToLowerInvariant();
		ExtensionLower = TextRules.ExtensionLower(FileName);
	}

	/// Workspace-relative, "/"-separated, first segment is the type bucket.
	public string RelPath { get; }
	public int Type { get; }
	public string FileName { get; }
	public string FileNameLower { get; }
	public string ExtensionLower { get; }

	public long Size { get; set; }
	public long MTime { get; set; }

	/// Unix seconds, set once when the file is first indexed.
	public long DateAdded { get; set; }

	/// Interned tag ids derived from folder names, stored in catalog.bin.
	public int[] AutoTags { get; set; } = [];

	/// Interned tag ids the user set by hand, stored in tags.bin. Null when the
	/// user never edited this asset's tags.
	public int[]? UserTags { get; set; }

	public int[] Tags => UserTags ?? AutoTags;

	/// Filter categories a thumbnail renderer detected ("3d", "spatial"...).
	public string[] Subtypes { get; set; } = [];

	/// SHA-256 of the file's content, or null when it was never read. Only
	/// files that share a name and size with another are hashed, to tell exact
	/// copies apart from different files that happen to match. Cleared when
	/// size or mtime change.
	public byte[]? Hash { get; set; }

	/// VersionKey.For this record, kept once worked out: path and type never
	/// change, and grouping 1.7M records reworks every key otherwise.
	internal string? VersionKey { get; set; }

	public bool IsUnder(string relPrefix) =>
		relPrefix.Length == 0
		|| (RelPath.Length > relPrefix.Length
			&& RelPath[relPrefix.Length] == '/'
			&& RelPath.StartsWith(relPrefix, StringComparison.Ordinal));
}
