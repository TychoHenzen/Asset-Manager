using System.Text;
using AssetManager.Core;

namespace AssetManager.Tests;

public class VersionTests
{
	private static readonly TypeTable Types = Fixtures.Types();
	private static AssetType Sounds => Types[Types.IndexOf("sounds")];
	private static AssetType Models => Types[Types.IndexOf("models")];
	private static AssetType Images => Types[Types.IndexOf("images")];

	private static readonly byte[] HashA = [1, 2, 3];
	private static readonly byte[] HashB = [9, 9, 9];

	[Theory]
	[InlineData("MP3", true)]
	[InlineData("OGG", true)]
	[InlineData("wav files", true)]
	[InlineData("Sound Effects", false)]
	[InlineData("Achievements Sound Effects 1 - Universal Sound Effects", false)]
	public void FormatFolders_ForSounds(string folder, bool isFormat) =>
		Assert.Equal(isFormat, VersionKey.IsFormatFolder(folder, Sounds));

	[Theory]
	[InlineData("FBX format", true)]
	[InlineData("GLTF format", true)]
	[InlineData("Models", false)]
	public void FormatFolders_ForModels(string folder, bool isFormat) =>
		Assert.Equal(isFormat, VersionKey.IsFormatFolder(folder, Models));

	[Theory]
	[InlineData("PNG", true)]
	[InlineData("svg", true)]
	[InlineData("Default", false)]
	[InlineData("Double", false)]
	[InlineData("64x64", false)]
	public void FormatFolders_ForImages(string folder, bool isFormat) =>
		Assert.Equal(isFormat, VersionKey.IsFormatFolder(folder, Images));

	[Fact]
	public void FormatsInSiblingFolders_ShareAKey()
	{
		const string pack = "sounds/elvgames/sfx/Achievements";
		var key = VersionKey.For($"{pack}/OGG/Achievement_01.ogg", Sounds);

		Assert.Equal(key, VersionKey.For($"{pack}/MP3/Achievement_01.mp3", Sounds));
		Assert.Equal(key, VersionKey.For($"{pack}/WAV/achievement_01.wav", Sounds));
		Assert.NotEqual(key, VersionKey.For($"{pack}/WAV/Achievement_02.wav", Sounds));
	}

	[Fact]
	public void TwoResolutions_DoNotShareAKey() =>
		Assert.NotEqual(
			VersionKey.For("images/kenney/ui/pack/PNG/Default/button.png", Images),
			VersionKey.For("images/kenney/ui/pack/PNG/Double/button.png", Images));

	[Fact]
	public void Groups_JoinFormats_InPreferenceOrder()
	{
		const string pack = "sounds/v/sfx/p";
		var catalog = Fixtures.CatalogWith(
			Fixtures.File($"{pack}/WAV/hit.wav"), Fixtures.File($"{pack}/MP3/hit.mp3"), Fixtures.File($"{pack}/OGG/hit.ogg"),
			Fixtures.File($"{pack}/OGG/miss.ogg"));

		Assert.Equal([$"{pack}/OGG/hit.ogg", $"{pack}/MP3/hit.mp3", $"{pack}/WAV/hit.wav"], catalog.VersionsOf($"{pack}/MP3/hit.mp3"));
		Assert.Equal([$"{pack}/OGG/miss.ogg"], catalog.VersionsOf($"{pack}/OGG/miss.ogg"));
		Assert.Empty(catalog.VersionsOf("sounds/not/indexed.ogg"));
	}

	[Fact]
	public void Groups_JoinExactCopies_OnlyWhenTheHashesMatch()
	{
		var catalog = Fixtures.CatalogWith(
			Fixtures.File("images/a/icons/one/sheet.png", 50), Fixtures.File("images/b/icons/two/sheet.png", 50),
			Fixtures.File("images/c/icons/three/sheet.png", 50));
		SetHash(catalog, "images/a/icons/one/sheet.png", HashA);
		SetHash(catalog, "images/b/icons/two/sheet.png", HashA);
		SetHash(catalog, "images/c/icons/three/sheet.png", HashB);

		Assert.Equal(["images/a/icons/one/sheet.png", "images/b/icons/two/sheet.png"], catalog.VersionsOf("images/b/icons/two/sheet.png"));
		Assert.Equal(["images/c/icons/three/sheet.png"], catalog.VersionsOf("images/c/icons/three/sheet.png"));
	}

	[Fact]
	public void Groups_Chain_ACopyOfOneFormatJoinsTheWholeGroup()
	{
		var catalog = Fixtures.CatalogWith(
			Fixtures.File("sounds/v/c/p/OGG/hit.ogg", 40), Fixtures.File("sounds/v/c/p/WAV/hit.wav", 90),
			Fixtures.File("sounds/w/c/q/hit.ogg", 40));
		SetHash(catalog, "sounds/v/c/p/OGG/hit.ogg", HashA);
		SetHash(catalog, "sounds/w/c/q/hit.ogg", HashA);

		Assert.Equal(3, catalog.VersionsOf("sounds/v/c/p/WAV/hit.wav").Length);
	}

	[Fact]
	public void Query_ShowsOneTilePerAsset_AndCountsVersions()
	{
		var catalog = Fixtures.CatalogWith(
			Fixtures.File("sounds/v/c/p/WAV/hit.wav"), Fixtures.File("sounds/v/c/p/OGG/hit.ogg"),
			Fixtures.File("sounds/v/c/p/OGG/miss.ogg"));

		var page = catalog.Query(new CatalogQuery());

		Assert.Equal(["sounds/v/c/p/OGG/hit.ogg", "sounds/v/c/p/OGG/miss.ogg"], page.Items.Select(r => r.RelPath));
		Assert.Equal([2, 1], page.VersionCounts);
		Assert.Equal(2, page.Total);
	}

	[Fact]
	public void Query_WithAnExtensionFilter_ShowsTheVersionThatMatches()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("sounds/v/c/p/WAV/hit.wav"), Fixtures.File("sounds/v/c/p/OGG/hit.ogg"));

		var page = catalog.Query(new CatalogQuery { Extensions = ["wav"] });

		Assert.Equal(["sounds/v/c/p/WAV/hit.wav"], page.Items.Select(r => r.RelPath));
	}

	[Fact]
	public void Query_Ungrouped_ShowsEveryFile()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("sounds/v/c/p/WAV/hit.wav"), Fixtures.File("sounds/v/c/p/OGG/hit.ogg"));

		var page = catalog.Query(new CatalogQuery { GroupVersions = false });

		Assert.Equal(2, page.Total);
		Assert.Equal([1, 1], page.VersionCounts);
	}

	[Fact]
	public void Query_PagesCountGroups()
	{
		var files = Enumerable.Range(0, 5).SelectMany(i => new[]
		{
			Fixtures.File($"sounds/v/c/p/OGG/s{i}.ogg"), Fixtures.File($"sounds/v/c/p/WAV/s{i}.wav"),
		}).ToArray();
		var catalog = Fixtures.CatalogWith(files);

		var page = catalog.Query(new CatalogQuery { PerPage = 2, Page = 2 });

		Assert.Equal(5, page.Total);
		Assert.Equal(3, page.TotalPages);
		Assert.Equal(["s4.ogg"], page.Items.Select(r => r.FileName));
	}

	[Fact]
	public void AChangedFile_LosesItsHash()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("images/a/b/c/x.png", 10, 1000));
		SetHash(catalog, "images/a/b/c/x.png", HashA);

		catalog.ApplyFiles("", [Fixtures.File("images/a/b/c/x.png", 11, 2000)], 600, _ => "");

		Assert.Null(catalog.Get("images/a/b/c/x.png")!.Hash);
	}

	[Fact]
	public void SetHash_IgnoresAHashOfContentThatChangedSince()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("images/a/b/c/x.png", 10, 1000));
		var record = catalog.Get("images/a/b/c/x.png")!;

		Assert.False(catalog.SetHash(record, 10, 999, HashA));
		Assert.Null(record.Hash);
	}

	[Fact]
	public void HashCandidates_AreFilesSharingNameAndSize()
	{
		var catalog = Fixtures.CatalogWith(
			Fixtures.File("images/a/x.png", 5), Fixtures.File("images/b/x.png", 5),
			Fixtures.File("images/c/x.png", 6), Fixtures.File("images/d/y.png", 5));

		var candidates = DuplicateHashing.CandidatesIn(catalog.Snapshot().Records);

		Assert.Equal(["images/a/x.png", "images/b/x.png"], candidates.Select(r => r.RelPath).Order());
	}

	[Fact]
	public void AScan_HashesCandidates_AndGroupsTheCopies()
	{
		var library = new FakeLibrary("images/v/c/one/x.png", "images/v/c/two/x.png", "images/v/c/three/y.png");
		var catalog = new Catalog(Types);
		var read = new List<string>();
		var hashing = new DuplicateHashing(catalog, rel => { lock (read) read.Add(rel); return HashA; }, RetryPolicy.Immediate(0));
		var run = new ScanRun(catalog, new LibraryScanner(library, RetryPolicy.Immediate(0)), _ => "", () => 1) { Hashing = hashing };

		var report = run.Run("", null, () => { }, CancellationToken.None);

		Assert.Equal(2, report.Hashed);
		Assert.Equal(["images/v/c/one/x.png", "images/v/c/two/x.png"], read.Order());
		Assert.Equal(2, catalog.VersionsOf("images/v/c/one/x.png").Length);
	}

	[Fact]
	public void CatalogFile_RoundTripsHashes()
	{
		using var ws = new TempWorkspace();
		var catalog = Fixtures.CatalogWith(Fixtures.File("images/a/x.png"), Fixtures.File("images/b/y.png"));
		SetHash(catalog, "images/a/x.png", HashA);
		new CatalogStore(ws.Root).SaveCatalog(catalog);

		var loaded = new Catalog(Types);
		new CatalogStore(ws.Root).TryLoad(loaded);

		Assert.Equal(HashA, loaded.Get("images/a/x.png")!.Hash);
		Assert.Null(loaded.Get("images/b/y.png")!.Hash);
	}

	[Fact]
	public void CatalogFile_StillReadsVersion1()
	{
		using var stream = new MemoryStream();
		using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
		{
			StoreHeader.Write(w, CatalogFile.Magic, 1, generation: 4);
			w.Write(1);
			w.Write("images");
			w.Write(1);
			w.Write("craftpix");
			w.Write(1);
			w.Write("images/craftpix/a.png");
			w.Write(0);
			w.Write(7L);
			w.Write(70L);
			w.Write(500L);
			w.Write(1);
			w.Write(0);
			w.Write(0);
		}

		var contents = CatalogFile.Deserialize(stream.ToArray(), Types);

		var record = Assert.Single(contents.Records);
		Assert.Equal(("images/craftpix/a.png", 7L, 70L), (record.RelPath, record.Size, record.MTime));
		Assert.Null(record.Hash);
		Assert.Equal(4, contents.Generation);
	}

	private static void SetHash(Catalog catalog, string relPath, byte[] hash)
	{
		var record = catalog.Get(relPath)!;
		Assert.True(catalog.SetHash(record, record.Size, record.MTime, hash));
	}
}
