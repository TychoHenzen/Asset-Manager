using System.Collections.Generic;

namespace AssetManager.Core;

/// What the sidebar draws, in the shape sidebar._gather_stats produced.
/// Folders cover the whole catalog; tag and extension counts are scoped to the
/// active folder so a count describes what clicking it gives you.
public sealed class CatalogStats
{
	public int Total { get; init; }

	/// type id -> (count, second-level folder -> third-level folder names)
	public required Dictionary<string, TypeFolderStats> Folders { get; init; }

	/// Tags used by more than one asset in the whole catalog, counted within
	/// the prefix.
	public required Dictionary<string, int> Tags { get; init; }
	public required Dictionary<string, int> Extensions { get; init; }
}
