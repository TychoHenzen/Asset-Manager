using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;

namespace AssetManager.Core;

/// Lists folders on disk. On Windows the size and mtime come back with each
/// entry; asking per file costs a network round trip on a NAS share (measured
/// ~55 ms per file against ~1.3 ms per file this way).
public sealed class FileSystemLister(string workspaceRoot) : IDirectoryLister
{
	private static readonly EnumerationOptions Options = new()
	{
		RecurseSubdirectories = false,
		IgnoreInaccessible = true,
		ReturnSpecialDirectories = false,
		// Same as DirAccess's default include_hidden = false.
		AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
	};

	private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));

	public IReadOnlyList<DirEntry> List(string relDir)
	{
		var dir = relDir.Length == 0 ? _root : Path.Combine(_root, relDir.Replace('/', Path.DirectorySeparatorChar));
		var entries = new List<DirEntry>();
		try
		{
			var listing = new FileSystemEnumerable<DirEntry>(dir, (ref FileSystemEntry e) => new DirEntry(
				e.FileName.ToString(),
				e.IsDirectory,
				e.IsDirectory ? 0 : e.Length,
				e.LastWriteTimeUtc.ToUnixTimeSeconds()), Options);
			entries.AddRange(listing);
		}
		catch (DirectoryNotFoundException)
		{
			// ASSUMPTION: the NAS fails with "unexpected network error", never
			// with "not found", so not-found means the folder really went away.
			return [];
		}
		return entries;
	}
}
