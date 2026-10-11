using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetManager.Core;

/// Reads the files that might be exact copies of each other, to tell. Only
/// records sharing type, name and size with another are candidates, and each
/// is read once: the hash stays until the file changes. Opening a file on the
/// NAS costs ~55 ms, so a few are read at once.
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

	/// Records sharing type, name and size with another, without a hash yet.
	public static List<AssetRecord> CandidatesIn(IEnumerable<AssetRecord> records) =>
		records
			.GroupBy(r => (r.Type, r.FileNameLower, r.Size))
			.Where(g => g.Skip(1).Any())
			.SelectMany(g => g)
			.Where(r => r.Hash == null)
			.ToList();
}
