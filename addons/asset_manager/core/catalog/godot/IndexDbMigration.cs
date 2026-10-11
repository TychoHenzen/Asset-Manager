using System.Collections.Generic;
using System.IO;
using AssetManager.Core;
using Godot;

namespace AssetManager.Bridge;

/// One-time import of the GDScript addon's index.db, so a workspace indexed
/// before the fork keeps its tags, subtypes and dates. Sizes are unknown
/// (-1), which makes the first scan treat every entry as changed: it fills in
/// size and mtime and keeps the tags.
/// Every Variant and collection wrapper is disposed here rather than left for
/// the engine's shutdown, which would crash on them.
internal static class IndexDbMigration
{
	public const string FileName = "index.db";

	public static bool TryMigrate(string workspace, Catalog catalog)
	{
		var path = Path.Combine(workspace, CatalogStore.DirName, FileName);
		if (!File.Exists(path))
			return false;

		// FileAccess.store_var writes a 4-byte length before the variant.
		var bytes = File.ReadAllBytes(path);
		if (bytes.Length <= 4)
			return false;

		using var data = GD.BytesToVar(bytes[4..]);
		if (data.VariantType != Variant.Type.Dictionary)
			return false;
		using var root = data.AsGodotDictionary();
		if (!root.TryGetValue("assets", out var assetsVariant))
			return false;
		using var assetsHolder = assetsVariant;
		using var assets = assetsVariant.AsGodotDictionary();

		var records = new List<AssetRecord>();
		var tags = new TagCollector();
		foreach (var (key, value) in assets)
		{
			using var keyHolder = key;
			using var valueHolder = value;
			using var entry = value.AsGodotDictionary();
			var record = ToRecord(workspace, key.AsString(), entry, catalog.Types, tags);
			if (record != null)
				records.Add(record);
		}

		catalog.Load(records, tags.Names);
		return true;
	}

	private static AssetRecord? ToRecord(string workspace, string absolutePath, Godot.Collections.Dictionary entry, TypeTable types, TagCollector tags)
	{
		var rel = WorkspacePaths.Relative(workspace.Replace('\\', '/'), absolutePath);
		var type = types.IndexOf(ReadString(entry, "type"));
		if (rel.Length == 0 || type < 0)
			return null;

		var record = Catalog.NewRecord(rel, type);
		record.Size = -1;
		record.MTime = -1;
		record.DateAdded = ReadLong(entry, "date_added");
		record.AutoTags = tags.Intern(ReadStrings(entry, "tags"));
		record.Subtypes = ReadStrings(entry, "subtype");
		return record;
	}

	private static string ReadString(Godot.Collections.Dictionary entry, string key)
	{
		if (!entry.TryGetValue(key, out var value))
			return "";
		using (value)
			return value.AsString();
	}

	private static long ReadLong(Godot.Collections.Dictionary entry, string key)
	{
		if (!entry.TryGetValue(key, out var value))
			return 0;
		using (value)
			return value.AsInt64();
	}

	private static string[] ReadStrings(Godot.Collections.Dictionary entry, string key)
	{
		if (!entry.TryGetValue(key, out var value))
			return [];
		using (value)
		{
			if (value.VariantType == Variant.Type.PackedStringArray)
				return value.AsStringArray();
			if (value.VariantType != Variant.Type.Array)
				return [];
			using var array = value.AsGodotArray();
			var result = new string[array.Count];
			for (var i = 0; i < result.Length; i++)
			{
				using var item = array[i];
				result[i] = item.AsString();
			}
			return result;
		}
	}

	private sealed class TagCollector
	{
		private readonly Dictionary<string, int> _ids = new();

		public List<string> Names { get; } = [];

		public int[] Intern(string[] tags)
		{
			var ids = new int[tags.Length];
			for (var i = 0; i < tags.Length; i++)
			{
				if (!_ids.TryGetValue(tags[i], out ids[i]))
				{
					ids[i] = Names.Count;
					Names.Add(tags[i]);
					_ids[tags[i]] = ids[i];
				}
			}
			return ids;
		}
	}
}
