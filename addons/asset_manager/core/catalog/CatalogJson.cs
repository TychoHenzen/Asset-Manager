using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AssetManager.Core;

/// The two nested structures that cross into GDScript go as JSON text. Godot
/// collection wrappers held on the C# side are disposed by the engine at
/// shutdown, after the native side is gone, which crashes the editor on exit;
/// a string carries no such lifetime.
public static class CatalogJson
{
	/// `json` is JSON.stringify(AssetTypes.ALL).
	public static TypeTable ParseTypes(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var types = new List<AssetType>();
		foreach (var entry in doc.RootElement.EnumerateArray())
		{
			var extensions = Strings(entry, "extensions").Select(e => e.ToLowerInvariant()).ToList();
			types.Add(new AssetType
			{
				Id = entry.GetProperty("id").GetString() ?? "",
				Extensions = extensions.ToHashSet(),
				ExtensionOrder = extensions,
				ResourceTypes = Strings(entry, "resource_type").ToHashSet(),
			});
		}
		return new TypeTable(types);
	}

	/// The grid's filter dictionary, JSON.stringify'd. The prefix arrives
	/// absolute and is made workspace-relative here.
	public static CatalogQuery ParseQuery(string json, string workspace)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		return new CatalogQuery
		{
			Prefix = WorkspacePaths.Relative(workspace, String(root, "prefix")),
			Tags = Strings(root, "tags").ToArray(),
			Extensions = Strings(root, "extensions").ToArray(),
			Subtype = String(root, "subtype"),
			Search = String(root, "search"),
			Sort = (SortMode)Int(root, "sort", 0),
			Page = Math.Max(0, Int(root, "page", 0)),
			PerPage = Math.Max(1, Int(root, "per_page", 100)),
			GroupVersions = !root.TryGetProperty("group_versions", out var group) || group.ValueKind != JsonValueKind.False,
		};
	}

	/// The shape sidebar._gather_stats returned:
	/// {folders: {type: {count, subfolders: {name: {child: true}}}}, tags, extensions, total}.
	public static string Stats(CatalogStats stats)
	{
		using var stream = new MemoryStream();
		using (var w = new Utf8JsonWriter(stream))
		{
			w.WriteStartObject();
			w.WriteStartObject("folders");
			foreach (var (typeId, typeStats) in stats.Folders)
			{
				w.WriteStartObject(typeId);
				w.WriteNumber("count", typeStats.Count);
				w.WriteStartObject("subfolders");
				foreach (var (name, children) in typeStats.Subfolders)
				{
					w.WriteStartObject(name);
					foreach (var child in children)
						w.WriteBoolean(child, true);
					w.WriteEndObject();
				}
				w.WriteEndObject();
				w.WriteEndObject();
			}
			w.WriteEndObject();
			WriteCounts(w, "tags", stats.Tags);
			WriteCounts(w, "extensions", stats.Extensions);
			w.WriteNumber("total", stats.Total);
			w.WriteEndObject();
		}
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static void WriteCounts(Utf8JsonWriter w, string name, Dictionary<string, int> counts)
	{
		w.WriteStartObject(name);
		foreach (var (key, count) in counts)
			w.WriteNumber(key, count);
		w.WriteEndObject();
	}

	private static string String(JsonElement root, string property) =>
		root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

	/// JSON.stringify writes every number as a float ("2.0"), so read as double.
	private static int Int(JsonElement root, string property, int fallback) =>
		root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? (int)value.GetDouble() : fallback;

	private static IEnumerable<string> Strings(JsonElement entry, string property) =>
		entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
			? value.EnumerateArray().Select(v => v.GetString() ?? "")
			: Array.Empty<string>();
}
