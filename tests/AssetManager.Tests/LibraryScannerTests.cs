using AssetManager.Core;

namespace AssetManager.Tests;

public class LibraryScannerTests
{
	[Fact]
	public void Walk_ReturnsRelativePathsWithSizeAndMtimeFromTheListing()
	{
		using var ws = new TempWorkspace();
		var stamp = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
		ws.Write("images/vendor/pack/hero.png", "12345", stamp);

		var found = LibraryScanner.Walk(ws.Root, "", Fixtures.Types(), null, CancellationToken.None).Files;

		var file = Assert.Single(found);
		Assert.Equal("images/vendor/pack/hero.png", file.RelPath);
		Assert.Equal(5, file.Size);
		Assert.Equal(new DateTimeOffset(stamp).ToUnixTimeSeconds(), file.MTime);
	}

	[Fact]
	public void Walk_OnlyVisitsTypeBuckets()
	{
		using var ws = new TempWorkspace();
		ws.Write("images/a.png");
		ws.Write("notatype/b.png");
		ws.Write(".assetmanager/thumbnails/images/aa/x.webp");

		var found = LibraryScanner.Walk(ws.Root, "", Fixtures.Types(), null, CancellationToken.None).Files;

		Assert.Equal(["images/a.png"], found.Select(f => f.RelPath));
	}

	[Fact]
	public void Walk_WithScanRoot_OnlyVisitsThatSubtree()
	{
		using var ws = new TempWorkspace();
		ws.Write("images/one/a.png");
		ws.Write("images/two/b.png");

		var found = LibraryScanner.Walk(ws.Root, "images/two", Fixtures.Types(), null, CancellationToken.None).Files;

		Assert.Equal(["images/two/b.png"], found.Select(f => f.RelPath));
	}

	[Fact]
	public void Walk_Cancelled_Throws()
	{
		using var ws = new TempWorkspace();
		ws.Write("images/a.png");
		using var cancel = new CancellationTokenSource();
		cancel.Cancel();

		Assert.Throws<OperationCanceledException>(() =>
			LibraryScanner.Walk(ws.Root, "", Fixtures.Types(), null, cancel.Token));
	}
}
