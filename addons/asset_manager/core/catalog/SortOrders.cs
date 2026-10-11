using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetManager.Core;

/// Each sort order of one type's records (or of every type, scope -1), built
/// once per version of those records. Building one sorts up to 1.77M records
/// (seconds for names), so it happens outside the catalog's lock and is only
/// kept if the records did not change while it was being built. Not
/// thread-safe on its own; Catalog guards it.
internal sealed class SortOrders
{
	private readonly Dictionary<(int Scope, SortMode Sort), (int Version, AssetRecord[] Order)> _cached = [];

	public AssetRecord[]? TryGet(int scope, SortMode sort, int version) =>
		_cached.TryGetValue((scope, sort), out var entry) && entry.Version == version ? entry.Order : null;

	public void Keep(int scope, SortMode sort, int builtFor, int current, AssetRecord[] order)
	{
		if (builtFor == current)
			_cached[(scope, sort)] = (builtFor, order);
	}

	public bool Holds(int scope) => _cached.Keys.Any(k => k.Scope == scope);

	/// Forgets every order of `scope`, so a stale one is not held until the
	/// next query of it.
	public void Drop(int scope)
	{
		foreach (var key in _cached.Keys.Where(k => k.Scope == scope).ToList())
			_cached.Remove(key);
	}

	/// Sorts `records` in place and returns it. Paths are unique, so the path
	/// tie-break makes every order total and the unstable sort deterministic.
	public static AssetRecord[] Build(AssetRecord[] records, SortMode sort)
	{
		Array.Sort(records, ComparisonFor(sort));
		return records;
	}

	/// One order out of several already in `sort` order, such as each type's.
	public static AssetRecord[] Merge(IReadOnlyList<AssetRecord[]> orders, SortMode sort)
	{
		var merged = new AssetRecord[orders.Sum(o => o.Length)];
		var heads = new PriorityQueue<int, AssetRecord>(Comparer<AssetRecord>.Create(ComparisonFor(sort)));
		var next = new int[orders.Count];
		for (var i = 0; i < orders.Count; i++)
		{
			if (orders[i].Length > 0)
				heads.Enqueue(i, orders[i][0]);
		}
		var count = 0;
		while (heads.TryDequeue(out var i, out var record))
		{
			merged[count++] = record;
			if (++next[i] < orders[i].Length)
				heads.Enqueue(i, orders[i][next[i]]);
		}
		return merged;
	}

	private static Comparison<AssetRecord> ComparisonFor(SortMode sort) => sort switch
	{
		SortMode.Size => BySize,
		SortMode.Recent => ByRecent,
		_ => ByName,
	};

	private static readonly Comparison<AssetRecord> ByName = (a, b) =>
	{
		var byName = TextRules.CompareNoCase(a.FileName, b.FileName);
		return byName != 0 ? byName : AssetRecord.ComparePaths(a, b);
	};

	private static readonly Comparison<AssetRecord> BySize = (a, b) =>
	{
		var bySize = b.Size.CompareTo(a.Size);
		return bySize != 0 ? bySize : AssetRecord.ComparePaths(a, b);
	};

	private static readonly Comparison<AssetRecord> ByRecent = (a, b) =>
	{
		var byDate = b.DateAdded.CompareTo(a.DateAdded);
		return byDate != 0 ? byDate : AssetRecord.ComparePaths(a, b);
	};
}
