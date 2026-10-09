using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetManager.Core;

/// Each sort order of the record set, built once per version of it. Building
/// one sorts 300k records (about a second for names), so it happens outside
/// the catalog's lock and is only kept if the record set did not change while
/// it was being built. Not thread-safe on its own; Catalog guards it.
internal sealed class SortOrders
{
	private readonly Dictionary<SortMode, (int Version, AssetRecord[] Order)> _cached = [];

	public AssetRecord[]? TryGet(SortMode sort, int version) =>
		_cached.TryGetValue(sort, out var entry) && entry.Version == version ? entry.Order : null;

	public void Keep(SortMode sort, int builtFor, int current, AssetRecord[] order)
	{
		if (builtFor == current)
			_cached[sort] = (builtFor, order);
	}

	public void Clear() => _cached.Clear();

	public static AssetRecord[] Build(AssetRecord[] records, SortMode sort) => sort switch
	{
		SortMode.Size => records
			.OrderByDescending(r => r.Size).ThenBy(r => r.RelPath, StringComparer.Ordinal).ToArray(),
		SortMode.Recent => records
			.OrderByDescending(r => r.DateAdded).ThenBy(r => r.RelPath, StringComparer.Ordinal).ToArray(),
		_ => records
			.OrderBy(r => r.FileName, Comparer<string>.Create(TextRules.CompareNoCase))
			.ThenBy(r => r.RelPath, StringComparer.Ordinal).ToArray(),
	};
}
