namespace AssetManager.Core;

/// Where ThumbnailCache stored a thumbnail:
/// <workspace>/.assetmanager/thumbnails/<type>/<first 2 of key>/<key>.webp.
/// Grouped by type, then sharded, because a flat folder of 100k files is slow
/// to list, worst of all on a NAS.
public static class ThumbnailPaths
{
	public const string Extension = "webp";
	private const int ShardLength = 2;

	/// `workspace` and the returned path use "/", the form Godot hands around
	/// and the form the thumbnail key was hashed from.
	public static string For(string workspace, string typeId, string key) =>
		$"{workspace.TrimEnd('/')}/{CatalogStore.DirName}/thumbnails/{(typeId.Length > 0 ? typeId : "other")}/{key[..ShardLength]}/{key}.{Extension}";

	public static string ForAsset(string workspace, string typeId, string absolutePath, long mtime, long size) =>
		For(workspace, typeId, TextRules.ThumbnailKey(absolutePath, mtime, size));
}
