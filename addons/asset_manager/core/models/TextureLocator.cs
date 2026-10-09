using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssetManager.Core;

/// Finds the files an FBX's texture references point at, the way a person
/// would: where the reference says, next to the model, in a Textures folder
/// nearby, anywhere in the pack by name, and finally a same-named texture in
/// another format or a numbered variant of it (Synty props name a .psd that
/// never ships; Dungeons_Texture.psd -> Dungeons_Texture_01.png).
/// Never looks outside the pack, except for a relative reference that
/// explicitly climbs out of it.
public static class TextureLocator
{
	/// `fbxPath` and `packRoot` are absolute, "/"-separated.
	public static List<ResolvedTexture> Resolve(string fbxPath, IReadOnlyList<string> references, string packRoot)
	{
		var fbxDir = Path.GetDirectoryName(fbxPath)!.Replace('\\', '/');
		var index = PackImageIndex.For(packRoot);
		var resolved = new List<ResolvedTexture>();
		foreach (var reference in references)
		{
			var found = Locate(reference, fbxDir, index);
			if (found != null)
				resolved.Add(found);
		}
		return resolved;
	}

	/// The pack an asset belongs to: the bucket, then vendor, category and pack
	/// (models/synty/environment/POLYGON_Dungeon_Pack_v4), or the asset's own
	/// folder when it sits shallower than that.
	public static string PackRootFor(string assetPath, string bucket)
	{
		var segments = assetPath.Replace('\\', '/').Split('/');
		var bucketIndex = Array.LastIndexOf(segments, bucket, segments.Length - 2);
		if (bucketIndex < 0)
			return string.Join('/', segments[..^1]);
		// bucket + vendor, category, pack; never the file itself.
		var last = Math.Min(bucketIndex + 3, segments.Length - 2);
		return string.Join('/', segments[..(last + 1)]);
	}

	private static ResolvedTexture? Locate(string reference, string fbxDir, PackImageIndex index)
	{
		var name = reference[(reference.LastIndexOf('/') + 1)..];

		if (!IsAbsolute(reference))
		{
			var relative = Path.GetFullPath(Path.Combine(fbxDir, reference)).Replace('\\', '/');
			if (File.Exists(relative))
				return new ResolvedTexture(reference, relative, true);
		}

		var beside = fbxDir + "/" + name;
		if (File.Exists(beside))
			return new ResolvedTexture(reference, beside, true);

		var byName = Nearest(index.Files.Where(f => FileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)), fbxDir);
		if (byName != null)
			return new ResolvedTexture(reference, byName, true);

		var stem = Path.GetFileNameWithoutExtension(name);
		var sameStem = Nearest(index.Files.Where(f => Stem(f).Equals(stem, StringComparison.OrdinalIgnoreCase)), fbxDir);
		if (sameStem != null)
			return new ResolvedTexture(reference, sameStem, false);

		// The plain variant over lettered ones: shortest name, then by name.
		var variant = index.Files
			.Where(f => Stem(f).StartsWith(stem, StringComparison.OrdinalIgnoreCase))
			.OrderBy(f => Stem(f).Length)
			.ThenBy(f => FileName(f), StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
		return variant != null ? new ResolvedTexture(reference, variant, false) : null;
	}

	/// Of several files with the same name (every Kenney pack has its own
	/// colormap.png), the one sharing the longest folder path with the model.
	private static string? Nearest(IEnumerable<string> candidates, string fbxDir) =>
		candidates
			.OrderByDescending(c => SharedPrefixLength(Path.GetDirectoryName(c)!.Replace('\\', '/'), fbxDir))
			.ThenBy(c => c.Length)
			.FirstOrDefault();

	private static int SharedPrefixLength(string a, string b)
	{
		var n = Math.Min(a.Length, b.Length);
		var i = 0;
		while (i < n && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i]))
			i++;
		return i;
	}

	private static bool IsAbsolute(string reference) =>
		reference.StartsWith('/') || (reference.Length > 1 && reference[1] == ':');

	private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

	private static string Stem(string path) => Path.GetFileNameWithoutExtension(FileName(path));
}
