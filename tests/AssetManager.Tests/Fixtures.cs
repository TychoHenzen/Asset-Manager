using AssetManager.Core;

namespace AssetManager.Tests;

/// The subset of asset_types.gd the tests need, with the same rules.
internal static class Fixtures
{
	public static TypeTable Types() => new([
		Type("models", ["glb", "gltf", "fbx"]),
		Type("images", ["png", "jpg", "jpeg", "webp", "svg"]),
		Type("sounds", ["ogg", "mp3", "wav"]),
		Type("themes", ["tres"], ["Theme"]),
		Type("other", [""]),
	]);

	public static AssetType Type(string id, string[] extensions, string[]? resourceTypes = null) => new()
	{
		Id = id,
		Extensions = new HashSet<string>(extensions),
		ResourceTypes = new HashSet<string>(resourceTypes ?? []),
	};

	public static FoundFile File(string relPath, long size = 10, long mtime = 1000) => new(relPath, size, mtime);

	public static Catalog CatalogWith(params FoundFile[] files)
	{
		var catalog = new Catalog(Types());
		catalog.ApplyFiles("", files, now: 500, _ => "");
		return catalog;
	}

	/// ApplyScan for a walk that found `files`, with `vendorRoots` marked.
	public static ScanSummary ApplyFiles(this Catalog catalog, string scanRoot, IEnumerable<FoundFile> files, long now,
		Func<string, string> resourceTypeOf, params string[] vendorRoots) =>
		catalog.ApplyScan(scanRoot, new ScanResult(files.ToList(), new HashSet<string>(vendorRoots)), now, resourceTypeOf);
}
