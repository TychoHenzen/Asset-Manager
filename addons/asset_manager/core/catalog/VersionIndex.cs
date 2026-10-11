using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetManager.Core;

/// Which records are versions of one asset. Two records join when they share
/// a VersionKey (formats in sibling folders), or a name, size and content
/// hash (exact copies anywhere). Joins chain: a copy of one format joins that
/// format's whole group. Only records with company are indexed.
public sealed class VersionIndex
{
	private readonly Dictionary<AssetRecord, VersionGroup> _groupOf;

	private VersionIndex(Dictionary<AssetRecord, VersionGroup> groupOf, int groupCount)
	{
		_groupOf = groupOf;
		GroupCount = groupCount;
	}

	public static readonly VersionIndex Empty = new(new Dictionary<AssetRecord, VersionGroup>(ReferenceEqualityComparer.Instance), 0);

	public int GroupCount { get; }

	public int GroupedRecords => _groupOf.Count;

	/// Null for a record with no other versions.
	public VersionGroup? GroupOf(AssetRecord record) => _groupOf.GetValueOrDefault(record);

	public int CountOf(AssetRecord record) => GroupOf(record)?.Members.Count ?? 1;

	/// Each group shows once, at the place of its first match in the order,
	/// as its most preferred member that matches the filter: with a .wav
	/// filter that is the wav, without one the type's first format.
	public IEnumerable<AssetRecord> OnePerGroup(IEnumerable<AssetRecord> matches, Func<AssetRecord, bool> isMatch)
	{
		var shown = new HashSet<VersionGroup>(ReferenceEqualityComparer.Instance);
		foreach (var record in matches)
		{
			var group = GroupOf(record);
			if (group == null)
				yield return record;
			else if (shown.Add(group))
				yield return group.Members.FirstOrDefault(isMatch) ?? record;
		}
	}

	public static VersionIndex Build(IReadOnlyList<AssetRecord> records, TypeTable types)
	{
		var sets = new DisjointSets(records.Count);
		var firstByKey = new Dictionary<string, int>(StringComparer.Ordinal);
		for (var i = 0; i < records.Count; i++)
		{
			var record = records[i];
			Join(sets, firstByKey, i, VersionKey.For(record.RelPath, types[record.Type]));
			if (record.Hash != null)
				Join(sets, firstByKey, i, CopyKey(record));
		}

		// Most records are alone; only those with company are collected.
		var setSize = new int[records.Count];
		for (var i = 0; i < records.Count; i++)
			setSize[sets.Find(i)]++;
		var membersByRoot = new Dictionary<int, List<int>>();
		for (var i = 0; i < records.Count; i++)
		{
			var root = sets.Find(i);
			if (setSize[root] < 2)
				continue;
			if (!membersByRoot.TryGetValue(root, out var members))
				membersByRoot[root] = members = [];
			members.Add(i);
		}

		var groupOf = new Dictionary<AssetRecord, VersionGroup>(ReferenceEqualityComparer.Instance);
		foreach (var members in membersByRoot.Values)
		{
			var ordered = members
				.Select(i => records[i])
				.OrderBy(r => types[r.Type].ExtensionRank(r.ExtensionLower))
				.ThenBy(r => r.RelPath, StringComparer.Ordinal)
				.ToArray();
			var group = new VersionGroup(ordered);
			foreach (var record in ordered)
				groupOf[record] = group;
		}
		return new VersionIndex(groupOf, membersByRoot.Count);
	}

	/// The "\n" separators cannot occur in a path, so the two kinds of key
	/// never collide with each other.
	private static string CopyKey(AssetRecord r) =>
		$"{r.Type}\n{r.FileNameLower}\n{r.Size}\n{Convert.ToHexString(r.Hash!)}";

	private static void Join(DisjointSets sets, Dictionary<string, int> firstByKey, int index, string key)
	{
		if (firstByKey.TryGetValue(key, out var first))
			sets.Union(first, index);
		else
			firstByKey[key] = index;
	}

	private sealed class DisjointSets
	{
		private readonly int[] _parent;

		public DisjointSets(int count)
		{
			_parent = new int[count];
			for (var i = 0; i < count; i++)
				_parent[i] = i;
		}

		public int Find(int i)
		{
			while (_parent[i] != i)
			{
				_parent[i] = _parent[_parent[i]];
				i = _parent[i];
			}
			return i;
		}

		public void Union(int a, int b)
		{
			var rootA = Find(a);
			var rootB = Find(b);
			if (rootA != rootB)
				_parent[rootB] = rootA;
		}
	}
}
