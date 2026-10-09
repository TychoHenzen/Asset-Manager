using AssetManager.Core;

namespace AssetManager.Tests;

public class VendorMarkerTests
{
	[Fact]
	public void Walk_ReportsMarkersAsVendorRootsNotFiles()
	{
		using var ws = new TempWorkspace();
		ws.Write("models/kenney/" + VendorMarker.FileName, "kenney");
		ws.Write("models/kenney/props/barrel.glb");

		var walk = LibraryScanner.Walk(ws.Root, "", Fixtures.Types(), null, CancellationToken.None);

		Assert.Equal(["models/kenney/props/barrel.glb"], walk.Files.Select(f => f.RelPath));
		Assert.Equal(["models/kenney"], walk.VendorRoots);
	}

	[Fact]
	public void Walk_ScopedBelowTheVendor_StillFindsTheMarkerAbove()
	{
		using var ws = new TempWorkspace();
		ws.Write("models/kenney/" + VendorMarker.FileName, "kenney");
		ws.Write("models/kenney/props/pack/barrel.glb");

		var walk = LibraryScanner.Walk(ws.Root, "models/kenney/props/pack", Fixtures.Types(), null, CancellationToken.None);

		Assert.Contains("models/kenney", walk.VendorRoots);
	}

	[Fact]
	public void FilesUnderAMarkedVendor_GetTheirTypeAsTheFirstTag()
	{
		var catalog = new Catalog(Fixtures.Types());

		catalog.ApplyFiles("", [Fixtures.File("models/kenney/props/barrel.glb"), Fixtures.File("images/craftpix/hero.png")], 1, _ => "", "models/kenney");

		Assert.Equal(["models", "kenney", "props"], catalog.TagsOf("models/kenney/props/barrel.glb"));
		Assert.Equal(["craftpix"], catalog.TagsOf("images/craftpix/hero.png"));
	}

	[Fact]
	public void AddingAMarkerLater_RetagsUnchangedFiles()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("models/kenney/props/barrel.glb"));
		catalog.SetUserTags("models/kenney/props/barrel.glb", ["mine"]);

		var summary = catalog.ApplyFiles("", [Fixtures.File("models/kenney/props/barrel.glb")], 2, _ => "", "models/kenney");

		Assert.Equal(new ScanSummary(0, 0, 0, 1, Retagged: 1), summary);
		Assert.Equal(["mine"], catalog.TagsOf("models/kenney/props/barrel.glb"));
		var snapshot = catalog.Snapshot();
		Assert.Equal(["models", "kenney", "props"], catalog.Get("models/kenney/props/barrel.glb")!.AutoTags.Select(id => snapshot.TagNames[id]));

		var again = catalog.ApplyFiles("", [Fixtures.File("models/kenney/props/barrel.glb")], 3, _ => "", "models/kenney");
		Assert.Equal(0, again.Retagged);
	}
}
