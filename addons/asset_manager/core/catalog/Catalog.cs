using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetManager.Core;

/// The in-memory index. Every public member is safe to call from any thread:
/// queries run on background tasks while the main thread edits tags.
/// Mutations take one lock; the slow part of a scan (walking the disk and
/// reading resource headers) happens before the lock is taken.
public sealed class Catalog
{
	private readonly object _gate = new();
	private readonly Dictionary<string, AssetRecord> _byPath = new(StringComparer.Ordinal);
	private readonly TagTable _tags = new();

	// Each sort order is built once and reused until the next mutation, so a
	// query is one filtered walk and never a sort.
	private AssetRecord[]? _byName;
	private AssetRecord[]? _bySize;
	private AssetRecord[]? _byRecent;

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

	/// Folds one walk into the catalog. Unchanged files are left alone, so a
	/// rescan of an unchanged library costs nothing but the walk. Records under
	/// `scanRoot` that the walk did not see are removed; nothing outside it is
	/// touched. `resourceTypeOf` reads a .tres header and is only called for
	/// new or changed files of types that need it.
	public ScanSummary ApplyScan(string scanRoot, IReadOnlyList<FoundFile> found, long now, Func<string, string> resourceTypeOf)
	{
		var plan = ScanPlan.Build(found, _byPath, Types, resourceTypeOf);

		lock (_gate)
		{
			foreach (var change in plan.Changes)
			{
				change.Record.Size = change.File.Size;
				change.Record.MTime = change.File.MTime;
			}

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

			var removed = _byPath.Values
				.Where(r => r.IsUnder(scanRoot) && !plan.Seen.Contains(r.RelPath))
				.Select(r => r.RelPath)
				.ToList();
			foreach (var path in removed)
				_byPath.Remove(path);

			Invalidate();
			return new ScanSummary(plan.Additions.Count, plan.Changes.Count, removed.Count, _byPath.Count);
		}
	}

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
		lock (_gate)
		{
			var used = new HashSet<int>();
			foreach (var record in _byPath.Values)
				used.UnionWith(record.Tags);
			return NamesOf(used).OrderBy(t => t, StringComparer.Ordinal).ToArray();
		}
	}

	public QueryPage Query(CatalogQuery query)
	{
		lock (_gate)
		{
			var collector = new PageCollector(Math.Max(0, query.Page), query.PerPage);
			if (TryResolveTags(query.Tags, out var tagIds))
			{
				var matcher = new CatalogMatcher(query, tagIds, _tags.Names);
				foreach (var record in InDisplayOrder(matcher, query.Sort))
					collector.Take(record);
			}

			// The filter shrank under the page the grid was on: show the last one.
			if (collector.OverShot)
				return Query(query.WithPage(collector.LastPage));

			var totalPages = collector.LastPage + 1;
			return new QueryPage(collector.Items, collector.Total, Math.Clamp(query.Page, 0, totalPages - 1), totalPages);
		}
	}

	public CatalogStats Stats(string prefix)
	{
		lock (_gate)
			return CatalogStatsBuilder.Build(_byPath.Values, prefix, _tags.Names);
	}

	/// A search puts exact filename hits first, because a fuzzy hit on a long
	/// name otherwise outranks the file the user actually typed.
	private IEnumerable<AssetRecord> InDisplayOrder(CatalogMatcher matcher, SortMode sort)
	{
		if (!matcher.HasSearch)
			return OrderFor(sort).Where(matcher.Matches);

		var byName = OrderFor(SortMode.Name);
		return byName.Where(r => matcher.Matches(r) && matcher.FileNameContainsTerm(r))
			.Concat(byName.Where(r => matcher.Matches(r) && !matcher.FileNameContainsTerm(r)));
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

	private AssetRecord[] OrderFor(SortMode sort) => sort switch
	{
		SortMode.Size => _bySize ??= _byPath.Values
			.OrderByDescending(r => r.Size).ThenBy(r => r.RelPath, StringComparer.Ordinal).ToArray(),
		SortMode.Recent => _byRecent ??= _byPath.Values
			.OrderByDescending(r => r.DateAdded).ThenBy(r => r.RelPath, StringComparer.Ordinal).ToArray(),
		_ => _byName ??= _byPath.Values
			.OrderBy(r => r.FileName, Comparer<string>.Create(TextRules.CompareNoCase))
			.ThenBy(r => r.RelPath, StringComparer.Ordinal).ToArray(),
	};

	private void Invalidate()
	{
		_byName = null;
		_bySize = null;
		_byRecent = null;
	}
}
