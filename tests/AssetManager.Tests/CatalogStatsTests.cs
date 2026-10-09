using AssetManager.Core;

namespace AssetManager.Tests;

public class CatalogStatsTests
{
	[Fact]
	public void Stats_BuildsThreeLevelFolderTree()
	{
		var catalog = Fixtures.CatalogWith(
			Fixtures.File("images/craftpix/forest/a.png"),
			Fixtures.File("images/craftpix/desert/b.png"),
			Fixtures.File("images/loose.png"),
			Fixtures.File("models/kenney/c.glb"));

		var stats = catalog.Stats("");

		Assert.Equal(4, stats.Total);
		Assert.Equal(3, stats.Folders["images"].Count);
		Assert.Equal(["desert", "forest"], stats.Folders["images"].Subfolders["craftpix"]);
		Assert.Empty(stats.Folders["models"].Subfolders["kenney"]);
	}

	[Fact]
	public void Stats_HidesTagsOnlyOneAssetCarries_AndScopesCountsToPrefix()
	{
		var catalog = Fixtures.CatalogWith(
			Fixtures.File("images/craftpix/forest/a.png"),
			Fixtures.File("images/craftpix/forest/b.png"),
			Fixtures.File("images/craftpix/unique/c.png"),
			Fixtures.File("models/craftpix/d.glb"));

		var all = catalog.Stats("");
		Assert.Equal(4, all.Tags["craftpix"]);
		Assert.Equal(2, all.Tags["forest"]);
		Assert.False(all.Tags.ContainsKey("unique"));

		var models = catalog.Stats("models");
		Assert.Equal(1, models.Tags["craftpix"]);
		Assert.Equal(new Dictionary<string, int> { ["glb"] = 1 }, models.Extensions);
	}
}
