using System.Collections.Generic;

namespace AssetManager.Core;

/// An existing record whose folder-derived tags no longer match what its path
/// and the vendor markers give, though the file itself is unchanged.
internal sealed record PlannedRetag(AssetRecord Record, List<string> AutoTags);
