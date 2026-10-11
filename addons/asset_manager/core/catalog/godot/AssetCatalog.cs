using System;
using System.IO;
using System.Linq;
using System.Threading;
using AssetManager.Core;
using Godot;

namespace AssetManager.Bridge;

/// The GDScript UI's handle on the catalog. Anything that touches the disk
/// runs on a background task and answers with a signal on the main thread;
/// the synchronous members only read memory. Paths crossing this boundary are
/// absolute, "/"-separated, the way the UI already held them.
/// Only strings, numbers and packed arrays cross the boundary: a Godot
/// Array or Dictionary wrapper left alive on the C# side crashes the editor
/// on exit. For the same reason, connect bound methods to these signals, not
/// GDScript lambdas (seen on 4.7.1).
[Tool]
[GlobalClass]
public partial class AssetCatalog : RefCounted, ISerializationListener
{
	/// `needsScan`: there was no catalog.bin, the workspace is new or was only
	/// migrated from index.db (which has no file sizes).
	[Signal] public delegate void OpenedEventHandler(int count, bool needsScan);
	[Signal] public delegate void ScanProgressedEventHandler(string stage, string label, int current, int total);
	/// `retagged`: unchanged files whose folder tags changed (a vendor marker was
	/// added or removed above them). `failedFolders` stayed unreadable through
	/// every retry; what was indexed under them is kept. `retries` counts
	/// requests to the library that failed and were tried again.
	[Signal] public delegate void ScanFinishedEventHandler(int added, int changed, int removed, int total, bool cancelled, string root, double seconds, int retagged, string[] failedFolders, int retries);
	/// `versionCounts[i]` is how many versions (formats, exact copies) the
	/// asset at `paths[i]` has, 1 for none.
	[Signal] public delegate void QueryReadyEventHandler(int requestId, string[] paths, string[] types, long[] sizes, long[] mtimes, int total, int page, int totalPages, int[] versionCounts);

	/// `paths` are every version of the asset `path` asked about, preferred
	/// first, `sizes` theirs; just `path` itself when it has no others.
	[Signal] public delegate void VersionsReadyEventHandler(int requestId, string path, string[] paths, long[] sizes);

	/// `statsJson` has the shape sidebar._gather_stats returned.
	[Signal] public delegate void StatsReadyEventHandler(int requestId, string statsJson);

	/// The catalog changed under the UI (a reload from disk, or a long scan
	/// saving what it has so far); refresh views.
	[Signal] public delegate void ChangedEventHandler();

	private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(150);
	private static readonly TimeSpan SubtypeSaveDelay = TimeSpan.FromSeconds(10);

	private readonly BackgroundWork _work = new();
	private Catalog _catalog = new(new TypeTable([]));
	private CatalogStore _store = new("");
	private CancellationTokenSource? _scanCancel;
	private int _subtypeSaveQueued;
	private int _recordsVersionShown = -1;

	// Strings and bools survive a C# assembly reload; the catalog itself does
	// not. These are what OnAfterDeserialize needs to rebuild it.
	private string _typesJson = "";
	private bool _scanning;

	public string WorkspacePath { get; private set; } = "";

	public bool IsScanning => _scanning;

	public int Count => _catalog.Count;

	/// `typesJson` is JSON.stringify(AssetTypes.ALL).
	public void Setup(string workspacePath, string typesJson)
	{
		WorkspacePath = workspacePath.TrimEnd('/');
		_typesJson = typesJson;
		_catalog = new Catalog(CatalogJson.ParseTypes(typesJson));
		_store = new CatalogStore(WorkspacePath);
	}

	public void OnBeforeSerialize()
	{
		// BackgroundWork's unload hook has already stopped every task.
	}

	/// Godot rebuilds C# tool objects after every build in the project and
	/// keeps only Variant-compatible fields, so the catalog comes back empty.
	/// It is reloaded from disk here; Opened tells the UI to redraw. A scan
	/// cut off by the reload is reported as stopped so the UI stops waiting.
	public void OnAfterDeserialize()
	{
		_subtypeSaveQueued = 0;
		if (WorkspacePath.Length == 0 || _typesJson.Length == 0)
			return;

		var interrupted = _scanning;
		_scanning = false;
		Setup(WorkspacePath, _typesJson);
		if (interrupted)
			BackgroundWork.OnMainThread(() => EmitSignal(SignalName.ScanFinished, 0, 0, 0, 0, true, WorkspacePath, 0.0, 0, Array.Empty<string>(), 0));
		Open();
	}

	/// Loads catalog.bin, or migrates an old index.db. Emits Opened.
	public void Open()
	{
		_work.Run(token =>
		{
			var loaded = RetryPolicy.Network.Run(() => _store.TryLoad(_catalog), token);
			if (!loaded)
				IndexDbMigration.TryMigrate(WorkspacePath, _catalog);
			var count = _catalog.Count;
			BackgroundWork.OnMainThread(() => EmitSignal(SignalName.Opened, count, !loaded));
		}, "opening the catalog");
	}

	/// Walks `absoluteRoot` (the workspace for everything) and folds what
	/// changed into the catalog. Returns false when a scan is already running.
	public bool Scan(string absoluteRoot)
	{
		if (_scanning)
			return false;

		_scanning = true;
		var cancel = CancellationTokenSource.CreateLinkedTokenSource(_work.Token);
		_scanCancel = cancel;
		var root = WorkspacePaths.Relative(WorkspacePath, absoluteRoot);

		_work.Run(_ => RunScan(root, cancel.Token), "scanning");
		return true;
	}

	public void CancelScan() => _scanCancel?.Cancel();

	/// `filterJson` is the grid's filter: prefix (absolute), tags, extensions,
	/// subtype, search, sort (GridSubbar.Sort), page, per_page. Answers with
	/// QueryReady(requestId, ...).
	public void Query(int requestId, string filterJson)
	{
		var query = CatalogJson.ParseQuery(filterJson, WorkspacePath);
		_work.Run(_ =>
		{
			var result = _catalog.Query(query);
			var items = result.Items;
			var paths = items.Select(r => WorkspacePaths.Absolute(WorkspacePath, r.RelPath)).ToArray();
			var types = items.Select(r => _catalog.Types[r.Type].Id).ToArray();
			var sizes = items.Select(r => r.Size).ToArray();
			var mtimes = items.Select(r => r.MTime).ToArray();
			var versionCounts = result.VersionCounts.ToArray();
			BackgroundWork.OnMainThread(() => EmitSignal(SignalName.QueryReady,
				requestId, paths, types, sizes, mtimes, result.Total, result.Page, result.TotalPages, versionCounts));
		}, "querying");
	}

	public void Stats(int requestId, string absolutePrefix)
	{
		var prefix = WorkspacePaths.Relative(WorkspacePath, absolutePrefix);
		_work.Run(_ =>
		{
			var json = CatalogJson.Stats(_catalog.Stats(prefix));
			BackgroundWork.OnMainThread(() => EmitSignal(SignalName.StatsReady, requestId, json));
		}, "counting");
	}

	/// Reloads when another machine wrote the store since we read it. Reads
	/// 32 bytes when nothing changed.
	public void CheckStale()
	{
		if (_scanning)
			return;
		_work.Run(token =>
		{
			if (!_store.IsStale())
				return;
			RetryPolicy.Network.Run(() => _store.TryLoad(_catalog), token);
			BackgroundWork.OnMainThread(() => EmitSignal(SignalName.Changed));
		}, "checking for changes");
	}

	/// "" when the path is not indexed.
	public string TypeOf(string absolutePath)
	{
		var record = _catalog.Get(Rel(absolutePath));
		return record == null ? "" : _catalog.Types[record.Type].Id;
	}

	public string[] TagsOf(string absolutePath) => _catalog.TagsOf(Rel(absolutePath));

	/// The file's size as last scanned, 0 when not indexed.
	public long SizeOf(string absolutePath) => _catalog.Get(Rel(absolutePath))?.Size ?? 0;

	/// Every version of the asset at `absolutePath` (its formats and exact
	/// copies), preferred first, with their sizes. Answers with VersionsReady:
	/// the first ask after the catalog changed groups the whole library, about
	/// a second, too long for the main thread.
	public void Versions(int requestId, string absolutePath)
	{
		var rel = Rel(absolutePath);
		_work.Run(_ =>
		{
			var versions = _catalog.VersionsOf(rel);
			var paths = versions.Select(v => WorkspacePaths.Absolute(WorkspacePath, v)).ToArray();
			var sizes = versions.Select(v => _catalog.Get(v)?.Size ?? 0).ToArray();
			BackgroundWork.OnMainThread(() => EmitSignal(SignalName.VersionsReady, requestId, absolutePath, paths, sizes));
		}, "finding versions");
	}

	public string[] KnownTags() => _catalog.KnownTags();

	/// Updates memory now, writes tags.bin in the background.
	public void SetTags(string absolutePath, string[] tags)
	{
		var rel = Rel(absolutePath);
		if (!_catalog.SetUserTags(rel, tags))
		{
			GD.PushError("AssetManager: SetTags called for a path that is not indexed: ", absolutePath);
			return;
		}
		_work.Run(token => RetryPolicy.Network.Run(() => _store.SaveTags(_catalog, rel), token), "saving tags");
	}

	/// `subtypes[i]` is a comma-separated list for `paths[i]`. They arrive a
	/// page of thumbnails at a time, so the catalog write is held back and
	/// batched rather than rewriting catalog.bin per page.
	public void SetSubtypes(string[] paths, string[] subtypes)
	{
		var byPath = paths.Zip(subtypes).ToDictionary(
			p => Rel(p.First),
			p => p.Second.Split(',', StringSplitOptions.RemoveEmptyEntries));
		_catalog.SetSubtypes(byPath);
		if (Interlocked.Exchange(ref _subtypeSaveQueued, 1) == 1)
			return;
		_work.Run(token =>
		{
			token.WaitHandle.WaitOne(SubtypeSaveDelay);
			Interlocked.Exchange(ref _subtypeSaveQueued, 0);
			RetryPolicy.Network.Run(() => _store.SaveCatalog(_catalog), token);
		}, "saving subtypes");
	}

	/// Call from _exit_tree. Stops every background task.
	public void Shutdown() => _work.Shutdown(TimeSpan.FromSeconds(10));

	private void RunScan(string root, CancellationToken token)
	{
		var started = DateTime.UtcNow;
		var progress = new ThrottledProgress(ProgressInterval, ReportScan);
		var scanner = new LibraryScanner(new FileSystemLister(WorkspacePath), RetryPolicy.Network)
		{
			Retrying = (folder, attempt, error) => GD.Print($"AssetManager: retrying {folder} (attempt {attempt}): {error.Message}"),
		};
		var run = new ScanRun(_catalog, scanner,
			rel => ResourceHeader.TypeOf(Path.Combine(WorkspacePath, rel)),
			() => DateTimeOffset.UtcNow.ToUnixTimeSeconds())
		{
			// Only after a full scan: the first pass over this library takes
			// hours, and adding one pack should not start it.
			Hashing = root.Length == 0
				? new DuplicateHashing(_catalog, rel => ContentHash.Of(Path.Combine(WorkspacePath, rel)), RetryPolicy.Network)
				: null,
		};
		var report = new ScanReport(new ScanSummary(), [], Cancelled: true, 0);
		try
		{
			report = run.Run(root, progress, SaveDuringScan, token);
			if (!RetryPolicy.Network.Run(() => File.Exists(_store.CatalogPath), _work.Token))
				SaveDuringScan();
		}
		finally
		{
			var summary = report.Summary;
			var failed = report.FailedFolders.Select(f => WorkspacePaths.Absolute(WorkspacePath, f)).ToArray();
			var absoluteRoot = WorkspacePaths.Absolute(WorkspacePath, root);
			var seconds = (DateTime.UtcNow - started).TotalSeconds;
			if (report.Hashed + report.Unhashed > 0)
				GD.Print($"AssetManager: read {report.Hashed:N0} files to find exact copies; {report.Unhashed:N0} could not be read and wait for the next scan");
			BackgroundWork.OnMainThread(() =>
			{
				_scanCancel?.Dispose();
				_scanCancel = null;
				_scanning = false;
				EmitSignal(SignalName.ScanFinished, summary.Added, summary.Changed, summary.Removed, _catalog.Count,
					report.Cancelled, absoluteRoot, seconds, summary.Retagged, failed, report.Retries);
			});
		}
	}

	/// Stop does not cancel a save, only shutting down does: a stopped scan
	/// keeps what it walked.
	/// Views refresh only when files came, went or changed: the copy-hashing
	/// stage saves every 2 minutes for hours, and redrawing the page under the
	/// user each time would show nothing new on it.
	private void SaveDuringScan()
	{
		RetryPolicy.Network.Run(() => _store.SaveCatalog(_catalog), _work.Token);
		var version = _catalog.RecordsVersion;
		if (Interlocked.Exchange(ref _recordsVersionShown, version) != version)
			BackgroundWork.OnMainThread(() => EmitSignal(SignalName.Changed));
	}

	private void ReportScan(ScanProgress p)
	{
		var label = p.CurrentFolder.Length == 0 ? $"{p.FilesSeen:N0} files" : $"{p.FilesSeen:N0} files - {p.CurrentFolder}";
		if (p.Stage == "layout")
			Report("scan", "Finding packs - " + p.CurrentFolder, 0, 0);
		else
			Report(p.Stage, label, p.UnitsDone, p.UnitsTotal);
	}

	private void Report(string stage, string label, int current, int total) =>
		BackgroundWork.OnMainThread(() => EmitSignal(SignalName.ScanProgressed, stage, label, current, total));

	private string Rel(string absolutePath) => WorkspacePaths.Relative(WorkspacePath, absolutePath);
}
