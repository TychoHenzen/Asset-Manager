using System;
using System.Collections.Generic;

namespace AssetManager.Core;

/// One shared copy of each distinct tag-id array. The files of a folder get
/// the same auto tags, so 1.77M records hold 82k distinct arrays.
internal sealed class TagArrays
{
	private readonly Dictionary<int[], int[]> _shared = new(Contents.Instance);

	public int[] Intern(int[] ids)
	{
		if (ids.Length == 0)
			return [];
		if (!_shared.TryGetValue(ids, out var shared))
			_shared[ids] = shared = ids;
		return shared;
	}

	public void Clear() => _shared.Clear();

	internal sealed class Contents : IEqualityComparer<int[]>
	{
		public static readonly Contents Instance = new();

		public bool Equals(int[]? a, int[]? b) => a.AsSpan().SequenceEqual(b);

		public int GetHashCode(int[] ids)
		{
			var hash = new HashCode();
			foreach (var id in ids)
				hash.Add(id);
			return hash.ToHashCode();
		}
	}
}
