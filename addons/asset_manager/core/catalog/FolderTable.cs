using System;
using System.Collections.Generic;

namespace AssetManager.Core;

/// The library's folders by path, one AssetFolder each. Not thread-safe on
/// its own; Catalog guards its table with its lock, a load uses its own.
internal sealed class FolderTable
{
	private readonly Dictionary<string, AssetFolder> _byPath = new(StringComparer.Ordinal);

	public int Count => _byPath.Count;

	public IEnumerable<AssetFolder> All => _byPath.Values;

	public AssetFolder For(string path)
	{
		if (!_byPath.TryGetValue(path, out var folder))
			_byPath[path] = folder = new AssetFolder(path);
		return folder;
	}

	public bool TryGet(string path, out AssetFolder folder) => _byPath.TryGetValue(path, out folder!);

	public void Remove(AssetFolder folder) => _byPath.Remove(folder.Path);

	public void Clear() => _byPath.Clear();
}
