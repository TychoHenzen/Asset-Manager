using System;
using System.Collections.Generic;

namespace AssetManager.Core;

/// What a walk means for the catalog, worked out before the catalog's lock
/// is taken. Planning is the slow phase: it reads .tres headers over the
/// network. Applying the plan is dictionary work only.
internal sealed class ScanPlan
{
	public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
	public List<PlannedAddition> Additions { get; } = [];
	public List<PlannedChange> Changes { get; } = [];

	/// `existing` must not change while this runs: only one scan runs at a
	/// time, and only a scan or a load changes the catalog's record set.
	public static ScanPlan Build(
		IReadOnlyList<FoundFile> found,
		IReadOnlyDictionary<string, AssetRecord> existing,
		TypeTable types,
		Func<string, string> resourceTypeOf)
	{
		var plan = new ScanPlan();
		foreach (var file in found)
		{
			var typeIndex = types.IndexOf(RelPaths.Bucket(file.RelPath));
			if (typeIndex < 0)
				continue;
			var type = types[typeIndex];
			if (!type.MatchesExtension(TextRules.ExtensionLower(RelPaths.FileName(file.RelPath))))
				continue;

			existing.TryGetValue(file.RelPath, out var record);
			var unchanged = record != null && record.Size == file.Size && record.MTime == file.MTime;
			if (!unchanged && type.NeedsResourceHeader && !type.MatchesResourceType(resourceTypeOf(file.RelPath)))
				continue;

			plan.Seen.Add(file.RelPath);
			if (unchanged)
				continue;
			if (record != null)
				plan.Changes.Add(new PlannedChange(record, file));
			else
				plan.Additions.Add(new PlannedAddition(file, typeIndex, AutoTags.Build(RelPaths.DirBelowBucket(file.RelPath), types)));
		}
		return plan;
	}
}
