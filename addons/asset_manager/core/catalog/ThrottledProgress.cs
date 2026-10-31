using System;
using System.Diagnostics;

namespace AssetManager.Core;

/// Passes on at most one report per interval. Each report becomes a deferred
/// call on the main thread, and a scan reports far faster than a frame.
/// The first report always passes, and so does the one that finishes a stage,
/// so the dialog never sits on a stale count.
internal sealed class ThrottledProgress : IProgress<ScanProgress>
{
	private readonly long _intervalTicks;
	private readonly Action<ScanProgress> _report;
	private readonly Func<long> _clock;
	private long _lastTicks;
	private bool _reported;

	public ThrottledProgress(TimeSpan interval, Action<ScanProgress> report, Func<long>? clock = null)
	{
		_clock = clock ?? Stopwatch.GetTimestamp;
		_intervalTicks = (long)(interval.TotalSeconds * Stopwatch.Frequency);
		_report = report;
	}

	public void Report(ScanProgress value)
	{
		var now = _clock();
		var finishesStage = value.UnitsTotal > 0 && value.UnitsDone == value.UnitsTotal;
		if (_reported && !finishesStage && now - _lastTicks < _intervalTicks)
			return;
		_reported = true;
		_lastTicks = now;
		_report(value);
	}
}
