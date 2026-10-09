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
			var ca = char.ToLowerInvariant(a[i]);
			var cb = char.ToLowerInvariant(b[i]);
			if (ca != cb)
				return ca < cb ? -1 : 1;
		}
		return a.Length.CompareTo(b.Length);
	}

	/// String.is_subsequence_ofn, with both sides already lowercased.
	public static bool IsSubsequence(string queryLower, string textLower)
	{
		var q = 0;
		for (var t = 0; t < textLower.Length && q < queryLower.Length; t++)
		{
			if (textLower[t] == queryLower[q])
				q++;
		}
		return q == queryLower.Length;
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
