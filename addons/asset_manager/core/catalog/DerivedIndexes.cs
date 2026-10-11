using System.Collections.Generic;

namespace AssetManager.Core;

/// What the catalog derives from its records, kept per type: sort orders and
/// version groups, each stamped with the version of the records (and hashes)
/// it was built from. A change to one type's records drops only that type's
/// indexes and the all-types orders, so a scan of sounds never costs the
/// next images query a re-sort. Not thread-safe on its own; Catalog guards
/// it with its lock and builds outside it.
internal sealed class DerivedIndexes
{
	private readonly SortOrders _orders = new();
	private readonly int[] _typeVersions;
	private readonly int[] _hashVersions;
	private readonly VersionIndex?[] _versions;
	private readonly Stamp[] _versionsBuiltFor;

	public DerivedIndexes(int typeCount)
	{
		_typeVersions = new int[typeCount];
		_hashVersions = new int[typeCount];
		_versions = new VersionIndex?[typeCount];
		_versionsBuiltFor = new Stamp[typeCount];
	}

	/// Goes up with any change to any type's records.
	public int RecordsVersion { get; private set; }

	/// What an order of `scope` (a type, or -1 for all) is built against.
	public int OrderStamp(int scope) => scope >= 0 ? _typeVersions[scope] : RecordsVersion;

	public AssetRecord[]? TryGetOrder(int scope, SortMode sort) => _orders.TryGet(scope, sort, OrderStamp(scope));

	/// Kept only if the records did not change since `builtFor` was read.
	public void KeepOrder(int scope, SortMode sort, int builtFor, AssetRecord[] order) =>
		_orders.Keep(scope, sort, builtFor, OrderStamp(scope), order);

	/// The versions of one type's records and of its content hashes.
	public readonly record struct Stamp(int Records, int Hashes);

	public Stamp VersionsStamp(int type) => new(_typeVersions[type], _hashVersions[type]);

	public VersionIndex? TryGetVersions(int type) =>
		_versions[type] is { } versions && _versionsBuiltFor[type] == VersionsStamp(type) ? versions : null;

	/// Kept only if neither the records nor the hashes changed since
	/// `builtFor` was read.
	public void KeepVersions(int type, Stamp builtFor, VersionIndex versions)
	{
		if (builtFor != VersionsStamp(type))
			return;
		_versions[type] = versions;
		_versionsBuiltFor[type] = builtFor;
	}

	public void HashChanged(int type) => _hashVersions[type]++;

	public bool Holds(int type) => _versions[type] != null || _orders.Holds(type);

	/// Drops what was built from the records of `types`, and the orders of
	/// every type together.
	public void RecordsChanged(IEnumerable<int> types)
	{
		RecordsVersion++;
		_orders.Drop(-1);
		foreach (var type in types)
		{
			_typeVersions[type]++;
			_versions[type] = null;
			_orders.Drop(type);
		}
	}
}
