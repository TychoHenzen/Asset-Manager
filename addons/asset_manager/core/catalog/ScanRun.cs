using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace AssetManager.Core;

/// One scan of the workspace, kept as it goes. Walked units are folded into
/// the catalog in batches and saved at intervals, so a scan stopped or broken
/// off after an hour keeps that hour's work, and a folder that stays
/// unreadable costs only that folder.
public sealed class ScanRun(Catalog catalog, LibraryScanner scanner, Func<string, string> resourceTypeOf, Func<long> now)
{
	/// Each apply sweeps the whole record set for removals, so batches are
	/// by time rather than per pack.
	public TimeSpan ApplyEvery { get; init; } = TimeSpan.FromSeconds(30);
	public TimeSpan SaveEvery { get; init; } = TimeSpan.FromMinutes(2);

	private readonly List<UnitResult> _pending = [];
	private readonly ScanCoverage _walked = new();
	private readonly Stopwatch _sinceApply = Stopwatch.StartNew();
	private readonly Stopwatch _sinceSave = Stopwatch.StartNew();
	private HashSet<string> _vendorRoots = [];
	private ScanSummary _summary;
	private bool _unsaved;
	private int _filesSeen;

	/// `save` writes the catalog; it is called at intervals while anything
	/// changed and once at the end. A cancelled scan keeps every unit that
	/// finished, and skips only the sweep for vanished packs.
	public ScanReport Run(string scanRoot, IProgress<ScanProgress>? progress, Action save, CancellationToken cancel)
	{
		var failed = new List<string>();
		var cancelled = false;
		try
		{
			var layout = scanner.Layout(scanRoot, catalog.Types, cancel,
				folder => progress?.Report(new ScanProgress("layout", 0, folder)));
			_vendorRoots = layout.VendorRoots;
			_pending.AddRange(layout.Shallow);

			var firstPassFailures = new List<string>(layout.FailedDirs);
			WalkUnits("scan", layout.Units, firstPassFailures, progress, save, cancel);
			// A second chance for folders that failed the first time; whatever
			// still fails is what gets reported.
			WalkUnits("retry", firstPassFailures, failed, progress, save, cancel);

			cancel.ThrowIfCancellationRequested();
			Apply();
			_summary = _summary with { Removed = _summary.Removed + catalog.RemoveUncovered(scanRoot, _walked) };
		}
		catch (OperationCanceledException)
		{
			cancelled = true;
			Apply();
		}

		_unsaved |= _summary.AnythingChanged;
		if (_unsaved)
			save();
		return new ScanReport(_summary with { Total = catalog.Count }, failed, cancelled, scanner.Retries);
	}

	private void WalkUnits(string stage, List<string> roots, List<string> failures, IProgress<ScanProgress>? progress, Action save, CancellationToken cancel)
	{
		for (var i = 0; i < roots.Count; i++)
		{
			cancel.ThrowIfCancellationRequested();
			var done = i;
			var unit = scanner.WalkUnit(roots[i], _vendorRoots, cancel, (files, folder) =>
				progress?.Report(new ScanProgress(stage, _filesSeen + files, folder, done, roots.Count)));
			_filesSeen += unit.Files.Count;
			failures.AddRange(unit.FailedDirs);
			_pending.Add(unit);

			if (_sinceApply.Elapsed >= ApplyEvery)
				Apply();
			if (_unsaved && _sinceSave.Elapsed >= SaveEvery)
			{
				save();
				_unsaved = false;
				_sinceSave.Restart();
			}
		}
		progress?.Report(new ScanProgress(stage, _filesSeen, "", roots.Count, roots.Count));
	}

	private void Apply()
	{
		_sinceApply.Restart();
		if (_pending.Count == 0)
			return;
		var walk = new ScanResult(_pending.SelectMany(u => u.Files).ToList(), _vendorRoots);
		var batch = catalog.ApplyScan(walk, ScanCoverage.Of(_pending), now(), resourceTypeOf);
		_walked.AddRange(_pending);
		_pending.Clear();
		_unsaved |= batch.AnythingChanged;
		_summary = new ScanSummary(
			_summary.Added + batch.Added,
			_summary.Changed + batch.Changed,
			_summary.Removed + batch.Removed,
			batch.Total,
			_summary.Retagged + batch.Retagged);
	}
}
