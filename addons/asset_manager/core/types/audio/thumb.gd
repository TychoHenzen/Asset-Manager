@tool
extends RefCounted

## Thumbnail renderer for sounds and music: the waveform, painted into a square.
## Cover art wins when present, the waveform is the fallback.
## None of it needs a viewport or a server: reading the file (slow on a
## network share), decoding it, folding peaks and painting all happen in
## prepare() on a worker. render_prepared only hands the result over.

const WAVE_COLOR: Color = Color(0.45, 0.78, 1.0)
const BG_COLOR: Color = Color(0.12, 0.13, 0.16, 1.0)

## Worker thread. The finished thumbnail, or null when the file would not read.
static func prepare(path: String) -> Variant:
	var art := CoverArtExtractor.extract_image(path)
	if art != null and not art.is_empty():
		return art

	var stream := _load_stream(path)
	if stream == null:
		return null
	var peaks := WaveformGenerator.peaks_blocking(stream, ThumbnailSizes.THUMB_SIZE)
	if peaks.is_empty():
		return null
	return _paint(peaks, ThumbnailSizes.THUMB_SIZE)

static func render_prepared(prepared: Variant, _viewport: ThumbnailViewport, _path: String) -> Image:
	return prepared as Image

## Same loading as the audio preview. These files live outside res://, no
## imported resource to ask for.
static func _load_stream(path: String) -> AudioStream:
	var ext := path.get_extension().to_lower()

	if ext == "wav":
		# Audio preview hand-parses RIFF (predates this API). Worth revisiting
		# together if a WAV shows up blank here.
		return AudioStreamWAV.load_from_file(path)

	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		return null
	var buffer := file.get_buffer(file.get_length())
	file.close()

	if ext == "ogg":
		return AudioStreamOggVorbis.load_from_buffer(buffer)
	if ext == "mp3":
		var stream := AudioStreamMP3.new()
		stream.data = buffer
		return stream

	return null

## WaveformGenerator.paint() returns an ImageTexture for the preview, but the
## cache stores Images, so the drawing is duplicated here rather than pulling
## pixels back off the GPU.
static func _paint(peaks: Array[Vector2], size: int) -> Image:
	var image := Image.create_empty(size, size, false, Image.FORMAT_RGBA8)
	image.fill(BG_COLOR)

	var mid_y := size / 2.0

	for x in range(mini(size, peaks.size())):
		var top := int(mid_y - peaks[x].y * mid_y)
		var bottom := int(mid_y - peaks[x].x * mid_y)
		top = clampi(top, 0, size - 1)
		bottom = clampi(maxi(bottom, top + 1), 0, size - 1)

		for y in range(top, bottom + 1):
			image.set_pixel(x, y, WAVE_COLOR)

	return image
