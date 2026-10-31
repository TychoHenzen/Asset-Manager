using System.Diagnostics;
using AssetManager.Core;

namespace AssetManager.Tests;

public class ThrottledProgressTests
{
	private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(150);
	private static readonly long IntervalTicks = (long)(Interval.TotalSeconds * Stopwatch.Frequency);

	[Fact]
	public void TheFirstReport_PassesWhateverTheClockReads()
	{
		foreach (var start in new[] { 0L, 12_345L, Stopwatch.GetTimestamp(), long.MaxValue - IntervalTicks })
		{
			var seen = new List<ScanProgress>();
			var throttle = new ThrottledProgress(Interval, seen.Add, () => start);

			throttle.Report(new ScanProgress("scan", 1, "a", 0, 10));

			Assert.Single(seen);
		}
	}

	[Fact]
	public void ReportsInsideTheInterval_AreDropped_AndTheNextOneAfterItPasses()
	{
		var now = 1_000L;
		var seen = new List<ScanProgress>();
		var throttle = new ThrottledProgress(Interval, seen.Add, () => now);

		throttle.Report(new ScanProgress("scan", 1, "a", 0, 10));
		now += IntervalTicks - 1;
		throttle.Report(new ScanProgress("scan", 2, "b", 1, 10));
		now += 1;
		throttle.Report(new ScanProgress("scan", 3, "c", 2, 10));

		Assert.Equal(["a", "c"], seen.Select(p => p.CurrentFolder));
	}

	[Fact]
	public void TheReportThatFinishesAStage_AlwaysPasses()
	{
		var seen = new List<ScanProgress>();
		var throttle = new ThrottledProgress(Interval, seen.Add, () => 1_000L);

		throttle.Report(new ScanProgress("scan", 1, "a", 9, 10));
		throttle.Report(new ScanProgress("scan", 2, "", 10, 10));

		Assert.Equal(2, seen.Count);
	}
}
