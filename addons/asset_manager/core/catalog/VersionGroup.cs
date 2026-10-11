using System.Collections.Generic;

namespace AssetManager.Core;

/// Files that are one asset: formats of it and exact copies of those.
/// `Members` is in preference order: the type's extension order, then path.
public sealed class VersionGroup(IReadOnlyList<AssetRecord> members)
{
	public IReadOnlyList<AssetRecord> Members { get; } = members;
}
