using System.Collections.Generic;

namespace AssetManager.Core;

/// Which records a set of walked units speaks for. A record the units cover
/// but did not see is gone from disk; one under a folder that could not be
/// listed is unknown, and stays.
public sealed class ScanCoverage
{
	private readonly FolderSet _recursive = new();
	private readonly FolderSet _shallow = new();
	private readonly FolderSet _failed = new();

	public static ScanCoverage Of(IEnumerable<UnitResult> units)
	{
		var coverage = new ScanCoverage();
		coverage.AddRange(units);
		return coverage;
	}

	public void AddRange(IEnumerable<UnitResult> units)
	{
		foreach (var unit in units)
		{
			(unit.Recursive ? _recursive : _shallow).Add(unit.Root);
			foreach (var dir in unit.FailedDirs)
				_failed.Add(dir);
		}
	}

	/// True when the walked units speak for the files directly in `folder`.
	/// Asked once per folder rather than once per record.
	public bool CoversFilesIn(string folder) =>
		_shallow.Contains(folder) || _recursive.Contains(folder) || _recursive.ContainsAncestorOf(folder);

	/// True when `folder` or a folder above it could not be listed: what is
	/// indexed under it is unknown, and stays.
	public bool IsFailedAtOrAbove(string folder) => _failed.Contains(folder) || _failed.ContainsAncestorOf(folder);
}
