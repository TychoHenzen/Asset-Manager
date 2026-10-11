using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetManager.Core;

public sealed class TypeTable
{
	private readonly List<AssetType> _types;
	private readonly Dictionary<string, int> _indexById;

	public TypeTable(IEnumerable<AssetType> types)
	{
		_types = types.ToList();
		_indexById = new Dictionary<string, int>(StringComparer.Ordinal);
		for (var i = 0; i < _types.Count; i++)
			_indexById[_types[i].Id] = i;
	}

	public IReadOnlyList<AssetType> All => _types;

	public AssetType this[int index] => _types[index];

	public int IndexOf(string id) => _indexById.TryGetValue(id, out var i) ? i : -1;

	public bool IsTypeId(string id) => _indexById.ContainsKey(id);
}
