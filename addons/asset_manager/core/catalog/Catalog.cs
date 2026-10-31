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
/// Records are kept by folder, and everything derived from them (sort
/// orders, version groups) is built per type: looking at sounds never sorts
/// or groups the 1.58M images.
public sealed class Catalog
{
	private readonly object _gate = new();
	private readonly FolderTable _folders = new();
	private readonly TagArrays _tagArrays = new();
	private readonly TagTable _tags = new();
	private int _count;

	// Sort orders and version groups, per type, each built once per version
	// of what it covers: a query is one filtered walk and never a sort.
	private readonly DerivedIndexes _derived;

	// The tag editor asks on every keystroke, on the main thread, so the tags
	// in use are counted as records change rather than found by a walk.
	private readonly TagUseCounts _tagUses = new();
	private string[]? _knownTags;

	public Catalog(TypeTable types)
	{
		Types = types;
		_derived = new DerivedIndexes(types.All.Count);
	}

	public TypeTable Types { get; }

	public int Count
	{
		get { lock (_gate) return _count; }
	}

	/// Goes up whenever records are added, changed, retagged or removed; not
	/// for hashes or subtypes.
	public int RecordsVersion
	{
		get { lock (_gate) return _derived.RecordsVersion; }
	}

	/// Replaces the whole catalog. Tag ids in `records` index into `tagNames`.
	/// Each record moves into this catalog's folder of its path.
	public void Load(IEnumerable<AssetRecord> records, IReadOnlyList<string> tagNames)
	{
		lock (_gate)
		{
			_folders.Clear();
			_tagArrays.Clear();
			_count = 0;
			_tags.Clear();
			foreach (var name in tagNames)
				_tags.Intern(name);
			_tagUses.Clear();
			foreach (var record in records)
			{
				record.Folder = _folders.For(record.Folder.Path);
				record.AutoTags = _tagArrays.Intern(record.AutoTags);
				AddLocked(record);
			}
			Invalidate(AllTypes());
		}
	}

	/// A record for `relPath` in a folder of its own, for loaders that build
	/// records before calling Load; Load moves it into the shared folder.
	public static AssetRecord NewRecord(string relPath, int type) =>
		new(new AssetFolder(RelPaths.Parent(relPath)), RelPaths.FileName(relPath), type);

	public CatalogSnapshot Snapshot()
	{
		lock (_gate)
			return new CatalogSnapshot(AllRecordsLocked(), _tags.Names.ToArray());
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

		// Planning and the removal sweep read the folders outside the lock:
		// only a scan or a load changes the record set, and only one scan runs.
		var plan = ScanPlan.Build(walk, FindUnlocked, tagNames, Types, resourceTypeOf);
		var removed = RecordsWhere(
			folder => coverage.CoversFilesIn(folder.Path) && !coverage.IsFailedAtOrAbove(folder.Path),
			record => !plan.Seen.Contains(record.RelPath));

		lock (_gate)
		{
			var touched = new HashSet<int>();
			foreach (var change in plan.Changes)
			{
				change.Record.Size = change.File.Size;
				change.Record.MTime = change.File.MTime;
				change.Record.Hash = null;
				touched.Add(change.Record.Type);
			}

			foreach (var retag in plan.Retags)
			{
				ChangeTags(retag.Record, r => r.AutoTags = InternTags(retag.AutoTags));
				touched.Add(retag.Record.Type);
			}

			foreach (var add in plan.Additions)
			{
				var relPath = add.File.RelPath;
				var record = new AssetRecord(_folders.For(RelPaths.Parent(relPath)), RelPaths.FileName(relPath), add.TypeIndex)
				{
					Size = add.File.Size,
					MTime = add.File.MTime,
					DateAdded = now,
					AutoTags = InternTags(add.AutoTags),
				};
				AddLocked(record);
				touched.Add(add.TypeIndex);
			}

			RemoveLocked(removed, touched);

			var summary = new ScanSummary(plan.Additions.Count, plan.Changes.Count, removed.Count, _count, plan.Retags.Count);
			// A rescan applies a batch every 30 s, nearly all of them changing
			// nothing; rebuilding sort orders, groups and the tag list for those
			// would cost the next query and the next tag keystroke for nothing.
			if (summary.AnythingChanged)
				Invalidate(touched);
			return summary;
		}
	}

	/// The end of a whole scan: removes records under `scanRoot` that no walked
	/// unit covers (their pack folder is gone), except under folders that
	/// could not be listed. Returns how many went.
	public int RemoveUncovered(string scanRoot, ScanCoverage walked)
	{
		var removed = RecordsWhere(
			folder => folder.IsAtOrUnder(scanRoot) && !walked.CoversFilesIn(folder.Path) && !walked.IsFailedAtOrAbove(folder.Path),
			_ => true);
		if (removed.Count == 0)
			return 0;
		lock (_gate)
		{
			var touched = new HashSet<int>();
			RemoveLocked(removed, touched);
			Invalidate(touched);
		}
		return removed.Count;
	}

	private void AddLocked(AssetRecord record)
	{
		record.Folder.Files[record.FileName] = record;
		_count++;
		_tagUses.Add(record.Tags);
	}

	private void RemoveLocked(List<AssetRecord> records, HashSet<int> touched)
	{
		foreach (var record in records)
		{
			var folder = record.Folder;
			if (!folder.Files.TryGetValue(record.FileName, out var current) || current != record)
				continue;
			folder.Files.Remove(record.FileName);
			if (folder.Files.Count == 0)
				_folders.Remove(folder);
			_count--;
			_tagUses.Remove(record.Tags);
			touched.Add(record.Type);
		}
	}

	/// Every tag change goes through here, so the use counts stay right.
	private void ChangeTags(AssetRecord record, Action<AssetRecord> change)
	{
		_tagUses.Remove(record.Tags);
		change(record);
		_tagUses.Add(record.Tags);
	}

	private int[] InternTags(IEnumerable<string> names) => _tagArrays.Intern(names.Select(_tags.Intern).ToArray());

	/// The folder test runs once per folder; only the folders it passes have
	/// their records looked at.
	private List<AssetRecord> RecordsWhere(Func<AssetFolder, bool> folderTest, Func<AssetRecord, bool> recordTest)
	{
		var found = new List<AssetRecord>();
		foreach (var folder in _folders.All)
		{
			if (!folderTest(folder))
				continue;
			foreach (var record in folder.Files.Values)
			{
				if (recordTest(record))
					found.Add(record);
			}
		}
		return found;
	}

	private AssetRecord? FindUnlocked(string relPath) =>
		_folders.TryGet(RelPaths.Parent(relPath), out var folder) ? folder.Files.GetValueOrDefault(RelPaths.FileName(relPath)) : null;

	public AssetRecord? Get(string relPath)
	{
		lock (_gate)
			return FindUnlocked(relPath);
	}

	public string[] TagsOf(string relPath)
	{
		lock (_gate)
			return FindUnlocked(relPath) is { } r ? NamesOf(r.Tags) : [];
	}

	/// Returns false for an unknown path.
	public bool SetUserTags(string relPath, IEnumerable<string> tags)
	{
		lock (_gate)
		{
			if (FindUnlocked(relPath) is not { } record)
				return false;
			ChangeTags(record, r => r.UserTags = tags.Select(_tags.Intern).Distinct().ToArray());
			TagsChanged();
			return true;
		}
	}

	/// Every user override, by path, for tags.bin.
	public Dictionary<string, string[]> UserTagOverrides()
	{
		lock (_gate)
		{
			return AllRecordsLocked()
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
				if (FindUnlocked(path) is { } record)
					ChangeTags(record, r => r.UserTags = tags.Select(_tags.Intern).Distinct().ToArray());
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
				if (FindUnlocked(path) is { } record)
					record.Subtypes = values;
			}
		}
	}

	/// Every tag some asset currently carries, sorted. Built from the use
	/// counts, a few milliseconds for the 14k tags of the library.
	public string[] KnownTags()
	{
		lock (_gate)
			return _knownTags ??= _tagUses.Used().Select(id => _tags[id]).OrderBy(t => t, StringComparer.Ordinal).ToArray();
	}

	/// Runs outside the lock. A record removed meanwhile can still appear in
	/// this one answer; the next query will not have it.
	public QueryPage Query(CatalogQuery query)
	{
		// ASSUMPTION: a record's type is the type of its bucket folder, as
		// ScanPlan.TypeFor decides it, so a prefix inside one bucket only ever
		// matches records of that type.
		var scope = Types.IndexOf(RelPaths.Bucket(query.Prefix + "/"));
		var order = OrderFor(scope, query.SearchTermLower.Length > 0 ? SortMode.Name : query.Sort);
		var versions = query.GroupVersions ? VersionsFor(scope) : VersionIndex.Empty;
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

	/// Which records are versions of which, for one type (`scope`) or all of
	/// them (-1). Versions never cross types, so all of them is each type's
	/// index side by side.
	private VersionIndex VersionsFor(int scope) =>
		scope >= 0 ? VersionsOfType(scope) : VersionIndex.ByType(Enumerable.Range(0, Types.All.Count).Select(VersionsOfType).ToArray());

	/// Built outside the lock and kept until the type's records or hashes
	/// change.
	private VersionIndex VersionsOfType(int type)
	{
		AssetRecord[] records;
		DerivedIndexes.Stamp builtFor;
		lock (_gate)
		{
			if (_derived.TryGetVersions(type) is { } cached)
				return cached;
			builtFor = _derived.VersionsStamp(type);
			records = RecordsLocked(type);
		}

		var versions = VersionIndex.Build(records, type, Types[type]);
		lock (_gate)
			_derived.KeepVersions(type, builtFor, versions);
		return versions;
	}

	/// The paths of every version of `relPath`'s asset, preferred first, or
	/// just `relPath` when it has none. Empty for a path not indexed.
	public string[] VersionsOf(string relPath)
	{
		var record = Get(relPath);
		if (record == null)
			return [];
		var group = VersionsOfType(record.Type).GroupOf(record);
		return group == null ? [relPath] : group.Members.Select(r => r.RelPath).ToArray();
	}

	/// Stores a hash read from a file of `size` and `mtime`. Ignored when the
	/// record changed or went away since: the hash would describe old content.
	public bool SetHash(AssetRecord record, long size, long mtime, byte[] hash)
	{
		lock (_gate)
		{
			if (!record.Folder.Files.TryGetValue(record.FileName, out var current) || current != record
				|| record.Size != size || record.MTime != mtime)
				return false;
			record.Hash = hash;
			_derived.HashChanged(record.Type);
			return true;
		}
	}

	public CatalogStats Stats(string prefix)
	{
		AssetRecord[] records;
		string[] tagNames;
		lock (_gate)
		{
			records = AllRecordsLocked();
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

	/// Records in folder order, so the files of one folder sit together.
	private AssetRecord[] AllRecordsLocked()
	{
		var all = new AssetRecord[_count];
		var i = 0;
		foreach (var folder in _folders.All)
		{
			foreach (var record in folder.Files.Values)
				all[i++] = record;
		}
		return all;
	}

	private AssetRecord[] RecordsLocked(int type)
	{
		var found = new List<AssetRecord>();
		foreach (var folder in _folders.All)
		{
			foreach (var record in folder.Files.Values)
			{
				if (record.Type == type)
					found.Add(record);
			}
		}
		return found.ToArray();
	}

	/// `scope` is a type index, or -1 for every type. The order of every type
	/// is the types' own orders merged, so the All view and a type's view
	/// share one sort, whichever comes first.
	private AssetRecord[] OrderFor(int scope, SortMode sort)
	{
		AssetRecord[]? records = null;
		int version;
		lock (_gate)
		{
			if (_derived.TryGetOrder(scope, sort) is { } cached)
				return cached;
			version = _derived.OrderStamp(scope);
			if (scope >= 0)
				records = RecordsLocked(scope);
		}

		var order = records != null
			? SortOrders.Build(records, sort)
			: SortOrders.Merge(AllTypes().Select(type => OrderFor(type, sort)).ToArray(), sort);
		lock (_gate)
			_derived.KeepOrder(scope, sort, version, order);
		return order;
	}

	/// Whether a sort order or version index of `type` is being kept: a query
	/// of another type should leave it unbuilt.
	internal bool HasIndexesFor(int type)
	{
		lock (_gate)
			return _derived.Holds(type);
	}

	private IEnumerable<int> AllTypes() => Enumerable.Range(0, Types.All.Count);

	/// The records of `types` changed.
	private void Invalidate(IEnumerable<int> types)
	{
		_derived.RecordsChanged(types);
		TagsChanged();
	}

	private void TagsChanged() => _knownTags = null;
}
