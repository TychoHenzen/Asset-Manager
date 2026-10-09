using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AssetManager.Core;

/// catalog.bin: everything a scan produces. Rewritten after a scan that
/// changed something, never on a tag edit.
/// Layout after the header: type ids, tag names, then per record: path, type
/// index, size, mtime, date added, auto tag ids, subtypes. Type ids are stored
/// by name so a reordered asset_types.gd still loads.
internal static class CatalogFile
{
	public const string Magic = "AMCT";
	public const int Version = 1;

	public static byte[] Serialize(CatalogSnapshot snapshot, TypeTable types, long generation)
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter(stream, Encoding.UTF8);
		StoreHeader.Write(w, Magic, Version, generation);

		w.Write(types.All.Count);
		foreach (var type in types.All)
			w.Write(type.Id);

		w.Write(snapshot.TagNames.Count);
		foreach (var tag in snapshot.TagNames)
			w.Write(tag);

		w.Write(snapshot.Records.Count);
		foreach (var r in snapshot.Records)
		{
			w.Write(r.RelPath);
			w.Write(r.Type);
			w.Write(r.Size);
			w.Write(r.MTime);
			w.Write(r.DateAdded);
			w.Write(r.AutoTags.Length);
			foreach (var id in r.AutoTags)
				w.Write(id);
			w.Write(r.Subtypes.Length);
			foreach (var subtype in r.Subtypes)
				w.Write(subtype);
		}

		w.Flush();
		return stream.ToArray();
	}

	/// Records of a type this version no longer declares are dropped.
	public static CatalogFileContents Deserialize(byte[] bytes, TypeTable types)
	{
		using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
		var generation = StoreHeader.Read(r, Magic, Version);

		var storedTypes = new int[r.ReadInt32()];
		for (var i = 0; i < storedTypes.Length; i++)
			storedTypes[i] = types.IndexOf(r.ReadString());

		var tagNames = new string[r.ReadInt32()];
		for (var i = 0; i < tagNames.Length; i++)
			tagNames[i] = r.ReadString();

		var count = r.ReadInt32();
		var records = new List<AssetRecord>(count);
		for (var i = 0; i < count; i++)
		{
			var relPath = r.ReadString();
			var type = storedTypes[r.ReadInt32()];
			var record = new AssetRecord(relPath, Math.Max(type, 0))
			{
				Size = r.ReadInt64(),
				MTime = r.ReadInt64(),
				DateAdded = r.ReadInt64(),
				AutoTags = ReadInts(r),
				Subtypes = ReadStrings(r),
			};
			if (type >= 0)
				records.Add(record);
		}

		return new CatalogFileContents(generation, records, tagNames);
	}

	private static int[] ReadInts(BinaryReader r)
	{
		var values = new int[r.ReadInt32()];
		for (var i = 0; i < values.Length; i++)
			values[i] = r.ReadInt32();
		return values;
	}

	private static string[] ReadStrings(BinaryReader r)
	{
		var values = new string[r.ReadInt32()];
		for (var i = 0; i < values.Length; i++)
			values[i] = r.ReadString();
		return values;
	}
}
