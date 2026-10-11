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
		AssetFolder? lastFolder = null;
		TypeFolderStats? lastStats = null;

		foreach (var r in records)
		{
			total++;
			// Records come folder by folder, so the folder's place in the tree
			// is worked out once per folder, not once per file.
			if (!ReferenceEquals(r.Folder, lastFolder))
			{
				lastFolder = r.Folder;
				lastStats = AddFolders(folders, r.Folder.Path);
			}
			if (lastStats != null)
				lastStats.Count++;

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

	/// Puts the folder of a file into the tree and returns its type's entry
	/// for the caller to count the file in, null for a folder outside any type.
	private static TypeFolderStats? AddFolders(Dictionary<string, TypeFolderStats> folders, string folder)
	{
		if (folder.Length == 0)
			return null;
		var segments = folder.Split('/', 4);

		var typeId = segments[0];
		if (!folders.TryGetValue(typeId, out var stats))
			folders[typeId] = stats = new TypeFolderStats();

		if (segments.Length < 2)
			return stats;

		var sub = segments[1];
		if (!stats.Subfolders.TryGetValue(sub, out var children))
			stats.Subfolders[sub] = children = new SortedSet<string>(StringComparer.Ordinal);

		if (segments.Length >= 3)
			children.Add(segments[2]);
		return stats;
	}
}
