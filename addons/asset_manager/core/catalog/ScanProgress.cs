namespace AssetManager.Core;

/// `Stage` is "layout" while the top folders are listed (no unit total yet),
/// "scan" while packs are walked, "retry" for the second pass over folders
/// that failed the first time.
public readonly record struct ScanProgress(string Stage, int FilesSeen, string CurrentFolder, int UnitsDone = 0, int UnitsTotal = 0);
