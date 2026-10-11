using System.Collections.Generic;

namespace AssetManager.Core;

/// One page of matches plus the size of the whole match set, so the grid can
/// draw its pager without holding every match. `VersionCounts[i]` is how many
/// versions `Items[i]`'s asset has, 1 for none.
public sealed record QueryPage(IReadOnlyList<AssetRecord> Items, int Total, int Page, int TotalPages, IReadOnlyList<int> VersionCounts);
