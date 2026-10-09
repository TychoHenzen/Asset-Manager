namespace AssetManager.Core;

public readonly record struct ScanSummary(int Added, int Changed, int Removed, int Total)
{
	public bool AnythingChanged => Added + Changed + Removed > 0;
}
