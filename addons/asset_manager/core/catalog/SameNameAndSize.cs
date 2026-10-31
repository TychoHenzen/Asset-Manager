using System;
using System.Collections.Generic;

namespace AssetManager.Core;

/// Records that could be exact copies: same type, same file name ignoring
/// case, same size. Compared in place, without a lowercase copy of each name.
internal sealed class SameNameAndSize : IEqualityComparer<AssetRecord>
{
	public static readonly SameNameAndSize Instance = new();

	public bool Equals(AssetRecord? a, AssetRecord? b) =>
		ReferenceEquals(a, b)
		|| (a != null && b != null
			&& a.Type == b.Type
			&& a.Size == b.Size
			&& TextRules.EqualsLower(a.FileName, b.FileName));

	public int GetHashCode(AssetRecord r) => HashCode.Combine(r.Type, r.Size, TextRules.HashLower(r.FileName));
}
