using AssetManager.Core;

namespace AssetManager.Tests;

public class CatalogStoreTests
{
	[Fact]
	public void SaveThenLoad_RoundTripsRecordsTagsAndSubtypes()
	{
		using var ws = new TempWorkspace();
		var original = Fixtures.CatalogWith(Fixtures.File("images/craftpix/a.png", 7, 70), Fixtures.File("models/x.glb"));
		original.SetUserTags("images/craftpix/a.png", ["hero", "mine"]);
		original.SetSubtypes(new Dictionary<string, string[]> { ["models/x.glb"] = ["3d"] });
		var store = new CatalogStore(ws.Root);
		store.SaveCatalog(original);
		store.SaveTags(original, "images/craftpix/a.png");

		var loaded = new Catalog(Fixtures.Types());
		Assert.True(new CatalogStore(ws.Root).TryLoad(loaded));

		var a = loaded.Get("images/craftpix/a.png")!;
		Assert.Equal((7L, 70L, 500L), (a.Size, a.MTime, a.DateAdded));
		Assert.Equal(["hero", "mine"], loaded.TagsOf(a.RelPath));
		Assert.Equal(["3d"], loaded.Get("models/x.glb")!.Subtypes);
	}

	[Fact]
	public void TryLoad_WithoutCatalogFile_ReturnsFalse()
	{
		using var ws = new TempWorkspace();
		Assert.False(new CatalogStore(ws.Root).TryLoad(new Catalog(Fixtures.Types())));
	}

	[Fact]
	public void IsStale_TrueAfterAnotherStoreWrites()
	{
		using var ws = new TempWorkspace();
		var catalog = Fixtures.CatalogWith(Fixtures.File("images/a.png"));
		var mine = new CatalogStore(ws.Root);
		mine.SaveCatalog(catalog);
		Assert.False(mine.IsStale());

		new CatalogStore(ws.Root).SaveCatalog(catalog);

		Assert.True(mine.IsStale());
	}

	[Fact]
	public void SaveTags_FoldsInOverridesAnotherMachineSavedMeanwhile()
	{
		using var ws = new TempWorkspace();
		var seed = Fixtures.CatalogWith(Fixtures.File("images/a.png"), Fixtures.File("images/b.png"));
		new CatalogStore(ws.Root).SaveCatalog(seed);

		var mineCatalog = new Catalog(Fixtures.Types());
		var mine = new CatalogStore(ws.Root);
		mine.TryLoad(mineCatalog);

		var theirsCatalog = new Catalog(Fixtures.Types());
		var theirs = new CatalogStore(ws.Root);
		theirs.TryLoad(theirsCatalog);
		theirsCatalog.SetUserTags("images/b.png", ["theirs"]);
		theirs.SaveTags(theirsCatalog, "images/b.png");

		mineCatalog.SetUserTags("images/a.png", ["mine"]);
		mine.SaveTags(mineCatalog, "images/a.png");

		var check = new Catalog(Fixtures.Types());
		new CatalogStore(ws.Root).TryLoad(check);
		Assert.Equal(["mine"], check.TagsOf("images/a.png"));
		Assert.Equal(["theirs"], check.TagsOf("images/b.png"));
	}

	[Fact]
	public void ResourceHeader_ReadsTypeFromFirstLine()
	{
		using var ws = new TempWorkspace();
		var path = ws.Write("themes/ui.tres", "[gd_resource type=\"Theme\" format=3 uid=\"uid://x\"]\n\n[resource]\n");
		Assert.Equal("Theme", ResourceHeader.TypeOf(path));
		Assert.Equal("", ResourceHeader.TypeOf(path + ".missing"));
	}
}
