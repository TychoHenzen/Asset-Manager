using System;
using System.Collections.Generic;

namespace AssetManager.Core;

public sealed class TypeFolderStats
{
	public int Count { get; set; }

	public Dictionary<string, SortedSet<string>> Subfolders { get; } = new(StringComparer.Ordinal);
}
