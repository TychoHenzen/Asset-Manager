using System.Collections.Generic;

namespace AssetManager.Core;

/// One asset type as declared in asset_types.gd. The table is handed in from
/// GDScript so the type list stays single-source.
public sealed class AssetType
{
	public required string Id { get; init; }

	/// Lowercase, without the dot. "" stands for files with no extension.
	public required IReadOnlySet<string> Extensions { get; init; }

	/// The extensions in asset_types.gd's order, which puts the formats Godot
	/// handles best first. The first version of an asset in this order is the
	/// one offered by default.
	public IReadOnlyList<string> ExtensionOrder { get; init; } = [];

	/// Empty means any resource type is accepted.
	public required IReadOnlySet<string> ResourceTypes { get; init; }

	public bool NeedsResourceHeader => ResourceTypes.Count > 0;

	/// Mirrors DefaultImportRunner._matches_type, minus the header read, which
	/// the caller does only for files that are new or changed.
	public bool MatchesExtension(string extensionLower) =>
		Extensions.Count == 0 || Extensions.Contains(extensionLower);

	public bool MatchesResourceType(string resourceType) =>
		!NeedsResourceHeader || ResourceTypes.Contains(resourceType);

	/// Lower is preferred; extensions not listed come last.
	public int ExtensionRank(string extensionLower)
	{
		for (var i = 0; i < ExtensionOrder.Count; i++)
		{
			if (ExtensionOrder[i] == extensionLower)
				return i;
		}
		return ExtensionOrder.Count;
	}
}
