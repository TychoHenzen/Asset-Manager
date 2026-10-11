using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetManager.Core;

/// The in-memory index. Every public member is safe to call from any thread:
/// queries run on background tasks while the main thread edits tags.
/// The lock is only held for dictionary work. Anything that walks or sorts
/// the whole record set (queries, stats, known tags, scan planning) takes a
/// snapshot under the lock and does the slow part outside it, so a
/// main-thread call like TagsOf never waits behind a query.
public sealed class Catalog
{
	private readonly object _gate = new();
	private readonly Dictionary<string, AssetRecord> _byPath = new(StringComparer.Ordinal);
	private readonly TagTable _tags = new();

	// Each sort order is built once per version of the record set, so a query
	// is one filtered walk and never a sort.
	private readonly SortOrders _orders = new();
	private int _recordsVersion;

	// The tag editor asks on every keystroke; walking 300k records each time
	// is too slow for the main thread.
	private string[]? _knownTags;
	private int _tagsVersion;

	// Grouping walks every record, so it is built once per version of the
	// record set and of the content hashes, like the sort orders.
	private VersionIndex? _versions;
	private (int Records, int Hashes) _versionsBuiltFor = (-1, -1);
	private int _hashesVersion;

	public Catalog(TypeTable types) => Types = types;

	public TypeTable Types { get; }

	public int Count
	{
		get { lock (_gate) return _byPath.Count; }
	}

	/// Replaces the whole catalog. Tag ids in `records` index into `tagNames`.
	public void Load(IEnumerable<AssetRecord> records, IReadOnlyList<string> tagNames)
	{
		lock (_gate)
		{
			_byPath.Clear();
			_tags.Clear();
			foreach (var name in tagNames)
				_tags.Intern(name);
			foreach (var record in records)
				_byPath[record.RelPath] = record;
			Invalidate();
		}
	}

	public CatalogSnapshot Snapshot()
	{
		lock (_gate)
			return new CatalogSnapshot(_byPath.Values.ToArray(), _tags.Names.ToArray());
	}

	/// Folds a batch of walked units into the catalog. Unchanged files keep
	/// their record, so a rescan of an unchanged library costs the walk and a
	/// tag comparison. Records the batch covers but did not see are removed,
	/// except under folders that could not be listed; nothing else is touched.
	/// `resourceTypeOf` reads a .tres header and is only called for new or
	/// changed files of types that need it.
	public ScanSummary ApplyScan(ScanResult walk, ScanCoverage coverage, long now, Func<string, string> resourceTypeOf)
	{
		string[] tagNames;
		lock (_gate)
			tagNames = _tags.Names.ToArray();

		// Planning and the removal sweep read the dictionary outside the lock:
		// only a scan or a load changes the record set, and only one scan runs.
		var plan = ScanPlan.Build(walk, _byPath, tagNames, Types, resourceTypeOf);
		var removed = PathsWhere(r => coverage.IsGone(r.RelPath, plan.Seen));

		lock (_gate)
		{
			foreach (var change in plan.Changes)
			{
				change.Record.Size = change.File.Size;
				change.Record.MTime = change.File.MTime;
				change.Record.Hash = null;
			}

			foreach (var retag in plan.Retags)
				retag.Record.AutoTags = retag.AutoTags.Select(_tags.Intern).ToArray();

			foreach (var add in plan.Additions)
			{
				_byPath[add.File.RelPath] = new AssetRecord(add.File.RelPath, add.TypeIndex)
				{
					Size = add.File.Size,
					MTime = add.File.MTime,
					DateAdded = now,
					AutoTags = add.AutoTags.Select(_tags.Intern).ToArray(),
				};
			}

			foreach (var path in removed)
				_byPath.Remove(path);

			Invalidate();
			return new ScanSummary(plan.Additions.Count, plan.Changes.Count, removed.Count, _byPath.Count, plan.Retags.Count);
		}
	}

	/// The end of a whole scan: removes records under `scanRoot` that no walked
	/// unit covers (their pack folder is gone), except under folders that
	/// could not be listed. Returns how many went.
	public int RemoveUncovered(string scanRoot, ScanCoverage walked)
	{
		var removed = PathsWhere(r => r.IsUnder(scanRoot) && !walked.Covers(r.RelPath) && !walked.IsUnderFailedFolder(r.RelPath));
		if (removed.Count == 0)
			return 0;
		lock (_gate)
		{
			foreach (var path in removed)
				_byPath.Remove(path);
			Invalidate();
		}
		return removed.Count;
	}

	private List<string> PathsWhere(Func<AssetRecord, bool> predicate) =>
		_byPath.Values.Where(predicate).Select(r => r.RelPath).ToList();

	public AssetRecord? Get(string relPath)
	{
		lock (_gate)
			return _byPath.GetValueOrDefault(relPath);
	}

	public string[] TagsOf(string relPath)
	{
		lock (_gate)
			return _byPath.TryGetValue(relPath, out var r) ? NamesOf(r.Tags) : [];
	}

	/// Returns false for an unknown path.
	public bool SetUserTags(string relPath, IEnumerable<string> tags)
	{
		lock (_gate)
		{
			if (!_byPath.TryGetValue(relPath, out var record))
				return false;
			record.UserTags = tags.Select(_tags.Intern).Distinct().ToArray();
			TagsChanged();
			return true;
		}
	}

	/// Every user override, by path, for tags.bin.
	public Dictionary<string, string[]> UserTagOverrides()
	{
		lock (_gate)
		{
			return _byPath.Values
				.Where(r => r.UserTags != null)
				.ToDictionary(r => r.RelPath, r => NamesOf(r.UserTags!), StringComparer.Ordinal);
		}
	}

	/// Overrides read back from tags.bin. Paths no longer indexed are skipped.
	public void ApplyUserTagOverrides(IReadOnlyDictionary<string, string[]> overrides)
	{
		lock (_gate)
		{
			foreach (var (path, tags) in overrides)
			{
				if (_byPath.TryGetValue(path, out var record))
					record.UserTags = tags.Select(_tags.Intern).Distinct().ToArray();
			}
			TagsChanged();
		}
	}

	public void SetSubtypes(IReadOnlyDictionary<string, string[]> subtypes)
	{
		lock (_gate)
		{
			foreach (var (path, values) in subtypes)
			{
				if (_byPath.TryGetValue(path, out var record))
					record.Subtypes = values;
			}
		}
	}

	/// Every tag some asset currently carries, sorted.
	public string[] KnownTags()
	{
		AssetRecord[] records;
		string[] names;
		int version;
		lock (_gate)
		{
			if (_knownTags != null)
				return _knownTags;
			records = _byPath.Values.ToArray();
			names = _tags.Names.ToArray();
			version = _tagsVersion;
		}

		var used = new HashSet<int>();
		foreach (var record in records)
			used.UnionWith(record.Tags);
		var known = used.Select(id => names[id]).OrderBy(t => t, StringComparer.Ordinal).ToArray();

		lock (_gate)
		{
			if (version == _tagsVersion)
				_knownTags = known;
		}
		return known;
	}

	/// Runs outside the lock. A record removed meanwhile can still appear in
	/// this one answer; the next query will not have it.
	public QueryPage Query(CatalogQuery query)
	{
		var order = OrderFor(query.SearchTermLower.Length > 0 ? SortMode.Name : query.Sort);
		var versions = query.GroupVersions ? Versions() : VersionIndex.Empty;
		var collector = new PageCollector(Math.Max(0, query.Page), query.PerPage);

		int[] tagIds;
		string[] tagNames;
		bool resolved;
		lock (_gate)
		{
			resolved = TryResolveTags(query.Tags, out tagIds);
			tagNames = _tags.Names.ToArray();
		}

		if (resolved)
		{
			var matcher = new CatalogMatcher(query, tagIds, tagNames);
			foreach (var record in versions.OnePerGroup(InDisplayOrder(matcher, order), matcher.Matches))
				collector.Take(record);
		}

		// The filter shrank under the page the grid was on: show the last one.
		if (collector.OverShot)
			return Query(query.WithPage(collector.LastPage));

		var totalPages = collector.LastPage + 1;
		var counts = collector.Items.Select(versions.CountOf).ToArray();
		return new QueryPage(collector.Items, collector.Total, Math.Clamp(query.Page, 0, totalPages - 1), totalPages, counts);
	}

	/// Which records are versions of which. Built outside the lock and kept
	/// until the record set or a hash changes.
	public VersionIndex Versions()
	{
		AssetRecord[] records;
		(int, int) builtFor;
		lock (_gate)
		{
			builtFor = (_recordsVersion, _hashesVersion);
			if (_versions != null && _versionsBuiltFor == builtFor)
				return _versions;
			records = _byPath.Values.ToArray();
		}

		var versions = VersionIndex.Build(records, Types);
		lock (_gate)
		{
			if (builtFor == (_recordsVersion, _hashesVersion))
			{
				_versions = versions;
				_versionsBuiltFor = builtFor;
			}
		}
		return versions;
	}

	/// The paths of every version of `relPath`'s asset, preferred first, or
	/// just `relPath` when it has none. Empty for a path not indexed.
	public string[] VersionsOf(string relPath)
	{
		var record = Get(relPath);
		if (record == null)
			return [];
		var group = Versions().GroupOf(record);
		return group == null ? [relPath] : group.Members.Select(r => r.RelPath).ToArray();
	}

	/// Stores a hash read from a file of `size` and `mtime`. Ignored when the
	/// record changed or went away since: the hash would describe old content.
	public bool SetHash(AssetRecord record, long size, long mtime, byte[] hash)
	{
		lock (_gate)
		{
			if (!_byPath.TryGetValue(record.RelPath, out var current) || current != record
				|| record.Size != size || record.MTime != mtime)
				return false;
			record.Hash = hash;
			_hashesVersion++;
			return true;
		}
	}

	public CatalogStats Stats(string prefix)
	{
		AssetRecord[] records;
		string[] tagNames;
		lock (_gate)
		{
			records = _byPath.Values.ToArray();
			tagNames = _tags.Names.ToArray();
		}
		return CatalogStatsBuilder.Build(records, prefix, tagNames);
	}

	/// A search puts exact filename hits first, because a fuzzy hit on a long
	/// name otherwise outranks the file the user actually typed. `order` is
	/// by name whenever there is a search.
	private static IEnumerable<AssetRecord> InDisplayOrder(CatalogMatcher matcher, AssetRecord[] order)
	{
		if (!matcher.HasSearch)
			return order.Where(matcher.Matches);

		return order.Where(r => matcher.Matches(r) && matcher.FileNameContainsTerm(r))
			.Concat(order.Where(r => matcher.Matches(r) && !matcher.FileNameContainsTerm(r)));
	}

	/// False when a wanted tag is unknown, which no asset can match.
	private bool TryResolveTags(IReadOnlyList<string> tags, out int[] ids)
	{
		ids = new int[tags.Count];
		for (var i = 0; i < ids.Length; i++)
		{
			if (!_tags.TryGetId(tags[i], out ids[i]))
				return false;
		}
		return true;
	}

	private string[] NamesOf(IEnumerable<int> ids) => ids.Select(id => _tags[id]).ToArray();

	private AssetRecord[] OrderFor(SortMode sort)
	{
		AssetRecord[] records;
		int version;
		lock (_gate)
		{
			var cached = _orders.TryGet(sort, _recordsVersion);
			if (cached != null)
				return cached;
			records = _byPath.Values.ToArray();
			version = _recordsVersion;
		}

		var order = SortOrders.Build(records, sort);
		lock (_gate)
			_orders.Keep(sort, version, _recordsVersion, order);
		return order;
	}

	private void Invalidate()
	{
		_recordsVersion++;
		_orders.Clear();
		TagsChanged();
	}

	private void TagsChanged()
	{
		_tagsVersion++;
		_knownTags = null;
	}
}
