using AssetManager.Core;

namespace AssetManager.Tests;

public class CatalogQueryTests
{
	private static Catalog Library() => Fixtures.CatalogWith(
		Fixtures.File("images/craft/forest/b_tree.png", 300, 1),
		Fixtures.File("images/craft/forest/a_tree.png", 100, 1),
		Fixtures.File("images/craftpix/c_rock.jpg", 200, 1),
		Fixtures.File("models/kenney/wooden_sword_01.glb", 50, 1),
		Fixtures.File("models/kenney/sword.glb", 60, 1));

	private static string[] Names(QueryPage page) => page.Items.Select(r => r.FileName).ToArray();

	[Fact]
	public void Query_ByName_SortsCaseInsensitively()
	{
		var page = Library().Query(new CatalogQuery());
		Assert.Equal(["a_tree.png", "b_tree.png", "c_rock.jpg", "sword.glb", "wooden_sword_01.glb"], Names(page));
		Assert.Equal(5, page.Total);
	}

	[Fact]
	public void Query_Prefix_MatchesWholeFolderNamesOnly()
	{
		var page = Library().Query(new CatalogQuery { Prefix = "images/craft" });
		Assert.Equal(["a_tree.png", "b_tree.png"], Names(page));
	}

	[Fact]
	public void Query_TagsExtensionsAndSize()
	{
		var catalog = Library();
		Assert.Equal(["a_tree.png", "b_tree.png"], Names(catalog.Query(new CatalogQuery { Tags = ["forest"] })));
		Assert.Equal(["c_rock.jpg"], Names(catalog.Query(new CatalogQuery { Extensions = ["JPG"] })));
		Assert.Equal("b_tree.png", Names(catalog.Query(new CatalogQuery { Sort = SortMode.Size }))[0]);
		Assert.Empty(Names(catalog.Query(new CatalogQuery { Tags = ["no_such_tag"] })));
	}

	[Fact]
	public void Query_FuzzySearch_PutsExactFilenameHitsFirst()
	{
		var page = Library().Query(new CatalogQuery { Search = "sword" });
		Assert.Equal(["sword.glb", "wooden_sword_01.glb"], Names(page));

		var fuzzy = Library().Query(new CatalogQuery { Search = "swrd" });
		Assert.Equal(["sword.glb", "wooden_sword_01.glb"], Names(fuzzy));
	}

	[Fact]
	public void Query_QuotedSearch_IsExactSubstringOnFilenameOrTag()
	{
		var catalog = Library();
		Assert.Empty(Names(catalog.Query(new CatalogQuery { Search = "\"swrd\"" })));
		Assert.Equal(["a_tree.png", "b_tree.png"], Names(catalog.Query(new CatalogQuery { Search = "\"fores\"" })));
	}

	[Fact]
	public void Query_PagesAndFallsBackToTheLastPage()
	{
		var catalog = Library();
		var second = catalog.Query(new CatalogQuery { PerPage = 2, Page = 1 });
		Assert.Equal(["c_rock.jpg", "sword.glb"], Names(second));
		Assert.Equal(3, second.TotalPages);

		var beyond = catalog.Query(new CatalogQuery { PerPage = 2, Page = 9 });
		Assert.Equal(2, beyond.Page);
		Assert.Equal(["wooden_sword_01.glb"], Names(beyond));
	}

	[Fact]
	public void KnownTags_AndSortOrders_FollowEdits()
	{
		var catalog = Library();
		Assert.DoesNotContain("hero", catalog.KnownTags());
		Assert.Equal("a_tree.png", Names(catalog.Query(new CatalogQuery()))[0]);

		catalog.SetUserTags("models/kenney/sword.glb", ["hero"]);
		catalog.ApplyFiles("images/craft/forest", [
			Fixtures.File("images/craft/forest/a_tree.png", 100, 1),
			Fixtures.File("images/craft/forest/b_tree.png", 300, 1),
			Fixtures.File("images/craft/forest/_first.png"),
		], 9, _ => "");

		Assert.Contains("hero", catalog.KnownTags());
		Assert.Equal("_first.png", Names(catalog.Query(new CatalogQuery()))[0]);
	}

	[Fact]
	public void Query_Subtype_FiltersOnRendererDetectedCategory()
	{
		var catalog = Library();
		catalog.SetSubtypes(new Dictionary<string, string[]> { ["models/kenney/sword.glb"] = ["3d"] });
		Assert.Equal(["sword.glb"], Names(catalog.Query(new CatalogQuery { Subtype = "3d" })));
	}
}
