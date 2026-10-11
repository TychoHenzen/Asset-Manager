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
		ExtensionOrder = extensions,
		ResourceTypes = new HashSet<string>(resourceTypes ?? []),
	};

	public static FoundFile File(string relPath, long size = 10, long mtime = 1000) => new(relPath, size, mtime);

	public static Catalog CatalogWith(params FoundFile[] files)
	{
		var catalog = new Catalog(Types());
		catalog.ApplyFiles("", files, now: 500, _ => "");
		return catalog;
	}

	/// ApplyScan for one walk of `scanRoot` that found `files`, with
	/// `vendorRoots` marked.
	public static ScanSummary ApplyFiles(this Catalog catalog, string scanRoot, IEnumerable<FoundFile> files, long now,
		Func<string, string> resourceTypeOf, params string[] vendorRoots)
	{
		var walk = new ScanResult(files.ToList(), new HashSet<string>(vendorRoots));
		var coverage = ScanCoverage.Of([new UnitResult(scanRoot, Recursive: true, walk.Files, [])]);
		return catalog.ApplyScan(walk, coverage, now, resourceTypeOf);
	}

	/// Every file and vendor marker under `scanRoot` on disk, by way of the
	/// layout and unit walks a scan makes.
	public static ScanResult Walk(string workspaceRoot, string scanRoot, CancellationToken cancel = default)
	{
		var scanner = new LibraryScanner(new FileSystemLister(workspaceRoot), RetryPolicy.Immediate(0));
		var layout = scanner.Layout(scanRoot, Types(), cancel);
		var files = layout.Shallow.SelectMany(u => u.Files).ToList();
		foreach (var unit in layout.Units)
			files.AddRange(scanner.WalkUnit(unit, layout.VendorRoots, cancel).Files);
		return new ScanResult(files, layout.VendorRoots);
	}
}
