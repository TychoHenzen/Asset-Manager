using System.Collections.Generic;

namespace AssetManager.Core;

/// Counts every match but keeps only the requested page, so a query over
/// 280k records allocates one page of results.
internal sealed class PageCollector
{
	private readonly int _start;
	private readonly int _perPage;

	public PageCollector(int page, int perPage)
	{
		_perPage = perPage;
		_start = page * perPage;
		Items = new List<AssetRecord>(perPage);
	}

	public List<AssetRecord> Items { get; }
	public int Total { get; private set; }

	/// True when the requested page lies past the last match.
	public bool OverShot => Items.Count == 0 && Total > 0 && _start >= Total;

	public int LastPage => Total == 0 ? 0 : (Total - 1) / _perPage;

	public void Take(AssetRecord record)
	{
		if (Total >= _start && Items.Count < _perPage)
			Items.Add(record);
		Total++;
	}
}
