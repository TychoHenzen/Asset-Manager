using System.Collections.Generic;

namespace AssetManager.Core;

internal sealed record PlannedAddition(FoundFile File, int TypeIndex, List<string> AutoTags);
