using System;
using System.Collections.Generic;
using System.Threading;

namespace AssetManager.Core;

/// Walks the workspace one folder at a time, so a failed request costs one
/// retried folder instead of the whole walk. The library sits on a LAN share
/// that drops single requests at random points of a walk of a million files.
/// The walk is split into units, one per pack folder, which ScanRun applies
/// to the catalog as they finish.
public sealed class LibraryScanner(IDirectoryLister lister, RetryPolicy retry)
{
	/// `type/vendor/category/pack`: the depth of the folders walked as units.
	public const int PackDepth = 4;

	private int _retries;

	/// Called for each failed request that will be retried: the folder, the
	/// attempt that failed, and why.
	public Action<string, int, Exception>? Retrying { get; init; }

	public int Retries => _retries;

	/// Lists the folders above pack depth. `scanRoot` is workspace-relative:
	/// "" walks every type bucket, anything else that subtree only and must
	/// start with a type folder. `onFolder` is told each folder listed.
	public ScanLayout Layout(string scanRoot, TypeTable types, CancellationToken cancel, Action<string>? onFolder = null)
	{
		var layout = new ScanLayout([], [], MarkedAncestors(scanRoot, cancel), []);
		if (FolderSet.Depth(scanRoot) >= PackDepth)
		{
			layout.Units.Add(scanRoot);
			return layout;
		}

		var queue = new Queue<string>();
		if (scanRoot.Length > 0)
			queue.Enqueue(scanRoot);
		else
		{
			foreach (var type in types.All)
				queue.Enqueue(type.Id);
		}

		while (queue.Count > 0)
		{
			var dir = queue.Dequeue();
			if (!TryList(dir, cancel, out var entries))
			{
				layout.FailedDirs.Add(dir);
				continue;
			}

			var files = new List<FoundFile>();
			foreach (var entry in entries)
			{
				var path = Join(dir, entry.Name);
				if (!entry.IsDirectory)
					AddFile(dir, entry, files, layout.VendorRoots);
				else if (FolderSet.Depth(path) >= PackDepth)
					layout.Units.Add(path);
				else
					queue.Enqueue(path);
			}
			layout.Shallow.Add(new UnitResult(dir, Recursive: false, files, []));
			onFolder?.Invoke(dir);
		}

		layout.Units.Sort(StringComparer.Ordinal);
		return layout;
	}

	/// Walks `root` and everything below it. Folders that stay unreadable are
	/// reported and skipped; the rest of the unit is still walked.
	/// `onFolder(soFar, folder)` is told after each folder, with the result as
	/// it stands.
	public UnitResult WalkUnit(string root, HashSet<string> vendorRoots, CancellationToken cancel, Action<UnitResult, string>? onFolder = null)
	{
		var result = new UnitResult(root, Recursive: true, [], []);
		var queue = new Queue<string>();
		queue.Enqueue(root);
		while (queue.Count > 0)
		{
			var dir = queue.Dequeue();
			if (!TryList(dir, cancel, out var entries))
			{
				result.FailedDirs.Add(dir);
				continue;
			}
			foreach (var entry in entries)
			{
				if (entry.IsDirectory)
					queue.Enqueue(Join(dir, entry.Name));
				else
					AddFile(dir, entry, result.Files, vendorRoots);
			}
			onFolder?.Invoke(result, dir);
		}
		return result;
	}

	private bool TryList(string dir, CancellationToken cancel, out IReadOnlyList<DirEntry> entries) =>
		retry.TryRun(() => lister.List(dir), cancel, out entries, out _, (attempt, error) =>
		{
			Interlocked.Increment(ref _retries);
			Retrying?.Invoke(dir, attempt, error);
		});

	/// Vendor marker files mark their folder rather than being assets.
	private static void AddFile(string dir, DirEntry entry, List<FoundFile> files, HashSet<string> vendorRoots)
	{
		if (entry.Name == VendorMarker.FileName)
			vendorRoots.Add(dir);
		else
			files.Add(new FoundFile(Join(dir, entry.Name), entry.Size, entry.MTime));
	}

	/// A scoped scan starts below the vendor folder, so the walk never sees the
	/// marker; the folders above the scan root are listed for it instead. A
	/// folder that cannot be listed just counts as unmarked.
	private HashSet<string> MarkedAncestors(string scanRoot, CancellationToken cancel)
	{
		var marked = new HashSet<string>(StringComparer.Ordinal);
		var slash = scanRoot.IndexOf('/');
		while (slash > 0)
		{
			var ancestor = scanRoot[..slash];
			if (TryList(ancestor, cancel, out var entries) && HasMarker(entries))
				marked.Add(ancestor);
			slash = scanRoot.IndexOf('/', slash + 1);
		}
		return marked;
	}

	private static bool HasMarker(IReadOnlyList<DirEntry> entries)
	{
		foreach (var entry in entries)
		{
			if (!entry.IsDirectory && entry.Name == VendorMarker.FileName)
				return true;
		}
		return false;
	}

	private static string Join(string dir, string name) => dir.Length == 0 ? name : dir + "/" + name;
}
