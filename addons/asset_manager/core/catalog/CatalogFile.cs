using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AssetManager.Core;

/// catalog.bin: everything a scan produces. Rewritten after a scan that
/// changed something, never on a tag edit. Read and written as a stream: the
/// file is hundreds of MB at library size, too big to hold twice.
/// Layout after the header: type ids, tag names, then
/// - version 3: the folders, the distinct auto-tag arrays, then per record:
///   folder index, file name, type index, size, mtime, date added, tag array
///   index, subtypes, content hash (a length, 0 for none, then the bytes).
///   Numbers are 7-bit encoded. 1.77M files share 84k folders and 82k tag
///   arrays, so this is about a third of version 2.
/// - versions 1 and 2: per record the full path, type index, size, mtime,
///   date added, auto tag ids, subtypes, and (version 2) the content hash.
/// Type ids are stored by name so a reordered asset_types.gd still loads.
internal static class CatalogFile
{
	public const string Magic = "AMCT";
	public const int Version = 3;
	private const int OldestReadable = 1;

	public static void Serialize(Stream stream, CatalogSnapshot snapshot, TypeTable types, long generation)
	{
		using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
		StoreHeader.Write(w, Magic, Version, generation);

		w.Write(types.All.Count);
		foreach (var type in types.All)
			w.Write(type.Id);

		w.Write(snapshot.TagNames.Count);
		foreach (var tag in snapshot.TagNames)
			w.Write(tag);

		var folders = new Dictionary<AssetFolder, int>(ReferenceEqualityComparer.Instance);
		var folderList = new List<AssetFolder>();
		var tagArrays = new Dictionary<int[], int>(TagArrays.Contents.Instance);
		var tagArrayList = new List<int[]>();
		foreach (var r in snapshot.Records)
		{
			if (folders.TryAdd(r.Folder, folderList.Count))
				folderList.Add(r.Folder);
			if (tagArrays.TryAdd(r.AutoTags, tagArrayList.Count))
				tagArrayList.Add(r.AutoTags);
		}

		w.Write7BitEncodedInt(folderList.Count);
		foreach (var folder in folderList)
			w.Write(folder.Path);

		w.Write7BitEncodedInt(tagArrayList.Count);
		foreach (var ids in tagArrayList)
		{
			w.Write7BitEncodedInt(ids.Length);
			foreach (var id in ids)
				w.Write7BitEncodedInt(id);
		}

		w.Write7BitEncodedInt(snapshot.Records.Count);
		foreach (var r in snapshot.Records)
		{
			w.Write7BitEncodedInt(folders[r.Folder]);
			w.Write(r.FileName);
			w.Write7BitEncodedInt(r.Type);
			w.Write7BitEncodedInt64(r.Size);
			w.Write7BitEncodedInt64(r.MTime);
			w.Write7BitEncodedInt64(r.DateAdded);
			w.Write7BitEncodedInt(tagArrays[r.AutoTags]);
			w.Write7BitEncodedInt(r.Subtypes.Length);
			foreach (var subtype in r.Subtypes)
				w.Write(subtype);
			var hash = r.Hash ?? [];
			w.Write7BitEncodedInt(hash.Length);
			w.Write(hash);
		}
	}

	public static CatalogFileContents Deserialize(byte[] bytes, TypeTable types) =>
		Deserialize(new MemoryStream(bytes), types);

	/// Records of a type this version no longer declares are dropped.
	public static CatalogFileContents Deserialize(Stream stream, TypeTable types)
	{
		using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
		var generation = StoreHeader.Read(r, Magic, OldestReadable, Version, out var version);

		var storedTypes = new int[r.ReadInt32()];
		for (var i = 0; i < storedTypes.Length; i++)
			storedTypes[i] = types.IndexOf(r.ReadString());

		var tagNames = new string[r.ReadInt32()];
		for (var i = 0; i < tagNames.Length; i++)
			tagNames[i] = r.ReadString();

		var records = version >= 3 ? ReadRecords(r, storedTypes) : ReadRecordsByPath(r, storedTypes, version);
		return new CatalogFileContents(generation, records, tagNames);
	}

	private static List<AssetRecord> ReadRecords(BinaryReader r, int[] storedTypes)
	{
		var folders = new AssetFolder[r.Read7BitEncodedInt()];
		for (var i = 0; i < folders.Length; i++)
			folders[i] = new AssetFolder(r.ReadString());

		var tagArrays = new int[r.Read7BitEncodedInt()][];
		for (var i = 0; i < tagArrays.Length; i++)
		{
			var ids = new int[r.Read7BitEncodedInt()];
			for (var j = 0; j < ids.Length; j++)
				ids[j] = r.Read7BitEncodedInt();
			tagArrays[i] = ids;
		}

		var count = r.Read7BitEncodedInt();
		var records = new List<AssetRecord>(count);
		for (var i = 0; i < count; i++)
		{
			var folder = folders[r.Read7BitEncodedInt()];
			var fileName = r.ReadString();
			var type = storedTypes[r.Read7BitEncodedInt()];
			var record = new AssetRecord(folder, fileName, Math.Max(type, 0))
			{
				Size = r.Read7BitEncodedInt64(),
				MTime = r.Read7BitEncodedInt64(),
				DateAdded = r.Read7BitEncodedInt64(),
				AutoTags = tagArrays[r.Read7BitEncodedInt()],
				Subtypes = ReadStrings(r, r.Read7BitEncodedInt()),
				Hash = ReadHash(r, r.Read7BitEncodedInt()),
			};
			if (type >= 0)
				records.Add(record);
		}
		return records;
	}

	private static List<AssetRecord> ReadRecordsByPath(BinaryReader r, int[] storedTypes, int version)
	{
		var folders = new FolderTable();
		var tagArrays = new TagArrays();
		var count = r.ReadInt32();
		var records = new List<AssetRecord>(count);
		for (var i = 0; i < count; i++)
		{
			var relPath = r.ReadString();
			var type = storedTypes[r.ReadInt32()];
			var record = new AssetRecord(folders.For(RelPaths.Parent(relPath)), RelPaths.FileName(relPath), Math.Max(type, 0))
			{
				Size = r.ReadInt64(),
				MTime = r.ReadInt64(),
				DateAdded = r.ReadInt64(),
				AutoTags = tagArrays.Intern(ReadInts(r)),
				Subtypes = ReadStrings(r, r.ReadInt32()),
				Hash = version >= 2 ? ReadHash(r, r.ReadInt32()) : null,
			};
			if (type >= 0)
				records.Add(record);
		}
		return records;
	}

	private static byte[]? ReadHash(BinaryReader r, int length) => length == 0 ? null : r.ReadBytes(length);

	private static int[] ReadInts(BinaryReader r)
	{
		var values = new int[r.ReadInt32()];
		for (var i = 0; i < values.Length; i++)
			values[i] = r.ReadInt32();
		return values;
	}

	private static string[] ReadStrings(BinaryReader r, int count)
	{
		if (count == 0)
			return [];
		var values = new string[count];
		for (var i = 0; i < values.Length; i++)
			values[i] = r.ReadString();
		return values;
	}
}
