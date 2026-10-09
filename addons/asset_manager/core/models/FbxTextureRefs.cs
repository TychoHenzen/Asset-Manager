using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetManager.Core;

/// The image files an FBX names. Binary FBX stores each string as a length
/// followed by plain bytes, and ASCII FBX quotes them, so in both forms a
/// texture reference is a run of printable characters ending in an image
/// extension. Reading them this way needs no FBX parser and works whether the
/// importer could find the file or not.
public static partial class FbxTextureRefs
{
	private const int MaxPathLength = 260;

	[GeneratedRegex(@"[\x20-\x7e]+?\.(?:png|jpe?g|tga|tiff?|psd|bmp|exr|webp|dds)(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
	private static partial Regex Reference();

	/// Distinct references in file order, "\" turned into "/". The same
	/// texture usually appears twice (absolute FileName, RelativeFilename).
	public static List<string> Find(byte[] fbx)
	{
		var text = Encoding.Latin1.GetString(fbx);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var refs = new List<string>();
		foreach (Match match in Reference().Matches(text))
		{
			var reference = Clean(match.Value);
			if (reference.Length is > 0 and <= MaxPathLength && seen.Add(reference))
				refs.Add(reference);
		}
		return refs;
	}

	/// ASCII FBX puts the property name and an opening quote in the same
	/// printable run as the path; only what follows the last quote is the path.
	private static string Clean(string run)
	{
		var quote = run.LastIndexOf('"');
		var path = quote >= 0 ? run[(quote + 1)..] : run;
		return path.Trim().Replace('\\', '/');
	}
}
