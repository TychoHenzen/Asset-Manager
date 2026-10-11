using System.Text;
using AssetManager.Core;

namespace AssetManager.Tests;

public class TextureLocatorTests : IDisposable
{
	private readonly TempWorkspace _ws = new();

	public TextureLocatorTests() => PackImageIndex.ClearCache();

	public void Dispose() => _ws.Dispose();

	/// A binary FBX string property: type code, little-endian length, bytes.
	private static byte[] BinaryString(string value)
	{
		var bytes = Encoding.ASCII.GetBytes(value);
		return [(byte)'S', (byte)bytes.Length, 0, 0, 0, .. bytes];
	}

	[Fact]
	public void Find_ReadsBinaryAndAsciiReferences_Deduplicated()
	{
		byte[] binary = [0, 1, 2, .. BinaryString("U:\\Dropbox\\Synty\\Textures\\Dungeons_Texture_01.png"),
			9, .. BinaryString("..\\..\\Textures\\Dungeons_Texture_01.png"), 0, .. BinaryString("U:/Dropbox/Synty/Textures/Dungeons_Texture_01.png")];
		Assert.Equal(["U:/Dropbox/Synty/Textures/Dungeons_Texture_01.png", "../../Textures/Dungeons_Texture_01.png"], FbxTextureRefs.Find(binary));

		var ascii = Encoding.ASCII.GetBytes("Video: \"Video::colormap\" {\n RelativeFilename: \"Textures\\colormap.png\"\n}");
		Assert.Equal(["Textures/colormap.png"], FbxTextureRefs.Find(ascii));
	}

	[Fact]
	public void PackRootFor_IsBucketThenVendorCategoryPack()
	{
		Assert.Equal("Z:/lib/models/synty/environment/Dungeon",
			TextureLocator.PackRootFor("Z:/lib/models/synty/environment/Dungeon/FBX/Characters/ghost.fbx", "models"));
		Assert.Equal("Z:/lib/models/kenney",
			TextureLocator.PackRootFor("Z:/lib/models/kenney/barrel.fbx", "models"));
	}

	[Fact]
	public void Resolve_RelativeReferenceNextToTheModel_IsExact()
	{
		var fbx = Model("models/kenney/props/Mini/Models/FBX format/barrel.fbx");
		_ws.Write("models/kenney/props/Mini/Models/FBX format/Textures/colormap.png");

		var found = Assert.Single(Resolve(fbx, "Textures/colormap.png"));

		Assert.True(found.Exact);
		Assert.EndsWith("FBX format/Textures/colormap.png", found.Path);
	}

	[Fact]
	public void Resolve_AbsoluteAuthorPath_FindsTheNameInThePacksTextures()
	{
		var fbx = Model("models/synty/environment/Dungeon/FBX/Characters/ghost.fbx");
		_ws.Write("models/synty/environment/Dungeon/Textures/Dungeons_Texture_01.png");

		var found = Assert.Single(Resolve(fbx, "U:/Dropbox/SyntyStudios/Textures/Dungeons_Texture_01.png"));

		Assert.True(found.Exact);
		Assert.EndsWith("Dungeon/Textures/Dungeons_Texture_01.png", found.Path);
	}

	[Fact]
	public void Resolve_MissingPsd_FallsBackToThePlainNumberedVariant()
	{
		var fbx = Model("models/synty/environment/Dungeon/FBX/SM_Env_Altar.fbx");
		_ws.Write("models/synty/environment/Dungeon/Textures/Dungeons_Texture_01_A.png");
		_ws.Write("models/synty/environment/Dungeon/Textures/Dungeons_Texture_01.png");
		_ws.Write("models/synty/environment/Dungeon/Textures/Dungeons_Texture_02.png");

		var found = Assert.Single(Resolve(fbx, "../../Textures/Dungeons_Texture.psd"));

		Assert.False(found.Exact);
		Assert.EndsWith("/Dungeons_Texture_01.png", found.Path);
	}

	[Fact]
	public void Resolve_PrefersTheNearestOfSeveralSameNamedFiles_AndStaysInThePack()
	{
		var fbx = Model("models/kenney/props/Arcade/Models/FBX format/cabinet.fbx");
		_ws.Write("models/kenney/props/Arcade/Models/Textures/colormap.png");
		_ws.Write("models/kenney/props/Arcade/Other/deep/colormap.png");
		_ws.Write("models/kenney/props/Food/Textures/only_in_food.png");

		var resolved = Resolve(fbx, "Textures/colormap.png", "only_in_food.png");

		var found = Assert.Single(resolved);
		Assert.EndsWith("Arcade/Models/Textures/colormap.png", found.Path);
	}

	private string Model(string relPath) => _ws.Write(relPath).Replace('\\', '/');

	private static List<ResolvedTexture> Resolve(string fbx, params string[] refs) =>
		TextureLocator.Resolve(fbx, refs, TextureLocator.PackRootFor(fbx, "models"));
}
