using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;

namespace AssetManager.Core;

/// Every image file in one pack, listed once and kept for a few minutes, so
/// resolving a page of models from the same pack costs one directory walk
/// instead of one per model (each walk is network round trips on a NAS).
public sealed class PackImageIndex
{
	private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
	private static readonly ConcurrentDictionary<string, (DateTime Built, PackImageIndex Index)> Cache = new(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".bmp", ".webp", ".exr", ".dds",
	};

	private PackImageIndex(List<string> files) => Files = files;

	/// Absolute paths, "/"-separated.
	public IReadOnlyList<string> Files { get; }

	public static PackImageIndex For(string packRoot)
	{
		var now = DateTime.UtcNow;
		if (Cache.TryGetValue(packRoot, out var entry) && now - entry.Built < Lifetime)
			return entry.Index;
		var index = Build(packRoot);
		Cache[packRoot] = (now, index);
		return index;
	}

	public static void ClearCache() => Cache.Clear();

	private static PackImageIndex Build(string packRoot)
	{
		if (!Directory.Exists(packRoot))
			return new PackImageIndex([]);

		var walk = new FileSystemEnumerable<string>(
			packRoot,
			(ref FileSystemEntry entry) => entry.ToFullPath().Replace('\\', '/'),
			new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
		{
			ShouldIncludePredicate = (ref FileSystemEntry entry) =>
				!entry.IsDirectory && ImageExtensions.Contains(Path.GetExtension(entry.FileName).ToString()),
		};
		return new PackImageIndex(walk.ToList());
	}
}
