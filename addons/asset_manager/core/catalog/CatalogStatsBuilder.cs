using System;
using System.Collections.Generic;

namespace AssetManager.Core;

/// One pass over the index for everything the sidebar needs. Port of
/// sidebar._gather_stats, plus the rare-tag rule that used to run at import
/// time (_drop_rare_tags): a tag only one asset carries is not shown.
internal static class CatalogStatsBuilder
{
	public static CatalogStats Build(
		IEnumerable<AssetRecord> records,
		string prefix,
		IReadOnlyList<string> tagNames)
	{
		var folders = new Dictionary<string, TypeFolderStats>(StringComparer.Ordinal);
		var globalTagCounts = new int[tagNames.Count];
		var scopedTagCounts = new Dictionary<int, int>();
		var extensions = new Dictionary<string, int>(StringComparer.Ordinal);
		var total = 0;

		foreach (var r in records)
		{
			total++;
			AddFolders(folders, r.RelPath);

			var tags = r.Tags;
			foreach (var id in tags)
				globalTagCounts[id]++;

			if (!r.IsUnder(prefix))
				continue;

			foreach (var id in tags)
				scopedTagCounts[id] = scopedTagCounts.GetValueOrDefault(id) + 1;
			extensions[r.ExtensionLower] = extensions.GetValueOrDefault(r.ExtensionLower) + 1;
		}

		var visibleTags = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var (id, count) in scopedTagCounts)
		{
			if (globalTagCounts[id] > 1)
				visibleTags[tagNames[id]] = count;
		}

		return new CatalogStats
		{
			Total = total,
			Folders = folders,
			Tags = visibleTags,
			Extensions = extensions,
		};
	}

	private static void AddFolders(Dictionary<string, TypeFolderStats> folders, string relPath)
	{
		var first = relPath.IndexOf('/');
		if (first < 0)
			return;

		var typeId = relPath[..first];
		if (!folders.TryGetValue(typeId, out var stats))
			folders[typeId] = stats = new TypeFolderStats();
		stats.Count++;

		var second = relPath.IndexOf('/', first + 1);
		if (second < 0)
			return;

		var sub = relPath[(first + 1)..second];
		if (!stats.Subfolders.TryGetValue(sub, out var children))
			stats.Subfolders[sub] = children = new SortedSet<string>(StringComparer.Ordinal);

		var third = relPath.IndexOf('/', second + 1);
		if (third >= 0)
			children.Add(relPath[(second + 1)..third]);
	}
}
