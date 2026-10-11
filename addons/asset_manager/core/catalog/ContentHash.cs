using System.IO;
using System.Security.Cryptography;

namespace AssetManager.Core;

/// SHA-256 of a file, read once front to back.
public static class ContentHash
{
	private const int BufferSize = 1 << 20;

	public static byte[] Of(string fullPath)
	{
		using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
		return SHA256.HashData(stream);
	}
}
