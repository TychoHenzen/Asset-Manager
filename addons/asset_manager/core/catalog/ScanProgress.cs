namespace AssetManager.Core;

/// The total is unknown while walking, so progress is a running count.
public readonly record struct ScanProgress(int FilesSeen, string CurrentFolder);
