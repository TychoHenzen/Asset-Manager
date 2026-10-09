using System.Collections.Generic;

namespace AssetManager.Core;

/// A consistent copy of the catalog, taken under its lock and then written to
/// disk without holding it. Tag ids in each record index into TagNames.
public sealed record CatalogSnapshot(IReadOnlyList<AssetRecord> Records, IReadOnlyList<string> TagNames);
