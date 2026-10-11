using System.Collections.Generic;

namespace AssetManager.Core;

/// Lists one folder, not recursively. The listing comes back whole or not at
/// all, so a request that fails partway leaves nothing half-read behind.
public interface IDirectoryLister
{
	/// `relDir` is workspace-relative, "/"-separated; "" is the workspace.
	/// Returns an empty list for a folder that does not exist (it was deleted
	/// since its parent was listed). Throws IOException when the listing
	/// failed and is worth retrying.
	IReadOnlyList<DirEntry> List(string relDir);
}
