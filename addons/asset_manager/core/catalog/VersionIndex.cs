using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetManager.Core;

/// Which records of one type are versions of one asset. Two records join
/// when they share a version key (formats in sibling folders), or a name,
/// size and content hash (exact copies anywhere). Joins chain: a copy of one
/// format joins that format's whole group. Only records with company are
/// indexed. Both kinds of key need the same type, so each type is grouped on
/// its own and the All view puts the types' indexes side by side.
public sealed class VersionIndex
{
	private readonly Func<AssetRecord, VersionGroup?> _groupOf;

	private VersionIndex(Func<AssetRecord, VersionGroup?> groupOf, int groupCount, int groupedRecords)
	{
		_groupOf = groupOf;
		GroupCount = groupCount;
		GroupedRecords = groupedRecords;
	}

	public static readonly VersionIndex Empty = new(_ => null, 0, 0);

	public int GroupCount { get; }

	public int GroupedRecords { get; }

	/// Null for a record with no other versions.
	public VersionGroup? GroupOf(AssetRecord record) => _groupOf(record);

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

	/// The index of every type at once; `byType[i]` covers type i.
	public static VersionIndex ByType(IReadOnlyList<VersionIndex> byType) =>
		new(r => byType[r.Type].GroupOf(r), byType.Sum(v => v.GroupCount), byType.Sum(v => v.GroupedRecords));

	/// `records` are all of type `typeIndex`. The keys are compared in place
	/// (VersionKeys, CopyKeys): a string per record would cost another copy of
	/// every path.
	public static VersionIndex Build(IReadOnlyList<AssetRecord> records, int typeIndex, AssetType type)
	{
		var sets = new DisjointSets(records.Count);
		var byName = new Dictionary<AssetRecord, int>(new NameKeys(typeIndex, type));
		var byContent = new Dictionary<AssetRecord, int>(CopyKeys.Instance);
		for (var i = 0; i < records.Count; i++)
		{
			var record = records[i];
			Join(sets, byName, i, record);
			if (record.Hash != null)
				Join(sets, byContent, i, record);
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
			var ordered = members.Select(i => records[i]).ToArray();
			Array.Sort(ordered, (a, b) =>
			{
				var byRank = type.ExtensionRank(a.ExtensionLower).CompareTo(type.ExtensionRank(b.ExtensionLower));
				return byRank != 0 ? byRank : AssetRecord.ComparePaths(a, b);
			});
			var group = new VersionGroup(ordered);
			foreach (var record in ordered)
				groupOf[record] = group;
		}
		return new VersionIndex(r => groupOf.GetValueOrDefault(r), membersByRoot.Count, groupOf.Count);
	}

	private static void Join(DisjointSets sets, Dictionary<AssetRecord, int> firstByKey, int index, AssetRecord record)
	{
		if (firstByKey.TryGetValue(record, out var first))
			sets.Union(first, index);
		else
			firstByKey[record] = index;
	}

	/// VersionKey.For, compared without building it: the folder's key (kept
	/// per folder) and the stem, ignoring case.
	private sealed class NameKeys(int typeIndex, AssetType type) : IEqualityComparer<AssetRecord>
	{
		public bool Equals(AssetRecord? a, AssetRecord? b) =>
			ReferenceEquals(a, b)
			|| (a != null && b != null
				&& Folder(a) == Folder(b)
				&& TextRules.EqualsLower(VersionKey.Stem(a.FileName), VersionKey.Stem(b.FileName)));

		public int GetHashCode(AssetRecord r) =>
			HashCode.Combine(Folder(r), TextRules.HashLower(VersionKey.Stem(r.FileName)));

		private string Folder(AssetRecord r) => r.Folder.VersionFolder(typeIndex, type);
	}

	/// Exact copies: type, name ignoring case, size and content hash.
	private sealed class CopyKeys : IEqualityComparer<AssetRecord>
	{
		public static readonly CopyKeys Instance = new();

		public bool Equals(AssetRecord? a, AssetRecord? b) =>
			ReferenceEquals(a, b)
			|| (a != null && b != null
				&& SameNameAndSize.Instance.Equals(a, b)
				&& a.Hash.AsSpan().SequenceEqual(b.Hash));

		public int GetHashCode(AssetRecord r) => SameNameAndSize.Instance.GetHashCode(r);
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
