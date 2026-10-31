@tool
class_name WaveformGenerator
extends RefCounted

const WAVE_COLOR: Color = Color(0.55, 0.55, 0.55, 0.9)
const PLAYED_COLOR: Color = Color(0.35, 0.65, 1.0, 0.95)
const PLACEHOLDER_COLOR: Color = Color(0.5, 0.5, 0.5, 0.5)
const BG_COLOR: Color = Color(0, 0, 0, 0)
const DECODE_CHUNK_FRAMES: int = 65536
const MAX_DECODE_FRAMES: int = 44100 * 60 * 20 # 20 minutes @ 44.1kHz

## Each yield waits a full engine frame (~16ms @ 60fps) regardless of how
## little work happened. Yielding too often turns into the dominant cost.
const YIELD_EVERY_N_CHUNKS: int = 200

## Fake, deterministic bar pattern shown while the real waveform is still loading.
static func generate_placeholder(width: int, height: int) -> ImageTexture:
	var peaks: Array[Vector2] = []
	var rng := RandomNumberGenerator.new()
	rng.seed = 1337 ## nice
	for x in range(width):
		var amplitude: float = rng.randf_range(0.15, 0.75)
		peaks.append(Vector2(-amplitude, amplitude))
	return paint(peaks, width, height, PLACEHOLDER_COLOR)

func generate_peaks_async(stream: AudioStream, width: int) -> Array[Vector2]:
	if stream is AudioStreamWAV:
		var frames := _frames_from_wav(stream as AudioStreamWAV)
		return _bucket_peaks(frames, width)
	elif stream is AudioStreamOggVorbis or stream is AudioStreamMP3:
		var estimated_frames := int(stream.get_length() * 44100.0)
		return await _decode_and_bucket_async(stream, width, estimated_frames)
	return []

static func _frames_from_wav(wav: AudioStreamWAV) -> PackedVector2Array:
	var frames := PackedVector2Array()
	var raw: PackedByteArray = wav.data
	if raw.is_empty():
		return frames

	var is_16_bit := wav.format == AudioStreamWAV.FORMAT_16_BITS
	var bytes_per_sample := 2 if is_16_bit else 1
	var channels := 2 if wav.stereo else 1
	var frame_count := raw.size() / (bytes_per_sample * channels)
	frames.resize(frame_count)

	for frame in range(frame_count):
		var sample := _read_sample(raw, frame, channels, bytes_per_sample, is_16_bit)
		frames[frame] = Vector2(sample, sample)

	return frames

func _decode_and_bucket_async(stream: AudioStream, width: int, estimated_total_frames: int) -> Array[Vector2]:
	var playback := stream.instantiate_playback()
	if playback == null:
		return []

	playback.start(0.0)
	var columns := PeakColumns.new(width, estimated_total_frames)
	var chunks_since_yield := 0

	while columns.frames_seen() < MAX_DECODE_FRAMES:
		var chunk: PackedVector2Array = playback.mix_audio(1.0, DECODE_CHUNK_FRAMES)
		if chunk.is_empty():
			break
		columns.add(chunk)
		if chunk.size() < DECODE_CHUNK_FRAMES:
			break # short chunk = end of stream.

		chunks_since_yield += 1
		if chunks_since_yield >= YIELD_EVERY_N_CHUNKS:
			chunks_since_yield = 0
			await Engine.get_main_loop().process_frame

	playback.stop()
	return columns.finish()

## Worker-thread version for thumbnails: the same peaks, computed in one go
## with no yielding. A playback that is never handed to the audio server is a
## plain decoder, so mixing it here races with nothing.
static func peaks_blocking(stream: AudioStream, width: int) -> Array[Vector2]:
	if stream is AudioStreamWAV:
		return _bucket_peaks(_frames_from_wav(stream as AudioStreamWAV), width)
	if not (stream is AudioStreamOggVorbis or stream is AudioStreamMP3):
		return []

	var playback := stream.instantiate_playback()
	if playback == null:
		return []

	playback.start(0.0)
	var columns := PeakColumns.new(width, int(stream.get_length() * 44100.0))
	while columns.frames_seen() < MAX_DECODE_FRAMES:
		var chunk: PackedVector2Array = playback.mix_audio(1.0, DECODE_CHUNK_FRAMES)
		if chunk.is_empty():
			break
		columns.add(chunk)
		if chunk.size() < DECODE_CHUNK_FRAMES:
			break
	playback.stop()
	return columns.finish()

static func _bucket_peaks(frames: PackedVector2Array, width: int) -> Array[Vector2]:
	var peaks: Array[Vector2] = []
	var frame_count := frames.size()
	if frame_count <= 0:
		return peaks

	var frames_per_column := maxf(1.0, float(frame_count) / float(width))
	var step := maxi(1, int(frames_per_column / PeakColumns.TARGET_SAMPLES_PER_BUCKET))

	for x in range(width):
		var start_frame := int(x * frames_per_column)
		var end_frame := mini(frame_count, int((x + 1) * frames_per_column))
		if end_frame <= start_frame:
			end_frame = mini(frame_count, start_frame + 1)

		var min_val := 1.0
		var max_val := -1.0
		var frame := start_frame
		while frame < end_frame:
			var sample: float = frames[frame].x
			min_val = minf(min_val, sample)
			max_val = maxf(max_val, sample)
			frame += step

		peaks.append(Vector2(min_val, max_val))

	return peaks

static func paint(peaks: Array[Vector2], width: int, height: int, color: Color) -> ImageTexture:
	var img := Image.create(width, height, false, Image.FORMAT_RGBA8)
	img.fill(BG_COLOR)

	var mid_y := height / 2.0

	for x in range(mini(width, peaks.size())):
		var min_val: float = peaks[x].x
		var max_val: float = peaks[x].y

		var y_top := int(mid_y - max_val * mid_y)
		var y_bottom := int(mid_y - min_val * mid_y)
		y_top = clampi(y_top, 0, height - 1)
		y_bottom = clampi(maxi(y_bottom, y_top + 1), 0, height - 1)

		for y in range(y_top, y_bottom + 1):
			img.set_pixel(x, y, color)

	return ImageTexture.create_from_image(img)

static func _read_sample(raw: PackedByteArray, frame: int, channels: int, bytes_per_sample: int, is_16_bit: bool) -> float:
	var byte_offset := frame * channels * bytes_per_sample
	if is_16_bit:
		var raw_value := raw.decode_s16(byte_offset)
		return raw_value / 32768.0
	else:
		var raw_value := raw[byte_offset]
		return (raw_value - 128) / 128.0
