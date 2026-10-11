using System.IO;
using System.Linq;
using AssetManager.Core;
using Godot;

namespace AssetManager.Bridge;

/// GDScript's handle on TextureLocator: which files an FBX's texture
/// references point at. Reads the model and lists its pack, so it belongs on a
/// worker thread (the thumbnail prepare step) or a one-off user action.
[Tool]
[GlobalClass]
public partial class ModelTextures : RefCounted
{
	/// Flat triples, one per reference that was found: reference, absolute
	/// path, "1" when the file has the referenced name (Godot's importer can
	/// find it by itself) or "0" for a fallback match.
	public string[] Resolve(string fbxPath)
	{
		if (!File.Exists(fbxPath))
			return [];
		var refs = FbxTextureRefs.Find(File.ReadAllBytes(fbxPath));
		if (refs.Count == 0)
			return [];
		var packRoot = TextureLocator.PackRootFor(fbxPath, "models");
		return TextureLocator.Resolve(fbxPath.Replace('\\', '/'), refs, packRoot)
			.SelectMany(t => new[] { t.Reference, t.Path, t.Exact ? "1" : "0" })
			.ToArray();
	}
}
