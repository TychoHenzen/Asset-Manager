using System;
using System.Security.Cryptography;
using System.Text;

namespace AssetManager.Core;

/// The handful of Godot String methods the GDScript version leaned on,
/// reproduced so sorting, searching, tagging and thumbnail keys come out the
/// same as before.
public static class TextRules
{
	/// String.nocasecmp_to: compares lowercased code units, so "_" sorts
	/// before letters (OrdinalIgnoreCase uppercases and would put it after).
	public static int CompareNoCase(string a, string b)
	{
		var n = Math.Min(a.Length, b.Length);
		for (var i = 0; i < n; i++)
		{
			var ca = Lower(a[i]);
			var cb = Lower(b[i]);
			if (ca != cb)
				return ca < cb ? -1 : 1;
		}
		return a.Length.CompareTo(b.Length);
	}

	/// char.ToLowerInvariant, with ASCII done inline: sorting 1.58M names
	/// lowers ~700M characters, nearly all of them ASCII.
	public static char Lower(char c) =>
		c < 128 ? (c is >= 'A' and <= 'Z' ? (char)(c | 0x20) : c) : char.ToLowerInvariant(c);

	/// String.is_subsequence_ofn. `queryLower` is already lowercased, `text`
	/// is lowered a character at a time, so no lowercase copy is made of each
	/// of 1.77M file names per search.
	public static bool IsSubsequence(string queryLower, string text)
	{
		var q = 0;
		for (var t = 0; t < text.Length && q < queryLower.Length; t++)
		{
			if (Lower(text[t]) == queryLower[q])
				q++;
		}
		return q == queryLower.Length;
	}

	/// text.ToLowerInvariant().Contains(termLower), without the copy.
	public static bool ContainsLower(string text, string termLower)
	{
		for (var start = 0; start + termLower.Length <= text.Length; start++)
		{
			var i = 0;
			while (i < termLower.Length && Lower(text[start + i]) == termLower[i])
				i++;
			if (i == termLower.Length)
				return true;
		}
		return false;
	}

	/// a.ToLowerInvariant() == b.ToLowerInvariant(), without the copies.
	public static bool EqualsLower(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
	{
		if (a.Length != b.Length)
			return false;
		for (var i = 0; i < a.Length; i++)
		{
			if (Lower(a[i]) != Lower(b[i]))
				return false;
		}
		return true;
	}

	/// A hash that agrees with EqualsLower.
	public static int HashLower(ReadOnlySpan<char> text)
	{
		var hash = new HashCode();
		foreach (var c in text)
			hash.Add(Lower(c));
		return hash.ToHashCode();
	}

	/// String.is_valid_int: optional sign, then one or more ASCII digits.
	public static bool IsValidInt(string s)
	{
		var start = s.Length > 0 && (s[0] == '+' || s[0] == '-') ? 1 : 0;
		if (start == s.Length)
			return false;
		for (var i = start; i < s.Length; i++)
		{
			if (s[i] < '0' || s[i] > '9')
				return false;
		}
		return true;
	}

	/// String.strip_edges: trims ASCII whitespace and control characters.
	public static string StripEdges(string s)
	{
		var start = 0;
		var end = s.Length;
		while (start < end && s[start] <= 32)
			start++;
		while (end > start && s[end - 1] <= 32)
			end--;
		return s.Substring(start, end - start);
	}

	public static string TrimSuffix(string s, string suffix) =>
		s.EndsWith(suffix, StringComparison.Ordinal) ? s[..^suffix.Length] : s;

	/// String.get_extension, lowercased: text after the last dot of the file
	/// name, "" when there is none.
	public static string ExtensionLower(string fileName)
	{
		var dot = fileName.LastIndexOf('.');
		return dot < 0 ? "" : fileName[(dot + 1)..].ToLowerInvariant();
	}

	/// ThumbnailCache.key_for's formula: md5_text of "path|mtime|size". Kept
	/// byte-for-byte so thumbnails generated before the fork stay valid.
	public static string ThumbnailKey(string absolutePath, long mtime, long size)
	{
		var bytes = Encoding.UTF8.GetBytes(absolutePath + "|" + mtime + "|" + size);
		return Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
	}
}
