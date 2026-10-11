using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetManager.Core;

/// What a walk means for the catalog, worked out before the catalog's lock
/// is taken. Planning is the slow phase: it reads .tres headers over the
/// network and derives every seen file's tags. Applying the plan is
/// dictionary work only.
internal sealed class ScanPlan
{
	public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
	public List<PlannedAddition> Additions { get; } = [];
	public List<PlannedChange> Changes { get; } = [];
	public List<PlannedRetag> Retags { get; } = [];

	/// `find` looks up the catalog's record for a path; the record set must
	/// not change while this runs: only one scan runs at a time, and only a
	/// scan or a load changes it.
	/// `tagNames` is a snapshot of the catalog's tag table, to compare an
	/// existing record's tags against.
	public static ScanPlan Build(
		ScanResult walk,
		Func<string, AssetRecord?> find,
		IReadOnlyList<string> tagNames,
		TypeTable types,
		Func<string, string> resourceTypeOf)
	{
		var plan = new ScanPlan();
		foreach (var file in walk.Files)
		{
			var record = find(file.RelPath);
			var unchanged = record != null && record.Size == file.Size && record.MTime == file.MTime;
			var typeIndex = TypeFor(file, unchanged, types, resourceTypeOf);
			if (typeIndex < 0)
				continue;

			plan.Seen.Add(file.RelPath);
			var typeTag = VendorMarker.IsUnderVendorRoot(file.RelPath, walk.VendorRoots) ? types[typeIndex].Id : null;
			var tags = AutoTags.Build(RelPaths.DirBelowBucket(file.RelPath), types, typeTag);
			plan.Place(file, typeIndex, record, unchanged, tags, tagNames);
		}
		return plan;
	}

	/// The index of the type that claims `file`, or -1 when none does: its
	/// bucket is not a type, its extension is not that type's, or (only read
	/// for new or changed files) its resource header says otherwise.
	private static int TypeFor(FoundFile file, bool unchanged, TypeTable types, Func<string, string> resourceTypeOf)
	{
		var typeIndex = types.IndexOf(RelPaths.Bucket(file.RelPath));
		if (typeIndex < 0)
			return -1;
		var type = types[typeIndex];
		if (!type.MatchesExtension(TextRules.ExtensionLower(RelPaths.FileName(file.RelPath))))
			return -1;
		if (!unchanged && type.NeedsResourceHeader && !type.MatchesResourceType(resourceTypeOf(file.RelPath)))
			return -1;
		return typeIndex;
	}

	private void Place(FoundFile file, int typeIndex, AssetRecord? record, bool unchanged, List<string> tags, IReadOnlyList<string> tagNames)
	{
		if (record == null)
		{
			Additions.Add(new PlannedAddition(file, typeIndex, tags));
			return;
		}
		if (!unchanged)
			Changes.Add(new PlannedChange(record, file));
		if (!record.AutoTags.Select(id => tagNames[id]).SequenceEqual(tags))
			Retags.Add(new PlannedRetag(record, tags));
	}
}
