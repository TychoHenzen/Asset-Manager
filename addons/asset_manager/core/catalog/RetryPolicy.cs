using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace AssetManager.Core;

/// Retries a request that failed on an unreliable link. The library sits on
/// a LAN share that drops single requests ("An unexpected network error
/// occurred", 0x8007003B) at random points of a long walk, while the same
/// folder lists fine a moment later.
public sealed class RetryPolicy(IReadOnlyList<TimeSpan> delays)
{
	/// About two minutes in all before a request is given up on.
	public static readonly RetryPolicy Network = new([
		TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
		TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60),
	]);

	/// Retries without waiting, for tests.
	public static RetryPolicy Immediate(int retries) => new(new TimeSpan[retries]);

	/// Runs `request`, retrying failures worth retrying. Returns false when it
	/// still failed after the last retry, or failed in a way a retry cannot
	/// fix; `error` says why. Waits honour `cancel`.
	/// `onRetry(attempt, error)` is told about each failure that will be retried.
	public bool TryRun<T>(Func<T> request, CancellationToken cancel, out T result, out Exception? error,
		Action<int, Exception>? onRetry = null)
	{
		for (var attempt = 0; ; attempt++)
		{
			cancel.ThrowIfCancellationRequested();
			try
			{
				result = request();
				error = null;
				return true;
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				if (!IsWorthRetrying(e) || attempt >= delays.Count)
				{
					result = default!;
					error = e;
					return false;
				}
				onRetry?.Invoke(attempt + 1, e);
				if (delays[attempt] > TimeSpan.Zero && cancel.WaitHandle.WaitOne(delays[attempt]))
					cancel.ThrowIfCancellationRequested();
			}
		}
	}

	/// Like TryRun, but throws the last error instead of returning false.
	public T Run<T>(Func<T> request, CancellationToken cancel, Action<int, Exception>? onRetry = null)
	{
		if (TryRun(request, cancel, out var result, out var error, onRetry))
			return result;
		throw error!;
	}

	public void Run(Action request, CancellationToken cancel, Action<int, Exception>? onRetry = null) =>
		Run(() => { request(); return true; }, cancel, onRetry);

	/// A name too long or a missing file stays that way, and access denied is
	/// a permission, not the link.
	private static bool IsWorthRetrying(Exception e) =>
		e is not (UnauthorizedAccessException or PathTooLongException or FileNotFoundException or DirectoryNotFoundException);
}
