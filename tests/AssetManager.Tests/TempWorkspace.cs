namespace AssetManager.Tests;

/// A throwaway workspace folder on local disk.
internal sealed class TempWorkspace : IDisposable
{
	public TempWorkspace()
	{
		Root = Path.Combine(Path.GetTempPath(), "am-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);
	}

	public string Root { get; }

	/// Godot-style path, forward slashes.
	public string GodotRoot => Root.Replace('\\', '/');

	public string Write(string relPath, string content = "x", DateTime? modified = null)
	{
		var full = Path.Combine(Root, relPath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		File.WriteAllText(full, content);
		if (modified != null)
			File.SetLastWriteTimeUtc(full, modified.Value);
		return full;
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(Root, recursive: true);
		}
		catch (IOException)
		{
			// A leftover temp folder is harmless.
		}
	}
}
