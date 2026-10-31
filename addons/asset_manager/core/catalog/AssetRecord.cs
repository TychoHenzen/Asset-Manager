using System;
using System.Collections.Concurrent;

namespace AssetManager.Core;

/// One indexed file. Size and mtime come from the directory listing at scan
/// time, so nothing downstream ever has to stat the file again: they are
/// half of the thumbnail key and the whole of the size sort.
/// Kept small, there are 1.77M of these: the path lives in the shared
/// folder, the extension is one of a dozen shared strings, and nothing is
/// stored that can be worked out from what is.
public sealed class AssetRecord
{
	private static readonly ConcurrentDictionary<string, string> Extensions = new(StringComparer.Ordinal);

	public AssetRecord(AssetFolder folder, string fileName, int type)
	{
		Folder = folder;
		FileName = fileName;
		Type = type;
		var extension = TextRules.ExtensionLower(fileName);
		ExtensionLower = Extensions.GetOrAdd(extension, extension);
	}

	/// Set again only when a catalog adopts a loaded record into its own
	/// folder of the same path.
	public AssetFolder Folder { get; internal set; }

	public string FileName { get; }
	public int Type { get; }
	public string ExtensionLower { get; }

	/// Workspace-relative, "/"-separated, first segment is the type bucket.
	/// Built on each call: for the edges (a page of results, a save), not for
	/// walks over every record.
	public string RelPath => Folder.Path.Length == 0 ? FileName : $"{Folder.Path}/{FileName}";

	public long Size { get; set; }
	public long MTime { get; set; }

	/// Unix seconds, set once when the file is first indexed.
	public long DateAdded { get; set; }

	/// Interned tag ids derived from folder names, stored in catalog.bin. The
	/// array is shared with the other files of the folder; replace it, never
	/// write into it.
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

	public bool IsUnder(string relPrefix) => Folder.IsAtOrUnder(relPrefix);

	/// Ordinal order of RelPath, without building either path: the tie-break
	/// of every sort. Not folder then name: "a/b c/x" sorts before "a/b/x",
	/// since a space is below "/".
	public static int ComparePaths(AssetRecord a, AssetRecord b)
	{
		if (ReferenceEquals(a.Folder, b.Folder))
			return string.CompareOrdinal(a.FileName, b.FileName);
		// Nearly always the folders differ somewhere inside both, and that
		// decides it; the walk below is for one folder containing the other.
		var folderA = a.Folder.Path;
		var folderB = b.Folder.Path;
		var common = folderA.AsSpan().CommonPrefixLength(folderB);
		if (common < folderA.Length && common < folderB.Length)
			return folderA[common] - folderB[common];

		var length = a.PathLength;
		var other = b.PathLength;
		var n = Math.Min(length, other);
		for (var i = common; i < n; i++)
		{
			var ca = a.PathChar(i);
			var cb = b.PathChar(i);
			if (ca != cb)
				return ca - cb;
		}
		return length - other;
	}

	private int PathLength => Folder.Path.Length == 0 ? FileName.Length : Folder.Path.Length + 1 + FileName.Length;

	private char PathChar(int i)
	{
		var folder = Folder.Path;
		if (folder.Length == 0)
			return FileName[i];
		if (i < folder.Length)
			return folder[i];
		return i == folder.Length ? '/' : FileName[i - folder.Length - 1];
	}
}
