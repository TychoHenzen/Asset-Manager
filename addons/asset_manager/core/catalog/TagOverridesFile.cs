using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AssetManager.Core;

/// tags.bin: only the tags a user edited, by path. Small, so a tag edit
/// rewrites kilobytes instead of the whole catalog.
internal static class TagOverridesFile
{
	public const string Magic = "AMTG";
	public const int Version = 1;

	public static byte[] Serialize(IReadOnlyDictionary<string, string[]> overrides, long generation)
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter(stream, Encoding.UTF8);
		StoreHeader.Write(w, Magic, Version, generation);

		w.Write(overrides.Count);
		foreach (var (path, tags) in overrides)
		{
			w.Write(path);
			w.Write(tags.Length);
			foreach (var tag in tags)
				w.Write(tag);
		}

		w.Flush();
		return stream.ToArray();
	}

	public static TagOverridesContents Deserialize(byte[] bytes)
	{
		using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
		var generation = StoreHeader.Read(r, Magic, Version);

		var count = r.ReadInt32();
		var overrides = new Dictionary<string, string[]>(count, StringComparer.Ordinal);
		for (var i = 0; i < count; i++)
		{
			var path = r.ReadString();
			var tags = new string[r.ReadInt32()];
			for (var t = 0; t < tags.Length; t++)
				tags[t] = r.ReadString();
			overrides[path] = tags;
		}

		return new TagOverridesContents(generation, overrides);
	}
}
