using System;
using System.Collections.Generic;

namespace AssetManager.Core;

/// What makes two files versions of one asset by name: packs in this library
/// ship each format in a sibling folder named after it (`MP3/`, `OGG/`, `WAV/`,
/// `FBX format/`, `GLB format/`), so two files are versions when they share a
/// type and a name apart from the extension, and their folders differ only in
/// such format folders. `Default/` and `Double/` (two resolutions) name no
/// format and keep their files apart.
public static class VersionKey
{
	public static string For(string relPath, AssetType type) =>
		FolderOf(RelPaths.Parent(relPath), type) + "/" + Stem(RelPaths.FileName(relPath)).ToString().ToLowerInvariant();

	/// The folder half of the key: `folder` minus its format folders,
	/// lowercased. The same for every file in the folder, so worked out once
	/// per folder (AssetFolder keeps it).
	public static string FolderOf(string folder, AssetType type)
	{
		var kept = new List<string>();
		foreach (var segment in folder.Split('/'))
		{
			if (!IsFormatFolder(segment, type))
				kept.Add(segment);
		}
		return string.Join("/", kept).ToLowerInvariant();
	}

	/// The name half of the key, before lowercasing: the file name without
	/// its extension.
	public static ReadOnlySpan<char> Stem(string fileName)
	{
		var dot = fileName.LastIndexOf('.');
		return dot > 0 ? fileName.AsSpan(0, dot) : fileName;
	}

	/// A folder whose name, taken word by word, names one of the type's
	/// formats: "MP3", "OGG", "FBX format", "GLTF format", "PNG", "svg".
	public static bool IsFormatFolder(string folderName, AssetType type)
	{
		foreach (var word in folderName.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries))
		{
			var lower = word.ToLowerInvariant();
			if (lower.Length > 0 && type.Extensions.Contains(lower))
				return true;
		}
		return false;
	}

	private static readonly char[] WordBreaks = [' ', '_', '-', '.', '(', ')', '[', ']', '+', '&', ','];
}
