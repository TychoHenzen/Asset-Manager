using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace AssetManager.Bridge;

/// Runs work off the main thread and can stop all of it. Godot unloads a C#
/// tool assembly on every rebuild and on plugin disable, and the unload
/// fails while a thread of ours is still running, so every task goes through
/// here and Shutdown cancels and joins them.
public sealed class BackgroundWork
{
	private static readonly List<WeakReference<BackgroundWork>> Live = [];

	private readonly object _gate = new();
	private readonly HashSet<Task> _running = [];
	private CancellationTokenSource _lifetime = new();

	static BackgroundWork()
	{
		var context = AssemblyLoadContext.GetLoadContext(typeof(BackgroundWork).Assembly);
		if (context != null)
			context.Unloading += _ => ShutdownAll();
	}

	public BackgroundWork()
	{
		lock (Live)
			Live.Add(new WeakReference<BackgroundWork>(this));
	}

	public CancellationToken Token => _lifetime.Token;

	public void Run(Action<CancellationToken> work, string label)
	{
		var token = _lifetime.Token;
		var task = Task.Run(() =>
		{
			try
			{
				work(token);
			}
			catch (OperationCanceledException)
			{
				// Shutdown or a newer request superseded this one.
			}
			catch (Exception e)
			{
				GD.PushError($"AssetManager: {label} failed: {e}");
			}
		}, token);

		lock (_gate)
			_running.Add(task);
		task.ContinueWith(t =>
		{
			lock (_gate)
				_running.Remove(t);
		}, TaskScheduler.Default);
	}

	/// Cancels everything and waits up to `timeout` for it to stop.
	public void Shutdown(TimeSpan timeout)
	{
		Task[] running;
		lock (_gate)
		{
			_lifetime.Cancel();
			running = [.. _running];
		}
		Task.WaitAll(running, timeout);
		lock (_gate)
			_lifetime = new CancellationTokenSource();
	}

	/// Queues `action` onto the main thread. Godot's deferred calls are the
	/// only safe way back from a pool thread to signals and UI.
	public static void OnMainThread(Action action) => Callable.From(action).CallDeferred();

	private static void ShutdownAll()
	{
		lock (Live)
		{
			foreach (var weak in Live)
			{
				if (weak.TryGetTarget(out var work))
					work.Shutdown(TimeSpan.FromSeconds(10));
			}
			Live.Clear();
		}
	}
}
