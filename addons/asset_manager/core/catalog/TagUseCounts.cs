using System.Collections.Generic;

namespace AssetManager.Core;

/// How many records carry each tag id, kept up to date as records change, so
/// the list of tags in use never needs a walk over every record (541 ms at
/// 1.77M records, and the tag editor asks on the main thread). Not
/// thread-safe on its own; Catalog guards it with its lock.
internal sealed class TagUseCounts
{
	private readonly List<int> _counts = [];

	public void Clear() => _counts.Clear();

	public void Add(int[] tags)
	{
		foreach (var id in tags)
		{
			while (_counts.Count <= id)
				_counts.Add(0);
			_counts[id]++;
		}
	}

	public void Remove(int[] tags)
	{
		foreach (var id in tags)
			_counts[id]--;
	}

	public IEnumerable<int> Used()
	{
		for (var id = 0; id < _counts.Count; id++)
		{
			if (_counts[id] > 0)
				yield return id;
		}
	}
}
