namespace AssetManager.Core;

/// Workspace-relative paths are "<type>/<folders...>/<file>".
internal static class RelPaths
{
	public static string Bucket(string relPath)
	{
		var slash = relPath.IndexOf('/');
		return slash < 0 ? "" : relPath[..slash];
	}

	public static string FileName(string relPath)
	{
		var slash = relPath.LastIndexOf('/');
		return slash < 0 ? relPath : relPath[(slash + 1)..];
	}

	public static string Parent(string relPath)
	{
		var slash = relPath.LastIndexOf('/');
		return slash < 0 ? "" : relPath[..slash];
	}

	/// The folders between the type bucket and the file, the part that turns
	/// into tags.
	public static string DirBelowBucket(string relPath)
	{
		var first = relPath.IndexOf('/');
		var last = relPath.LastIndexOf('/');
		return first < 0 || last <= first ? "" : relPath[(first + 1)..last];
	}
}
