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
	public static long Read(BinaryReader reader, string magic, int version) =>
		Read(reader, magic, version, version, out _);

	/// For a format that still reads its older versions: `version` is the one
	/// found, between `oldest` and `newest`.
	public static long Read(BinaryReader reader, string magic, int oldest, int newest, out int version)
	{
		var found = Encoding.ASCII.GetString(reader.ReadBytes(4));
		if (found != magic)
			throw new InvalidDataException($"Expected {magic} header, found '{found}'.");
		version = reader.ReadInt32();
		if (version < oldest || version > newest)
			throw new InvalidDataException($"{magic} format version {version} is not supported (expected {oldest} to {newest}).");
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
	public static void WriteAtomically(string path, byte[] bytes) =>
		WriteAtomically(path, stream => stream.Write(bytes));

	/// The same, for a file written straight to disk as it is produced rather
	/// than built in memory first.
	public static void WriteAtomically(string path, Action<Stream> write)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var temp = path + ".tmp";
		using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
			write(stream);
		File.Move(temp, path, overwrite: true);
	}

	/// Large sequential transfers keep the round trips to the NAS few.
	public const int BufferSize = 1 << 20;
}
