using System;
using System.Collections.Generic;

namespace AssetManager.Core;

/// Workspace-relative folders, asked "is this path inside one of them". The
/// folders of one scan sit at a handful of depths (pack folders all at four),
/// so a lookup is one prefix per depth, not a walk over the set.
internal sealed class FolderSet
{
	private readonly HashSet<string> _folders = new(StringComparer.Ordinal);
	private readonly SortedSet<int> _depths = [];

	public int Count => _folders.Count;

	public void Add(string relDir)
	{
		if (_folders.Add(relDir))
			_depths.Add(Depth(relDir));
	}

	/// True when `relPath` lies strictly inside one of the folders.
	public bool ContainsAncestorOf(string relPath)
	{
		foreach (var depth in _depths)
		{
			if (depth == 0)
				return true;
			var end = SegmentEnd(relPath, depth);
			if (end < 0)
				return false;
			if (_folders.Contains(relPath[..end]))
				return true;
		}
		return false;
	}

	public bool Contains(string relDir) => _folders.Contains(relDir);

	/// "" is the workspace itself, depth 0; "images/kenney" is depth 2.
	public static int Depth(string relDir)
	{
		if (relDir.Length == 0)
			return 0;
		var depth = 1;
		foreach (var c in relDir)
		{
			if (c == '/')
				depth++;
		}
		return depth;
	}

	/// Index of the "/" that ends the first `depth` segments, or -1 when
	/// `relPath` has no more than `depth` segments.
	private static int SegmentEnd(string relPath, int depth)
	{
		var index = -1;
		for (var i = 0; i < depth; i++)
		{
			index = relPath.IndexOf('/', index + 1);
			if (index < 0)
				return -1;
		}
		return index;
	}
}
