using System.Text.Json;
using AssetManager.Core;

namespace AssetManager.Tests;

public class CatalogJsonTests
{
	[Fact]
	public void ParseTypes_ReadsWhatJsonStringifyOfAssetTypesWrites()
	{
		const string json = """
			[{"id":"images","extensions":["PNG","jpg"],"default_icon":"Image"},
			 {"id":"themes","extensions":["tres"],"resource_type":["Theme"]},
			 {"id":"other","extensions":[""]}]
			""";

		var types = CatalogJson.ParseTypes(json);

		Assert.Equal(["images", "themes", "other"], types.All.Select(t => t.Id));
		Assert.True(types[0].MatchesExtension("png"));
		Assert.True(types[1].NeedsResourceHeader);
		Assert.True(types[2].MatchesExtension(""));
	}

	[Fact]
	public void ParseQuery_TakesFloatsFromGodotAndMakesPrefixRelative()
	{
		const string json = """{"prefix":"Z:/lib/images/craft","tags":["a"],"sort":2.0,"page":3.0,"per_page":50.0,"search":"x"}""";

		var query = CatalogJson.ParseQuery(json, "Z:/lib");

		Assert.Equal("images/craft", query.Prefix);
		Assert.Equal(["a"], query.Tags);
		Assert.Equal((SortMode.Size, 3, 50, "x"), (query.Sort, query.Page, query.PerPage, query.Search));
	}

	[Fact]
	public void Stats_HasTheSidebarShape()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("images/craftpix/forest/a.png"), Fixtures.File("images/craftpix/forest/b.png"));

		using var doc = JsonDocument.Parse(CatalogJson.Stats(catalog.Stats("")));
		var root = doc.RootElement;

		Assert.Equal(2, root.GetProperty("total").GetInt32());
		var images = root.GetProperty("folders").GetProperty("images");
		Assert.Equal(2, images.GetProperty("count").GetInt32());
		Assert.True(images.GetProperty("subfolders").GetProperty("craftpix").GetProperty("forest").GetBoolean());
		Assert.Equal(2, root.GetProperty("tags").GetProperty("forest").GetInt32());
	}

	[Theory]
	[InlineData("Z:/lib", "Z:/lib/images/a.png", "images/a.png")]
	[InlineData("Z:/lib/", "Z:/lib/images/", "images")]
	[InlineData("Z:/lib", "Z:/lib", "")]
	[InlineData("Z:/lib", "Z:/library/images/a.png", "")]
	public void WorkspacePaths_Relative(string workspace, string absolute, string expected) =>
		Assert.Equal(expected, WorkspacePaths.Relative(workspace, absolute));
}
