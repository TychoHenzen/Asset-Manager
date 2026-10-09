using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Threading;

namespace AssetManager.Core;

/// Walks the workspace and reports every file with the size and mtime the
/// directory listing already returned. On Windows those come back with each
/// entry for free; asking per file costs a network round trip on a NAS share
/// (measured ~55 ms per file against ~1.3 ms per file this way).
public static class LibraryScanner
{
	private const int ProgressEvery = 500;

	private static readonly EnumerationOptions Options = new()
	{
		RecurseSubdirectories = true,
		IgnoreInaccessible = true,
		ReturnSpecialDirectories = false,
		// Same as DirAccess's default include_hidden = false.
		AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
	};

	/// `scanRoot` is workspace-relative: "" walks every type bucket, anything
	/// else walks that subtree only and must start with a type folder.
	public static List<FoundFile> Walk(
		string workspaceRoot,
		string scanRoot,
		TypeTable types,
		IProgress<ScanProgress>? progress,
		CancellationToken cancel)
	{
		var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
		var found = new List<FoundFile>();

		foreach (var relRoot in RootsFor(scanRoot, types))
		{
			var dir = Path.Combine(rootFull, relRoot.Replace('/', Path.DirectorySeparatorChar));
			if (!Directory.Exists(dir))
				continue;

			var walk = new FileSystemEnumerable<FoundFile>(
				dir,
				(ref FileSystemEntry entry) => ToFound(ref entry, rootFull),
				Options)
			{
				ShouldIncludePredicate = (ref FileSystemEntry entry) =>
				{
					cancel.ThrowIfCancellationRequested();
					return !entry.IsDirectory;
				},
			};

			foreach (var file in walk)
			{
				found.Add(file);
				if (progress != null && found.Count % ProgressEvery == 0)
					progress.Report(new ScanProgress(found.Count, RelPaths.Parent(file.RelPath)));
			}
		}

		progress?.Report(new ScanProgress(found.Count, ""));
		return found;
	}

	private static IEnumerable<string> RootsFor(string scanRoot, TypeTable types)
	{
		if (scanRoot.Length > 0)
		{
			yield return scanRoot;
			yield break;
		}
		foreach (var type in types.All)
			yield return type.Id;
	}

	private static FoundFile ToFound(ref FileSystemEntry entry, string rootFull)
	{
		var dir = entry.Directory;
		var rel = dir.Length > rootFull.Length
			? string.Concat(dir[(rootFull.Length + 1)..], "/", entry.FileName).Replace('\\', '/')
			: entry.FileName.ToString();
		return new FoundFile(rel, entry.Length, entry.LastWriteTimeUtc.ToUnixTimeSeconds());
	}
}
