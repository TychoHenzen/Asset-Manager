using System.IO;
using System.Text.RegularExpressions;

namespace AssetManager.Core;

/// Port of core/shared/resource_header.gd: what a text resource says it is on
/// its first line. A .tres can hold anything, so its extension doesn't place
/// it; the header does.
public static partial class ResourceHeader
{
	[GeneratedRegex("^\\[gd_resource .*type=\"([^\"]+)\"")]
	private static partial Regex TypePattern();

	/// "" when the file is missing, unreadable or not a text resource.
	public static string TypeOf(string path)
	{
		string? firstLine;
		try
		{
			using var reader = new StreamReader(path);
			firstLine = reader.ReadLine();
		}
		catch (IOException)
		{
			return "";
		}
		catch (System.UnauthorizedAccessException)
		{
			return "";
		}

		if (firstLine == null)
			return "";
		var match = TypePattern().Match(firstLine);
		return match.Success ? match.Groups[1].Value : "";
	}
}
