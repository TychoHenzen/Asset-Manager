using AssetManager.Core;

namespace AssetManager.Tests;

public class ScanRunTests
{
	private const string PackA = "images/vendor/icons/pack-a";
	private const string PackB = "images/vendor/icons/pack-b";

	private sealed record Outcome(ScanReport Report, int Saves);

	private static Outcome Run(Catalog catalog, FakeLibrary library, int retries = 2,
		CancellationToken cancel = default, string scanRoot = "")
	{
		var scanner = new LibraryScanner(library, RetryPolicy.Immediate(retries));
		var run = new ScanRun(catalog, scanner, _ => "", () => 7) { ApplyEvery = TimeSpan.Zero };
		var saves = 0;
		var report = run.Run(scanRoot, null, () => saves++, cancel);
		return new Outcome(report, saves);
	}

	private static string[] Paths(Catalog catalog) =>
		catalog.Snapshot().Records.Select(r => r.RelPath).Order(StringComparer.Ordinal).ToArray();

	[Fact]
	public void AFolderThatFailsAFewTimes_IsRetriedAndIndexed()
	{
		var library = new FakeLibrary($"{PackA}/a.png", $"{PackB}/b.png");
		library.Fail(PackA, times: 2);
		var catalog = new Catalog(Fixtures.Types());

		var (report, saves) = Run(catalog, library);

		Assert.Equal([$"{PackA}/a.png", $"{PackB}/b.png"], Paths(catalog));
		Assert.Empty(report.FailedFolders);
		Assert.Equal(2, report.Retries);
		Assert.False(report.Cancelled);
		Assert.Equal(1, saves);
	}

	[Fact]
	public void AFolderThatNeverLists_IsReported_ItsRecordsKept_AndTheRestIndexed()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File($"{PackA}/old.png"), Fixtures.File($"{PackA}/sub/older.png"));
		var library = new FakeLibrary($"{PackA}/sub/older.png", $"{PackB}/b.png");
		library.Fail($"{PackA}/sub");

		var (report, _) = Run(catalog, library);

		// old.png is gone from a folder that listed; older.png sits under one that never did.
		Assert.Equal([$"{PackA}/sub/older.png", $"{PackB}/b.png"], Paths(catalog));
		Assert.Equal([$"{PackA}/sub"], report.FailedFolders);
		Assert.Equal(1, report.Summary.Removed);
	}

	[Fact]
	public void AFolderThatFailsThroughTheFirstPass_IsRecoveredByTheSecond()
	{
		var library = new FakeLibrary($"{PackA}/a.png");
		library.Fail(PackA, times: 3);
		var catalog = new Catalog(Fixtures.Types());

		var (report, _) = Run(catalog, library, retries: 2);

		Assert.Equal([$"{PackA}/a.png"], Paths(catalog));
		Assert.Empty(report.FailedFolders);
	}

	[Fact]
	public void AFolderAbovePackDepthThatNeverLists_KeepsEverythingBelowIt()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File($"{PackA}/a.png"), Fixtures.File("sounds/v/c/p/s.ogg"));
		var library = new FakeLibrary($"{PackA}/a.png", "sounds/v/c/p/s.ogg");
		library.Fail("images/vendor");

		var (report, _) = Run(catalog, library);

		Assert.Equal([$"{PackA}/a.png", "sounds/v/c/p/s.ogg"], Paths(catalog));
		Assert.Equal(["images/vendor"], report.FailedFolders);
	}

	[Fact]
	public void APackThatVanished_IsRemovedAtTheEnd()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File($"{PackA}/a.png"), Fixtures.File($"{PackB}/b.png"));
		var library = new FakeLibrary($"{PackB}/b.png");

		var (report, _) = Run(catalog, library);

		Assert.Equal([$"{PackB}/b.png"], Paths(catalog));
		Assert.Equal(1, report.Summary.Removed);
	}

	[Fact]
	public void Stopping_KeepsThePacksAlreadyWalked_AndRemovesNothingElse()
	{
		const string packC = "images/vendor/icons/pack-c";
		var catalog = Fixtures.CatalogWith(Fixtures.File("sounds/v/c/p/kept.ogg"));
		var library = new FakeLibrary($"{PackA}/a.png", $"{PackB}/b.png", $"{packC}/c.png");
		using var cancel = new CancellationTokenSource();
		library.BeforeList = folder =>
		{
			if (folder == PackB)
				cancel.Cancel();
		};

		var (report, saves) = Run(catalog, library, cancel: cancel.Token);

		// Stop lands while pack B is being listed: B finishes, C never starts,
		// and the sounds the scan never reached are not taken for deleted.
		Assert.True(report.Cancelled);
		Assert.Equal([$"{PackA}/a.png", $"{PackB}/b.png", "sounds/v/c/p/kept.ogg"], Paths(catalog));
		Assert.DoesNotContain(packC, library.Listed);
		Assert.Equal(1, saves);
	}

	[Fact]
	public void StoppingInsideABigPack_KeepsItsFoldersWalkedSoFar_AndRemovesNothingFromIt()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File($"{PackA}/old/gone.png"));
		var library = new FakeLibrary($"{PackA}/s1/a.png", $"{PackA}/s2/b.png", $"{PackA}/s3/c.png");
		using var cancel = new CancellationTokenSource();
		library.BeforeList = folder =>
		{
			if (folder == $"{PackA}/s2")
				cancel.Cancel();
		};

		var outcome = Run(catalog, library, cancel: cancel.Token);

		// Stop lands while s2 is listed, so s3 is never reached. The pack never
		// finished, so the record it no longer holds stays until a scan that
		// does finish it.
		Assert.True(outcome.Report.Cancelled);
		Assert.Equal([$"{PackA}/old/gone.png", $"{PackA}/s1/a.png", $"{PackA}/s2/b.png"], Paths(catalog));
		Assert.DoesNotContain($"{PackA}/s3", library.Listed);
	}

	[Fact]
	public void AnUnchangedLibrary_IsNotSaved()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File($"{PackA}/a.png"));
		var library = new FakeLibrary($"{PackA}/a.png");

		var (report, saves) = Run(catalog, library);

		Assert.False(report.Summary.AnythingChanged);
		Assert.Equal(0, saves);
	}

	[Fact]
	public void AScopedScan_OnlyTouchesItsSubtree()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File($"{PackA}/a.png"), Fixtures.File($"{PackB}/gone.png"));
		var library = new FakeLibrary($"{PackA}/a.png", $"{PackA}/new.png");

		Run(catalog, library, scanRoot: PackA);

		Assert.Equal([$"{PackA}/a.png", $"{PackA}/new.png", $"{PackB}/gone.png"], Paths(catalog));
		Assert.DoesNotContain(PackB, library.Listed);
	}

	[Fact]
	public void FilesAbovePackDepth_AreIndexedAndRemovedLikeAnyOther()
	{
		var catalog = Fixtures.CatalogWith(Fixtures.File("images/vendor/loose.png"), Fixtures.File("images/vendor/gone.png"));
		var library = new FakeLibrary("images/vendor/loose.png", "images/top.png");

		Run(catalog, library);

		Assert.Equal(["images/top.png", "images/vendor/loose.png"], Paths(catalog));
	}
}
