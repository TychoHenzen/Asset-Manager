using System;
using System.Collections.Generic;

namespace AssetManager.Core;

/// Folder names become tags. Port of DefaultImportRunner._build_auto_tags and
/// _is_noise: the folders a file sits in below its type bucket, lowercased,
/// spaces to underscores, minus words that describe nothing.
public static class AutoTags
{
	private static readonly HashSet<string> Blocklist = new(StringComparer.Ordinal)
	{
		"obj", "gltf", "glb", "fbx", "blend", "dae", "usd", "usda", "usdc",
		"usdz", "stl", "ply", "abc", "3ds", "max", "ma", "mb",
		"png", "jpg", "jpeg", "tga", "bmp", "webp", "exr", "hdr", "tiff", "tif",
		"wav", "ogg", "mp3", "flac", "aiff", "ogv", "mp4", "webm",
		"tres", "res", "tscn", "gdshader",
		"glb_format", "gltf_format", "fbx_format", "obj_format", "blend_format",
		"jpg_format", "png_format",
		"texture", "map", "asset", "sound", "audio", "game_kit", "tile",
		"src", "source", "file", "export",
		"exported", "output", "data", "content", "raw", "new", "old", "temp",
		"tmp", "misc", "other", "stuff", "backup", "archive", "archived",
		"final", "wip", "test", "sample", "preview", "thumbnail", "render",
		"extra", "common", "shared", "lib", "library", "pack", "bundle",
		"collection", "download", "unzipped", "converted", "shader_cache",
		"1k", "2k", "4k", "8k", "16k", "sd", "hd", "uhd",
		"lq", "mq", "hq", "low", "mid", "high", "lod",
		"red", "green", "blue", "yellow", "orange", "purple", "pink",
		"brown", "black", "white", "grey", "gray", "cyan", "magenta",
		"gold", "silver", "bronze", "beige", "tan", "teal", "violet",
		"maroon", "navy", "olive", "lime", "indigo", "turquoise",
		"colour", "color", "colours", "colors", "colored", "coloured",
	};

	/// `relativeDir` is the file's folder below its bucket, "/"-separated, ""
	/// for a file sitting directly in the bucket. `typeTag` (the bucket's type
	/// id, for files under a vendor marker) leads the list; it is a type id,
	/// which the noise rule would otherwise drop.
	public static List<string> Build(string relativeDir, TypeTable types, string? typeTag = null)
	{
		var tags = new List<string>();
		if (typeTag != null)
			tags.Add(typeTag);
		if (relativeDir.Length == 0)
			return tags;

		foreach (var segment in relativeDir.Split('/'))
		{
			var tag = TextRules.StripEdges(segment).ToLowerInvariant().Replace(" ", "_");
			if (tag.Length > 0 && !IsNoise(tag, types) && !tags.Contains(tag))
				tags.Add(tag);
		}
		return tags;
	}

	public static bool IsNoise(string tag, TypeTable types) =>
		tag.StartsWith('.')
		|| TextRules.IsValidInt(tag)
		|| Blocklist.Contains(tag)
		|| Blocklist.Contains(TextRules.TrimSuffix(tag, "s"))
		|| types.IsTypeId(tag);
}
