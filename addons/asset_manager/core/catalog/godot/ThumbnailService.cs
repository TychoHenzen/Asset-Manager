using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using AssetManager.Core;
using Godot;

namespace AssetManager.Bridge;

/// Thumbnails for the page the grid is showing, and nothing else. Workers
/// look each one up in the on-disk cache and load it; a missing image
/// thumbnail is decoded and written right there. Everything else needs a live
/// viewport, which only the main thread has, so it goes back to GDScript as
/// NeedsRender. A newer page request drops whatever the older one had left.
/// Connect bound methods to these signals, not lambdas: a GDScript lambda
/// connected to a C# object's signal crashes Godot on exit (seen on 4.7.1).
[Tool]
[GlobalClass]
public partial class ThumbnailService : RefCounted, ISerializationListener
{
	/// `webp` is the encoded thumbnail; decode with Image.load_webp_from_buffer.
	/// Bytes rather than an Image, so no C# Image wrapper outlives the call.
	[Signal] public delegate void ThumbnailReadyEventHandler(int requestId, string path, byte[] webp);
	[Signal] public delegate void NeedsRenderEventHandler(int requestId, string path, string type, long size, long mtime);

	public const int ThumbSize = 256;

	// The cost of a lookup is network latency, not CPU, so a few more workers
	// than cores is fine; many more just queues on the NAS.
	private const int WorkerCount = 6;
	private const float WebpQuality = 0.75f;

	private readonly BackgroundWork _work = new();
	private ConcurrentQueue<ThumbnailJob> _queue = new();
	private int _currentRequest;
	private int _activeWorkers;
	private string _workspace = "";

	public void Setup(string workspacePath) => _workspace = workspacePath.TrimEnd('/');

	public void OnBeforeSerialize()
	{
		// BackgroundWork's unload hook has already stopped the workers.
	}

	/// The worker count is an int, so a C# assembly reload keeps it while the
	/// workers it counted are gone; left alone, no worker would ever start
	/// again. The grid requests its page anew when it next refreshes.
	public void OnAfterDeserialize() => _activeWorkers = 0;

	/// The parallel arrays are one QueryReady page, as the grid received it.
	public void RequestPage(int requestId, string[] paths, string[] types, long[] sizes, long[] mtimes)
	{
		Interlocked.Exchange(ref _currentRequest, requestId);
		var queue = new ConcurrentQueue<ThumbnailJob>();
		for (var i = 0; i < paths.Length; i++)
		{
			var job = ThumbnailJob.From(requestId, _workspace, paths[i], types[i], sizes[i], mtimes[i]);
			if (job != null)
				queue.Enqueue(job);
		}
		Interlocked.Exchange(ref _queue, queue);
		SpawnWorkers();
	}

	/// Drops any page still loading, for when the grid is cleared.
	public void CancelAll() => Interlocked.Increment(ref _currentRequest);

	/// Saves a thumbnail GDScript rendered in a viewport. A square image is fit
	/// to `thumbSize`; a taller one is an animation strip and is kept as is.
	public void Store(string path, string type, long size, long mtime, Image image, int thumbSize)
	{
		var job = ThumbnailJob.From(0, _workspace, path, type, size, mtime);
		if (job == null || image == null || image.IsEmpty())
			return;
		_work.Run(_ =>
		{
			// The wrapper for a GDScript-owned Image is disposed here, not left
			// to the engine's shutdown.
			using var owned = image;
			if (image.GetHeight() > image.GetWidth())
			{
				Save(job.ThumbPath, image);
				return;
			}
			using var square = FitToSquare(image, thumbSize);
			Save(job.ThumbPath, square);
		}, "saving a thumbnail");
	}

	/// Where the thumbnail for this asset lives, whether or not it exists yet.
	public string ThumbPath(string path, string type, long size, long mtime) =>
		ThumbnailPaths.ForAsset(_workspace, type, path, mtime, size);

	public void Shutdown() => _work.Shutdown(TimeSpan.FromSeconds(10));

	private void Drain(CancellationToken token)
	{
		try
		{
			while (!token.IsCancellationRequested && Volatile.Read(ref _queue).TryDequeue(out var job))
			{
				if (job.RequestId == Volatile.Read(ref _currentRequest))
					Load(job);
			}
		}
		finally
		{
			Interlocked.Decrement(ref _activeWorkers);
		}
		// A page may have arrived while this worker was leaving and counted it
		// as still busy.
		if (!token.IsCancellationRequested)
			SpawnWorkers();
	}

	private void SpawnWorkers()
	{
		while (!Volatile.Read(ref _queue).IsEmpty)
		{
			var active = Volatile.Read(ref _activeWorkers);
			if (active >= WorkerCount)
				return;
			if (Interlocked.CompareExchange(ref _activeWorkers, active + 1, active) == active)
				_work.Run(Drain, "loading thumbnails");
		}
	}

	private void Load(ThumbnailJob job)
	{
		var webp = File.Exists(job.ThumbPath) ? File.ReadAllBytes(job.ThumbPath) : null;
		if (webp == null && job.TypeId == "images")
			webp = RenderImage(job);

		if (webp != null)
			BackgroundWork.OnMainThread(() => EmitSignal(SignalName.ThumbnailReady, job.RequestId, job.AbsolutePath, webp));
		else if (job.TypeId != "images")
			BackgroundWork.OnMainThread(() => EmitSignal(SignalName.NeedsRender, job.RequestId, job.AbsolutePath, job.TypeId, job.Size, job.MTime));
	}

	/// Port of the images type's thumb.gd: decode, fit, store. Returns the
	/// encoded thumbnail, null when the source would not decode.
	private static byte[]? RenderImage(ThumbnailJob job)
	{
		using var source = Image.LoadFromFile(job.AbsolutePath);
		if (source == null || source.IsEmpty())
		{
			GD.PushWarning("AssetManager: no thumbnail generated for ", job.AbsolutePath);
			return null;
		}
		using var thumb = FitToSquare(source, ThumbSize);
		return Save(job.ThumbPath, thumb);
	}

	private static byte[] Save(string thumbPath, Image image)
	{
		var webp = image.SaveWebpToBuffer(true, WebpQuality);
		Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);
		File.WriteAllBytes(thumbPath, webp);
		return webp;
	}

	/// Port of ThumbnailCache.fit_to_square: square canvas, asset scaled to fit
	/// with aspect kept and transparent padding, never cropped or stretched.
	/// Pixel art loses its edges to smooth filtering when enlarged, so
	/// upscaling stays blocky.
	public static Image FitToSquare(Image image, int size)
	{
		var scale = Math.Min((float)size / image.GetWidth(), (float)size / image.GetHeight());
		var width = Math.Max(1, (int)Math.Round(image.GetWidth() * scale, MidpointRounding.AwayFromZero));
		var height = Math.Max(1, (int)Math.Round(image.GetHeight() * scale, MidpointRounding.AwayFromZero));

		using var scaled = (Image)image.Duplicate();
		scaled.Resize(width, height, scale > 1f ? Image.Interpolation.Nearest : Image.Interpolation.Lanczos);
		if (scaled.GetFormat() != Image.Format.Rgba8)
			scaled.Convert(Image.Format.Rgba8);

		var canvas = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		canvas.Fill(new Color(0, 0, 0, 0));
		canvas.BlitRect(scaled, new Rect2I(0, 0, width, height), new Vector2I((size - width) / 2, (size - height) / 2));
		return canvas;
	}
}
