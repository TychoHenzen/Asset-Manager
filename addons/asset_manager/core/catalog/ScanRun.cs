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

	/// Run after the walk, to find exact copies; none when null.
	public DuplicateHashing? Hashing { get; init; }

	// Hashing saves from several threads at once.
	private readonly object _saveGate = new();
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

			Hashing?.Run(progress, () => SaveIfDue(save, changed: true), cancel);
		}
		catch (OperationCanceledException)
		{
			cancelled = true;
			Apply();
		}

		_unsaved |= _summary.AnythingChanged;
		if (_unsaved)
			save();
		return new ScanReport(_summary with { Total = catalog.Count }, failed, cancelled, scanner.Retries,
			Hashing?.Hashed ?? 0, Hashing?.Unreadable ?? 0);
	}

	private void SaveIfDue(Action save, bool changed)
	{
		lock (_saveGate)
		{
			_unsaved |= changed;
			if (!_unsaved || _sinceSave.Elapsed < SaveEvery)
				return;
			save();
			_unsaved = false;
			_sinceSave.Restart();
		}
	}

	private void WalkUnits(string stage, List<string> roots, List<string> failures, IProgress<ScanProgress>? progress, Action save, CancellationToken cancel)
	{
		for (var i = 0; i < roots.Count; i++)
		{
			cancel.ThrowIfCancellationRequested();
			var done = i;
			// Some packs hold hundreds of thousands of files and take many
			// minutes, so their files so far are applied as they come too.
			var applied = 0;
			var unit = scanner.WalkUnit(roots[i], _vendorRoots, cancel, (partial, folder) =>
			{
				progress?.Report(new ScanProgress(stage, _filesSeen + partial.Files.Count, folder, done, roots.Count));
				if (_sinceApply.Elapsed < ApplyEvery)
					return;
				Apply(partial.Files.Skip(applied));
				applied = partial.Files.Count;
				SaveIfDue(save, changed: false);
			});
			_filesSeen += unit.Files.Count;
			failures.AddRange(unit.FailedDirs);
			_pending.Add(unit);

			if (_sinceApply.Elapsed >= ApplyEvery)
				Apply();
			SaveIfDue(save, changed: false);
		}
		progress?.Report(new ScanProgress(stage, _filesSeen, "", roots.Count, roots.Count));
	}

	/// Folds the finished units into the catalog, plus `unfinished` files of a
	/// unit still being walked. Those are only added or updated: until the
	/// unit is done nothing can be said about what it no longer holds, so its
	/// removals wait for the unit's own apply, which sees all its files.
	private void Apply(IEnumerable<FoundFile>? unfinished = null)
	{
		_sinceApply.Restart();
		var files = _pending.SelectMany(u => u.Files).Concat(unfinished ?? []).ToList();
		if (files.Count == 0 && _pending.Count == 0)
			return;
		var walk = new ScanResult(files, _vendorRoots);
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
