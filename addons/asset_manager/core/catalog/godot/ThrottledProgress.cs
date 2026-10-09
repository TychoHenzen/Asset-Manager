using System;
using System.Diagnostics;
using AssetManager.Core;

namespace AssetManager.Bridge;

/// Passes on at most one report per interval. Each report becomes a deferred
/// call on the main thread, and a scan reports far faster than a frame.
internal sealed class ThrottledProgress : IProgress<ScanProgress>
{
	private readonly long _intervalTicks;
	private readonly Action<ScanProgress> _report;
	private long _lastTicks = long.MinValue;

	public ThrottledProgress(TimeSpan interval, Action<ScanProgress> report)
	{
		_intervalTicks = (long)(interval.TotalSeconds * Stopwatch.Frequency);
		_report = report;
	}

	public void Report(ScanProgress value)
	{
		var now = Stopwatch.GetTimestamp();
		if (now - _lastTicks < _intervalTicks)
			return;
		_lastTicks = now;
		_report(value);
	}
}
