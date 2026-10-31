namespace AssetManager.Core;

/// `Retagged` counts files that did not change but whose folder tags did (a
/// vendor marker was added or removed above them).
public readonly record struct ScanSummary(int Added, int Changed, int Removed, int Total, int Retagged = 0)
{
	public bool AnythingChanged => Added + Changed + Removed + Retagged > 0;
}
