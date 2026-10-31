using AssetManager.Core;

namespace AssetManager.Tests;

/// A library in memory, whose folders can be told to fail like the NAS does.
internal sealed class FakeLibrary : IDirectoryLister
{
	private readonly Dictionary<string, List<DirEntry>> _folders = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _failuresLeft = new(StringComparer.Ordinal);

	/// Called before each listing, with the folder; lets a test act mid-walk.
	public Action<string>? BeforeList { get; set; }

	public List<string> Listed { get; } = [];

	public FakeLibrary(params string[] files)
	{
		_folders[""] = [];
		foreach (var file in files)
			Add(file);
	}

	public void Add(string relPath, long size = 10, long mtime = 1000)
	{
		var parent = RelPaths.Parent(relPath);
		EnsureFolder(parent);
		_folders[parent].Add(new DirEntry(RelPaths.FileName(relPath), false, size, mtime));
	}

	/// The next `times` listings of `relDir` throw; int.MaxValue for always.
	public void Fail(string relDir, int times = int.MaxValue) => _failuresLeft[relDir] = times;

	public IReadOnlyList<DirEntry> List(string relDir)
	{
		BeforeList?.Invoke(relDir);
		Listed.Add(relDir);
		if (_failuresLeft.TryGetValue(relDir, out var left) && left > 0)
		{
			_failuresLeft[relDir] = left == int.MaxValue ? left : left - 1;
			throw new IOException("An unexpected network error occurred.");
		}
		return _folders.TryGetValue(relDir, out var entries) ? entries.ToList() : [];
	}

	private void EnsureFolder(string relDir)
	{
		if (_folders.ContainsKey(relDir))
			return;
		_folders[relDir] = [];
		var parent = RelPaths.Parent(relDir);
		EnsureFolder(parent);
		_folders[parent].Add(new DirEntry(RelPaths.FileName(relDir), true, 0, 0));
	}
}
