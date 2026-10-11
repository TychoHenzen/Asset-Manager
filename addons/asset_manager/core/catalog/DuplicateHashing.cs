using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetManager.Core;

/// Reads the files that might be exact copies of each other, to tell. Only
/// records sharing type, name and size with another are candidates, and each
/// is read once: the hash stays until the file changes. Opening a file on the
/// NAS costs ~55 ms whatever else is going on: 4, 8, 16 and 32 reads at once
/// all measured 15-18 files/s, so a few at once is enough.
public sealed class DuplicateHashing(Catalog catalog, Func<string, byte[]> hashFile, RetryPolicy retry)
{
	private int _hashed;
	private int _unreadable;

	public int Parallelism { get; init; } = 4;

	/// Files hashed so far, and files that could not be read even after
	/// retrying (they are tried again next scan). Kept when Run is stopped.
	public int Hashed => _hashed;
	public int Unreadable => _unreadable;

	/// `onHashed` is called after each stored hash, from worker threads.
	public void Run(IProgress<ScanProgress>? progress, Action onHashed, CancellationToken cancel)
	{
		var candidates = CandidatesIn(catalog.Snapshot().Records);
		var done = 0;
		var options = new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancel };
		Parallel.ForEach(candidates, options, record =>
		{
			var (size, mtime) = (record.Size, record.MTime);
			if (!retry.TryRun(() => hashFile(record.RelPath), cancel, out var hash, out _))
				Interlocked.Increment(ref _unreadable);
			else if (catalog.SetHash(record, size, mtime, hash))
			{
				Interlocked.Increment(ref _hashed);
				onHashed();
			}
			var count = Interlocked.Increment(ref done);
			progress?.Report(new ScanProgress("duplicates", count, record.RelPath, count, candidates.Count));
		});
	}

	/// Records sharing type, name and size with another, without a hash yet,
	/// most likely copies first. Measured on the library: of candidate pairs
	/// that also share an mtime 94% were copies, against 5% overall, and a
	/// big group (one icon sheet in 90 packs) folds the most tiles per read.
	/// The full first pass takes hours at ~55 ms per file opened on the NAS,
	/// so the order decides how much a stopped pass has already found.
	public static List<AssetRecord> CandidatesIn(IEnumerable<AssetRecord> records) =>
		records
			.GroupBy(r => r, SameNameAndSize.Instance)
			.Where(g => g.Skip(1).Any())
			.OrderByDescending(g => g.GroupBy(r => r.MTime).Any(m => m.Skip(1).Any()))
			.ThenByDescending(g => g.Count())
			.SelectMany(g => g.OrderBy(r => r.MTime))
			.Where(r => r.Hash == null)
			.ToList();
}
