using AssetManager.Core;

namespace AssetManager.Tests;

public class CatalogScanTests
{
	[Fact]
	public void ApplyScan_AddsNewFilesWithAutoTagsAndDateAdded()
	{
		var catalog = new Catalog(Fixtures.Types());

		var summary = catalog.ApplyFiles("", [Fixtures.File("images/craftpix/forest/tree.png")], now: 42, _ => "");

		Assert.Equal(new ScanSummary(1, 0, 0, 1), summary);
		var record = catalog.Get("images/craftpix/forest/tree.png")!;
		Assert.Equal(42, record.DateAdded);
		Assert.Equal(["craftpix", "forest"], catalog.TagsOf(record.RelPath));
	}

	[Fact]
	public void ApplyScan_SkipsFilesWhoseExtensionTheirBucketDoesNotAccept()
	{
		var catalog = new Catalog(Fixtures.Types());

		catalog.ApplyFiles("", [Fixtures.File("images/readme.txt"), Fixtures.File("other/LICENSE"), Fixtures.File("other/x.txt")], 1, _ => "");

		Assert.NotNull(catalog.Get("other/LICENSE"));
		Assert.Equal(1, catalog.Count);
	}

	[Fact]
	public void Rescan_LeavesUnchangedFilesAlone_AndKeepsUserTagsOnChangedOnes()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("images/a.png", 10, 100), Fixtures.File("images/b.png", 10, 100));
		catalog.SetUserTags("images/b.png", ["mine"]);

		var summary = catalog.ApplyFiles("", [Fixtures.File("images/a.png", 10, 100), Fixtures.File("images/b.png", 20, 200)], 999, _ => "");

		Assert.Equal(new ScanSummary(0, 1, 0, 2), summary);
		var b = catalog.Get("images/b.png")!;
		Assert.Equal((20L, 200L, 500L), (b.Size, b.MTime, b.DateAdded));
		Assert.Equal(["mine"], catalog.TagsOf("images/b.png"));
	}

	[Fact]
	public void ApplyScan_RemovesMissingFilesOnlyInsideTheScanRoot()
	{
		var catalog = Fixtures.CatalogWith(
			Fixtures.File("images/one/a.png"),
			Fixtures.File("images/two/b.png"),
			Fixtures.File("images/twofold/c.png"));

		var summary = catalog.ApplyFiles("images/two", [], 1, _ => "");

		Assert.Equal(1, summary.Removed);
		Assert.Null(catalog.Get("images/two/b.png"));
		Assert.NotNull(catalog.Get("images/one/a.png"));
		Assert.NotNull(catalog.Get("images/twofold/c.png"));
	}

	[Fact]
	public void ApplyScan_ReadsResourceHeadersOnlyForNewOrChangedFiles()
	{
		var reads = new List<string>();
		string Reader(string rel)
		{
			reads.Add(rel);
			return rel.EndsWith("ui_theme.tres") ? "Theme" : "StandardMaterial3D";
		}

		var catalog = new Catalog(Fixtures.Types());
		catalog.ApplyFiles("", [Fixtures.File("themes/ui_theme.tres"), Fixtures.File("themes/material.tres")], 1, Reader);
		Assert.NotNull(catalog.Get("themes/ui_theme.tres"));
		Assert.Null(catalog.Get("themes/material.tres"));

		reads.Clear();
		catalog.ApplyFiles("", [Fixtures.File("themes/ui_theme.tres")], 2, Reader);
		Assert.Empty(reads);
	}
}
