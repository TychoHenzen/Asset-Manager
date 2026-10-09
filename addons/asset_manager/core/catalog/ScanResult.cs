using System.Collections.Generic;

namespace AssetManager.Core;

/// One walk: the files found, and the workspace-relative folders holding a
/// vendor marker (including ones above a scoped scan's root).
public sealed record ScanResult(List<FoundFile> Files, HashSet<string> VendorRoots);
