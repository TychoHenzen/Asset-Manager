using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetManager.Core;

/// One query's filter, prepared once and then applied to each record. Same
/// rules as the GDScript grid's _matching_assets and _matches_search.
internal sealed class CatalogMatcher
{
	private readonly CatalogQuery _query;
	private readonly int[] _tagIds;
	private readonly IReadOnlyList<string> _tagNames;
	private readonly HashSet<string> _extensions;
	private readonly string _term;
	private readonly bool _exact;

	public CatalogMatcher(CatalogQuery query, int[] tagIds, IReadOnlyList<string> tagNames)
	{
		_query = query;
		_tagIds = tagIds;
		_tagNames = tagNames;
		_extensions = new HashSet<string>(query.Extensions.Select(e => e.ToLowerInvariant()), StringComparer.Ordinal);
		_term = query.SearchTermLower;
		_exact = query.IsExactSearch;
	}

	public bool HasSearch => _term.Length > 0;

	public bool FileNameContainsTerm(AssetRecord r) => TextRules.ContainsLower(r.FileName, _term);

	public bool Matches(AssetRecord r)
	{
		if (!r.IsUnder(_query.Prefix))
			return false;

		var tags = r.Tags;
		foreach (var wanted in _tagIds)
		{
			if (Array.IndexOf(tags, wanted) < 0)
				return false;
		}

		if (_extensions.Count > 0 && !_extensions.Contains(r.ExtensionLower))
			return false;

		if (_query.Subtype.Length > 0 && Array.IndexOf(r.Subtypes, _query.Subtype) < 0)
			return false;

		return !HasSearch || MatchesSearch(r, tags);
	}

	private bool MatchesSearch(AssetRecord r, int[] tags)
	{
		if (Hit(r.FileName))
			return true;
		foreach (var id in tags)
		{
			if (Hit(_tagNames[id]))
				return true;
		}
		return false;
	}

	/// `text` in any case; the term is lowercased.
	private bool Hit(string text) =>
		_exact ? TextRules.ContainsLower(text, _term) : TextRules.IsSubsequence(_term, text);
}
