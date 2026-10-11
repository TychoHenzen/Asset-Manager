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

	public bool Covers(string relPath) =>
		_shallow.Contains(RelPaths.Parent(relPath)) || _recursive.ContainsAncestorOf(relPath);

	public bool IsUnderFailedFolder(string relPath) => _failed.ContainsAncestorOf(relPath);

	/// Covered, not seen, and not under an unreadable folder.
	public bool IsGone(string relPath, IReadOnlySet<string> seen) =>
		Covers(relPath) && !seen.Contains(relPath) && !IsUnderFailedFolder(relPath);
}
