using System;
using System.Collections.Generic;

namespace AssetManager.Core;

/// Tag strings interned to small ints, so 280k records share one copy of each
/// tag and filtering compares ints. Not thread-safe on its own; Catalog guards
/// it with its lock.
internal sealed class TagTable
{
	private readonly List<string> _names = [];
	private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

	public IReadOnlyList<string> Names => _names;

	public string this[int id] => _names[id];

	public void Clear()
	{
		_names.Clear();
		_ids.Clear();
	}

	public int Intern(string tag)
	{
		if (_ids.TryGetValue(tag, out var id))
			return id;
		id = _names.Count;
		_names.Add(tag);
		_ids[tag] = id;
		return id;
	}

	public bool TryGetId(string tag, out int id) => _ids.TryGetValue(tag, out id);
}
