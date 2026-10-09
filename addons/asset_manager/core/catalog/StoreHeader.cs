using System;
using System.IO;
using System.Text;

namespace AssetManager.Core;

/// Both store files open with the same 16 bytes: 4-byte magic, int32 format
/// version, int64 generation. The generation sits at a fixed offset so a
/// staleness check reads 16 bytes instead of the whole file.
internal static class StoreHeader
{
	public const int Size = 16;

	public static void Write(BinaryWriter writer, string magic, int version, long generation)
	{
		writer.Write(Encoding.ASCII.GetBytes(magic));
		writer.Write(version);
		writer.Write(generation);
	}

	/// Throws InvalidDataException on a wrong magic or version.
	public static long Read(BinaryReader reader, string magic, int version)
	{
		var found = Encoding.ASCII.GetString(reader.ReadBytes(4));
		if (found != magic)
			throw new InvalidDataException($"Expected {magic} header, found '{found}'.");
		var foundVersion = reader.ReadInt32();
		if (foundVersion != version)
			throw new InvalidDataException($"{magic} format version {foundVersion} is not supported (expected {version}).");
		return reader.ReadInt64();
	}

	/// The generation on disk, or -1 when the file is missing or unreadable.
	public static long PeekGeneration(string path, string magic)
	{
		try
		{
			using var stream = File.OpenRead(path);
			var buffer = new byte[Size];
			if (stream.Read(buffer, 0, Size) < Size)
				return -1;
			if (Encoding.ASCII.GetString(buffer, 0, 4) != magic)
				return -1;
			return BitConverter.ToInt64(buffer, 8);
		}
		catch (IOException)
		{
			return -1;
		}
		catch (UnauthorizedAccessException)
		{
			return -1;
		}
	}

	/// One sequential write to a temp file, then a rename, so a reader on
	/// another machine never sees half a file and a crash never leaves one.
	public static void WriteAtomically(string path, byte[] bytes)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var temp = path + ".tmp";
		File.WriteAllBytes(temp, bytes);
		File.Move(temp, path, overwrite: true);
	}
}
