using System.Collections.Generic;

namespace AssetManager.Core;

/// Where a thumbnail is cached:
/// <workspace>/.assetmanager/thumbnails/<type folder>/<first 2 of key>/<key>.webp.
/// Grouped by type, then sharded, because a flat folder of 100k files is slow
/// to list, worst of all on a NAS.
public static class ThumbnailPaths
{
	public const string Extension = "webp";
	private const int ShardLength = 2;

	/// The key only changes when the asset does, so a renderer that starts
	/// drawing differently would keep being shown its old thumbnails. Bumping a
	/// type here moves it to a fresh folder ("models.2"); the old one is left
	/// as an orphan rather than deleted. A type at version 1 keeps the plain
	/// folder the GDScript addon used, so its thumbnails stay valid.
	private static readonly Dictionary<string, int> RendererVersions = new()
	{
		// 3: FBX textures found outside the model's folder (ModelLoader).
		// 2 was rendered while that fill never triggered.
		["models"] = 3,
	};

	public static string TypeFolder(string typeId)
	{
		var folder = typeId.Length > 0 ? typeId : "other";
		return RendererVersions.TryGetValue(folder, out var version) && version > 1 ? $"{folder}.{version}" : folder;
	}

	/// `workspace` and the returned path use "/", the form Godot hands around
	/// and the form the thumbnail key was hashed from.
	public static string For(string workspace, string typeId, string key) =>
		$"{workspace.TrimEnd('/')}/{CatalogStore.DirName}/thumbnails/{TypeFolder(typeId)}/{key[..ShardLength]}/{key}.{Extension}";

	public static string ForAsset(string workspace, string typeId, string absolutePath, long mtime, long size) =>
		For(workspace, typeId, TextRules.ThumbnailKey(absolutePath, mtime, size));
}
