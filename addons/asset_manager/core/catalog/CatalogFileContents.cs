using System.Collections.Generic;

namespace AssetManager.Core;

internal sealed record CatalogFileContents(long Generation, List<AssetRecord> Records, string[] TagNames);
