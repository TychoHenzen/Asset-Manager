using System.Collections.Generic;

namespace AssetManager.Core;

internal sealed record TagOverridesContents(long Generation, Dictionary<string, string[]> Overrides);
