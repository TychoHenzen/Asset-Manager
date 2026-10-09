using AssetManager.Core;

namespace AssetManager.Tests;

public class AutoTagsTests
{
	[Fact]
	public void Build_LowercasesAndUnderscoresFolderNames()
	{
		var tags = AutoTags.Build("Craftpix/Dark Forest", Fixtures.Types());
		Assert.Equal(["craftpix", "dark_forest"], tags);
	}

	[Fact]
	public void Build_DropsNoiseWordsPluralsNumbersHiddenFoldersAndTypeIds()
	{
		var tags = AutoTags.Build("vendor/Textures/2k/123/.hidden/images/sprites/vendor", Fixtures.Types());
		Assert.Equal(["vendor", "sprites"], tags);
	}

	[Fact]
	public void Build_EmptyDirectory_GivesNoTags() =>
		Assert.Empty(AutoTags.Build("", Fixtures.Types()));
}
