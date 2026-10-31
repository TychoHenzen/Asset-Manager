using System.Collections.Generic;

namespace AssetManager.Core;

/// The top of a scan, listed before any pack is walked: the pack folders to
/// walk (`Units`), the shallower folders' own files (`Shallow`), every vendor
/// marker found on the way, and folders that stayed unreadable.
public sealed record ScanLayout(List<string> Units, List<UnitResult> Shallow, HashSet<string> VendorRoots, List<string> FailedDirs);
