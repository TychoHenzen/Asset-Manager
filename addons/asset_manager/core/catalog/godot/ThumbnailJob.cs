using AssetManager.Core;

namespace AssetManager.Bridge;

/// One thumbnail to find or make. The cache path is worked out from the size
/// and mtime the scan stored, so no file is stat'ed to build it.
internal sealed record ThumbnailJob(int RequestId, string AbsolutePath, string TypeId, long Size, long MTime, string ThumbPath)
{
	/// Null for an asset whose size is not known yet (migrated from index.db
	/// and not rescanned): its key would be wrong.
	public static ThumbnailJob? From(int requestId, string workspace, string path, string type, long size, long mtime) =>
		path.Length == 0 || size < 0
			? null
			: new ThumbnailJob(requestId, path, type, size, mtime, ThumbnailPaths.ForAsset(workspace, type, path, mtime, size));
}
