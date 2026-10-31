using System.Collections.Generic;

namespace AssetManager.Core;

/// One unit of a walk: a pack folder and everything below it, or a shallower
/// folder's own files when not `Recursive`. Records under `FailedDirs`, which
/// stayed unreadable through every retry, are kept rather than taken for deleted.
public sealed record UnitResult(string Root, bool Recursive, List<FoundFile> Files, List<string> FailedDirs);
