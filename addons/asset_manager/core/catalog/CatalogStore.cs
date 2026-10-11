using System;
using System.IO;

namespace AssetManager.Core;

/// Persists a catalog under <workspace>/.assetmanager/. A workspace can be
/// opened from several machines, so each file carries a generation: a higher
/// number on disk than the one loaded means someone else wrote it since.
/// Every method does file I/O and belongs on a background thread.
public sealed class CatalogStore
{
	public const string DirName = ".assetmanager";

	// A scan and a delayed subtype save can both want to write catalog.bin,
	// and they share one temp file name.
	private readonly object _writeGate = new();
	private long _catalogGeneration;
	private long _tagsGeneration;

	public CatalogStore(string workspaceRoot)
	{
		Dir = Path.Combine(workspaceRoot, DirName);
	}

	public string Dir { get; }
	public string CatalogPath => Path.Combine(Dir, "catalog.bin");
	public string TagsPath => Path.Combine(Dir, "tags.bin");

	/// Fills `catalog` from disk. False when there is no catalog.bin yet.
	public bool TryLoad(Catalog catalog)
	{
		if (!File.Exists(CatalogPath))
			return false;

		CatalogFileContents contents;
		using (var stream = new FileStream(CatalogPath, FileMode.Open, FileAccess.Read, FileShare.Read, StoreHeader.BufferSize, FileOptions.SequentialScan))
			contents = CatalogFile.Deserialize(stream, catalog.Types);
		catalog.Load(contents.Records, contents.TagNames);
		_catalogGeneration = contents.Generation;

		if (File.Exists(TagsPath))
		{
			var tags = TagOverridesFile.Deserialize(File.ReadAllBytes(TagsPath));
			catalog.ApplyUserTagOverrides(tags.Overrides);
			_tagsGeneration = tags.Generation;
		}
		return true;
	}

	/// Reads 16 bytes per file.
	public bool IsStale() =>
		StoreHeader.PeekGeneration(CatalogPath, CatalogFile.Magic) > _catalogGeneration
		|| StoreHeader.PeekGeneration(TagsPath, TagOverridesFile.Magic) > _tagsGeneration;

	public void SaveCatalog(Catalog catalog)
	{
		lock (_writeGate)
		{
			var generation = NextGeneration(CatalogPath, CatalogFile.Magic, _catalogGeneration);
			var snapshot = catalog.Snapshot();
			StoreHeader.WriteAtomically(CatalogPath, stream => CatalogFile.Serialize(stream, snapshot, catalog.Types, generation));
			_catalogGeneration = generation;
		}
	}

	/// If another machine saved tags since we loaded, its overrides are folded
	/// in first, except for `editedPath`, which this save is about.
	public void SaveTags(Catalog catalog, string editedPath)
	{
		lock (_writeGate)
			SaveTagsLocked(catalog, editedPath);
	}

	private void SaveTagsLocked(Catalog catalog, string editedPath)
	{
		if (StoreHeader.PeekGeneration(TagsPath, TagOverridesFile.Magic) > _tagsGeneration)
		{
			var disk = TagOverridesFile.Deserialize(File.ReadAllBytes(TagsPath)).Overrides;
			disk.Remove(editedPath);
			catalog.ApplyUserTagOverrides(disk);
		}

		var generation = NextGeneration(TagsPath, TagOverridesFile.Magic, _tagsGeneration);
		StoreHeader.WriteAtomically(TagsPath, TagOverridesFile.Serialize(catalog.UserTagOverrides(), generation));
		_tagsGeneration = generation;
	}

	private static long NextGeneration(string path, string magic, long loaded) =>
		Math.Max(StoreHeader.PeekGeneration(path, magic), loaded) + 1;
}
