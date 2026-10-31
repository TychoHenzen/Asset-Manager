@tool
class_name PeakColumns
extends RefCounted

## Folds decoded audio frames into one min/max pair per column, a chunk at a
## time, so a stream never has to be held in memory whole. Touches no server,
## so it runs the same on the main thread (the preview, yielding between
## chunks) and on a worker (thumbnails, all at once).

const TARGET_SAMPLES_PER_BUCKET: int = 200

var _width: int
var _frames_per_column: float
var _stride: int
var _peaks: Array[Vector2] = []
var _current_column: int = 0
var _column_end_frame: int
var _min_val: float = 1.0
var _max_val: float = -1.0
var _frames_seen: int = 0

func _init(width: int, estimated_total_frames: int) -> void:
	_width = width
	_frames_per_column = maxf(1.0, float(maxi(1, estimated_total_frames)) / float(width))
	_stride = maxi(1, int(_frames_per_column / TARGET_SAMPLES_PER_BUCKET))
	_column_end_frame = int(_frames_per_column)

func frames_seen() -> int:
	return _frames_seen

func add(chunk: PackedVector2Array) -> void:
	var i := 0
	while i < chunk.size():
		while _frames_seen + i >= _column_end_frame and _current_column < _width:
			_peaks.append(Vector2(_min_val, _max_val))
			_current_column += 1
			_column_end_frame = int((_current_column + 1) * _frames_per_column)
			_min_val = 1.0
			_max_val = -1.0

		var sample: float = chunk[i].x
		_min_val = minf(_min_val, sample)
		_max_val = maxf(_max_val, sample)
		i += _stride

	_frames_seen += chunk.size()

## Closes the last column and pads to the full width.
func finish() -> Array[Vector2]:
	if _current_column < _width and _min_val <= _max_val:
		_peaks.append(Vector2(_min_val, _max_val))
		_current_column += 1
	while _current_column < _width:
		_peaks.append(Vector2(0.0, 0.0))
		_current_column += 1
	return _peaks
