using System.Text;
using AssetManager.Core;

namespace AssetManager.Tests;

/// Records share their folder, extension and tag arrays, and what is derived
/// from them is built per type; none of that may change what a query, a
/// search or a saved file says.
public class CompactCatalogTests
{
	private static readonly TypeTable Types = Fixtures.Types();

	[Fact]
	public void FilesOfOneFolder_ShareTheFolderAndTags()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("images/v/c/p/a.png"), Fixtures.File("images/v/c/p/b.PNG"));
		var a = catalog.Get("images/v/c/p/a.png")!;
		var b = catalog.Get("images/v/c/p/b.PNG")!;

		Assert.Same(a.Folder, b.Folder);
		Assert.Same(a.AutoTags, b.AutoTags);
		Assert.Same(a.ExtensionLower, b.ExtensionLower);
		Assert.Equal("images/v/c/p/b.PNG", b.RelPath);
	}

	[Fact]
	public void PathOrder_IsOrdinalOrderOfTheWholePath()
	{
		// A space sorts below "/", so folder-then-name order would differ.
		string[] paths = ["images/a/b c/x.png", "images/a/b/x.png", "images/a/b/X.png", "images/a/b-c/y.png", "images/a/bc.png", "images/a.png"];
		var catalog = Fixtures.CatalogWith(paths.Select(p => Fixtures.File(p)).ToArray());
		var records = paths.Select(p => catalog.Get(p)!).ToList();

		records.Sort(AssetRecord.ComparePaths);

		Assert.Equal(paths.Order(StringComparer.Ordinal), records.Select(r => r.RelPath));
	}

	[Theory]
	[InlineData("tree", false)]
	[InlineData("TREE", false)]
	[InlineData("\"Oak_Tree\"", true)]
	[InlineData("otr", false)]
	public void Search_IgnoresCase_WithoutLowercaseCopies(string search, bool exact)
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("images/v/forest/Oak_Tree.png"), Fixtures.File("images/v/forest/rock.png"));

		var page = catalog.Query(new CatalogQuery { Search = search });

		Assert.Equal(["Oak_Tree.png"], page.Items.Select(r => r.FileName));
		Assert.Equal(exact, new CatalogQuery { Search = search }.IsExactSearch);
	}

	[Fact]
	public void AQueryOfOneType_LeavesTheOtherTypesUnsortedAndUngrouped()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("sounds/v/c/p/OGG/hit.ogg"), Fixtures.File("sounds/v/c/p/WAV/hit.wav"),
			Fixtures.File("images/v/c/p/a.png"));
		var images = Types.IndexOf("images");
		var sounds = Types.IndexOf("sounds");

		var page = catalog.Query(new CatalogQuery { Prefix = "sounds" });

		Assert.Equal([2], page.VersionCounts);
		Assert.True(catalog.HasIndexesFor(sounds));
		Assert.False(catalog.HasIndexesFor(images));
	}

	[Fact]
	public void AScanOfOneType_KeepsTheOtherTypesIndexes()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("sounds/v/c/p/hit.ogg"), Fixtures.File("images/v/c/p/a.png"));
		catalog.Query(new CatalogQuery { Prefix = "images" });

		catalog.ApplyFiles("sounds/v", [Fixtures.File("sounds/v/c/p/hit.ogg"), Fixtures.File("sounds/v/c/p/miss.ogg")], 600, _ => "");

		Assert.True(catalog.HasIndexesFor(Types.IndexOf("images")));
		Assert.False(catalog.HasIndexesFor(Types.IndexOf("sounds")));
		Assert.Equal(2, catalog.Query(new CatalogQuery { Prefix = "sounds" }).Total);
	}

	[Fact]
	public void TheAllView_GroupsWithinEachType()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("sounds/v/c/p/OGG/hit.ogg"), Fixtures.File("sounds/v/c/p/WAV/hit.wav"),
			Fixtures.File("models/v/c/p/GLB format/hit.glb"), Fixtures.File("models/v/c/p/FBX format/hit.fbx"));

		var page = catalog.Query(new CatalogQuery());

		Assert.Equal(["models/v/c/p/GLB format/hit.glb", "sounds/v/c/p/OGG/hit.ogg"], page.Items.Select(r => r.RelPath));
		Assert.Equal([2, 2], page.VersionCounts);
	}

	[Fact]
	public void RemovingAFolder_sLastFile_LetsTheFolderGo()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("images/v/c/p/a.png"), Fixtures.File("images/v/c/q/b.png"));

		catalog.ApplyFiles("images/v/c/p", [], 600, _ => "");

		Assert.Null(catalog.Get("images/v/c/p/a.png"));
		Assert.Equal(["images/v/c/q/b.png"], catalog.Snapshot().Records.Select(r => r.RelPath));
		Assert.Equal(1, catalog.Count);
	}

	[Fact]
	public void CatalogFile_Version3_RoundTripsEverything()
	{
		using var ws = new TempWorkspace();
		var catalog = Fixtures.CatalogWith(Fixtures.File("images/a/b/x.png", 7, 70), Fixtures.File("images/a/b/y.png"),
			Fixtures.File("sounds/v/c/p/hit.ogg", 5, -1));
		var x = catalog.Get("images/a/b/x.png")!;
		catalog.SetHash(x, 7, 70, [1, 2, 3]);
		catalog.SetSubtypes(new Dictionary<string, string[]> { ["images/a/b/y.png"] = ["2d"] });
		new CatalogStore(ws.Root).SaveCatalog(catalog);

		var loaded = new Catalog(Types);
		new CatalogStore(ws.Root).TryLoad(loaded);

		Assert.Equal(Describe(catalog), Describe(loaded));
		Assert.Same(loaded.Get("images/a/b/x.png")!.Folder, loaded.Get("images/a/b/y.png")!.Folder);
	}

	[Fact]
	public void CatalogFile_StillReadsVersion2()
	{
		using var stream = new MemoryStream();
		using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
		{
			StoreHeader.Write(w, CatalogFile.Magic, 2, generation: 9);
			w.Write(1);
			w.Write("images");
			w.Write(1);
			w.Write("craftpix");
			w.Write(2);
			foreach (var name in new[] { "a.png", "b.png" })
			{
				w.Write("images/craftpix/" + name);
				w.Write(0);
				w.Write(7L);
				w.Write(70L);
				w.Write(500L);
				w.Write(1);
				w.Write(0);
				w.Write(0);
				w.Write(name == "a.png" ? 2 : 0);
				if (name == "a.png")
					w.Write(new byte[] { 4, 2 });
			}
		}

		var contents = CatalogFile.Deserialize(stream.ToArray(), Types);

		Assert.Equal(["images/craftpix/a.png", "images/craftpix/b.png"], contents.Records.Select(r => r.RelPath));
		Assert.Equal(new byte[] { 4, 2 }, contents.Records[0].Hash);
		Assert.Null(contents.Records[1].Hash);
		Assert.Same(contents.Records[0].Folder, contents.Records[1].Folder);
		Assert.Same(contents.Records[0].AutoTags, contents.Records[1].AutoTags);
		Assert.Equal(9, contents.Generation);
	}

	private static string[] Describe(Catalog catalog) =>
		catalog.Snapshot().Records
			.Select(r => $"{r.RelPath}|{r.Type}|{r.Size}|{r.MTime}|{r.DateAdded}|{string.Join(',', catalog.TagsOf(r.RelPath))}|{string.Join(',', r.Subtypes)}|{Convert.ToHexString(r.Hash ?? [])}")
			.Order(StringComparer.Ordinal)
			.ToArray();
}
