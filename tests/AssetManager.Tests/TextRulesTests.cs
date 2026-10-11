using AssetManager.Core;

namespace AssetManager.Tests;

public class TextRulesTests
{
	[Fact]
	public void CompareNoCase_PutsUnderscoreBeforeLetters_LikeGodotNocasecmp()
	{
		Assert.True(TextRules.CompareNoCase("a_b", "ab") < 0);
		Assert.True(TextRules.CompareNoCase("Apple", "apple") == 0);
		Assert.True(TextRules.CompareNoCase("abc", "abcd") < 0);
	}

	[Theory]
	[InlineData("swrd", "wooden_sword_01.glb", true)]
	[InlineData("drws", "wooden_sword_01.glb", false)]
	[InlineData("", "anything", true)]
	public void IsSubsequence_MatchesCharactersInOrder(string query, string text, bool expected) =>
		Assert.Equal(expected, TextRules.IsSubsequence(query, text));

	[Theory]
	[InlineData("12", true)]
	[InlineData("-3", true)]
	[InlineData("+3", true)]
	[InlineData("-", false)]
	[InlineData("1a", false)]
	[InlineData("", false)]
	public void IsValidInt_FollowsGodotRules(string text, bool expected) =>
		Assert.Equal(expected, TextRules.IsValidInt(text));

	[Fact]
	public void ThumbnailKey_IsMd5OfPathMtimeSize_SameAsGodotMd5Text() =>
		Assert.Equal(
			"4fde79cdf36a71aa3a7b8e7896d0a1ea",
			TextRules.ThumbnailKey("Z:/Tycho/Projects/AssetLibrary/images/craftpix/hero.png", 1760000000, 4096));

	[Fact]
	public void ThumbnailPath_IsTypeThenShardThenKey()
	{
		var path = ThumbnailPaths.For("Z:/lib/", "images", "4fde79cdf36a71aa3a7b8e7896d0a1ea");
		Assert.Equal("Z:/lib/.assetmanager/thumbnails/images/4f/4fde79cdf36a71aa3a7b8e7896d0a1ea.webp", path);
	}
}
