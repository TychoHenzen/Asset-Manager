using System;
using System.Collections.Generic;

namespace AssetManager.Core;

/// Everything the grid filters by. Same fields the GDScript grid handed to
/// _matching_assets, with the folder prefix workspace-relative.
public sealed record CatalogQuery
{
	public string Prefix { get; init; } = "";
	public IReadOnlyList<string> Tags { get; init; } = [];
	public IReadOnlyList<string> Extensions { get; init; } = [];
	public string Subtype { get; init; } = "";

	/// Quoted means exact substring, anything else is a subsequence match.
	public string Search { get; init; } = "";
	public SortMode Sort { get; init; } = SortMode.Name;
	public int Page { get; init; }
	public int PerPage { get; init; } = 100;

	/// One result per asset: its other versions (formats, exact copies) are
	/// folded into it.
	public bool GroupVersions { get; init; } = true;

	public bool IsExactSearch =>
		Search.Length >= 2 && Search.StartsWith('"') && Search.EndsWith('"');

	public string SearchTermLower =>
		(IsExactSearch ? Search[1..^1] : Search).ToLowerInvariant();

	public CatalogQuery WithPage(int page) => this with { Page = page };
}
