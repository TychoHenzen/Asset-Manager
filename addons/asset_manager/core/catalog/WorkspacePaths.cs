using System;

namespace AssetManager.Core;

/// Converts between the absolute "/"-separated paths the GDScript UI holds
/// and the workspace-relative paths the catalog stores.
public static class WorkspacePaths
{
	public static string Absolute(string workspace, string relPath) =>
		relPath.Length == 0 ? workspace.TrimEnd('/') : workspace.TrimEnd('/') + "/" + relPath;

	/// "" for the workspace itself or anything outside it.
	public static string Relative(string workspace, string absolute)
	{
		var root = workspace.TrimEnd('/');
		if (absolute.Length <= root.Length + 1 || !absolute.StartsWith(root + "/", StringComparison.Ordinal))
			return "";
		return absolute[(root.Length + 1)..].TrimEnd('/');
	}
}
