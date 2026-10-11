@tool
class_name ImportProgressDialog
extends AcceptDialog

## Progress for a scan, which runs in the background. Not modal: the editor
## stays usable while a large library is walked, and Hide only hides this, the
## scan carries on. Stop cancels it; packs already walked stay indexed and the
## next scan picks up the rest.
## Two tiers: the top row is which stage we're in, the bottom is progress
## within it. The bars run indeterminate while the top folders are listed,
## then count packs.

signal stop_requested

## Ordered, so the top bar can show real overall progress rather than just
## naming a stage.
const STAGES: Array[Dictionary] = [
	{"id": "scan", "label": "Scanning packs", "weight": 1.0},
	{"id": "retry", "label": "Retrying unreadable folders", "weight": 0.05},
	{"id": "duplicates", "label": "Finding exact copies", "weight": 0.5},
]

const STOP_ACTION: StringName = &"stop"

@onready var _stage_label: Label = $Margin/VBox/StageRow/StageLabel
@onready var _elapsed_label: Label = $Margin/VBox/StageRow/ElapsedLabel
@onready var _stage_bar: ProgressBar = $Margin/VBox/StageBar
@onready var _detail_label: Label = $Margin/VBox/DetailRow/DetailLabel
@onready var _count_label: Label = $Margin/VBox/DetailRow/CountLabel
@onready var _detail_bar: ProgressBar = $Margin/VBox/DetailBar

var _started_ms: int = 0
var _current_stage: String = ""
var _current_type: String = ""
var _ticker: Timer

func _ready() -> void:
	if EditorGuard.is_scene_tab(self):
		return

	# OK only hides the dialog. Stop is a custom action rather than a cancel
	# button, because closing the window also counts as cancel and closing it
	# should not throw away a long walk.
	exclusive = false
	get_ok_button().text = "Hide"
	add_button("Stop", true, STOP_ACTION)
	custom_action.connect(_on_custom_action)

	_ticker = Timer.new()
	_ticker.wait_time = 0.5
	_ticker.timeout.connect(_update_elapsed)
	add_child(_ticker)

func start() -> void:
	_started_ms = Time.get_ticks_msec()
	_current_stage = ""
	_current_type = ""
	_stage_label.text = "Starting..."
	_detail_label.text = ""
	_count_label.text = ""
	_stage_bar.value = 0.0
	_detail_bar.value = 0.0
	_elapsed_label.text = "0:00"
	_ticker.start()
	popup_centered()

func finish() -> void:
	_ticker.stop()
	hide()

func _on_custom_action(action: StringName) -> void:
	if action == STOP_ACTION:
		stop_requested.emit()
		hide()

## Fed from AssetCatalog.ScanProgressed, unknown keys are ignored so the
## reporting side can add fields without breaking this. A total of 0 means
## the stage cannot know how far along it is.
func on_progress(info: Dictionary) -> void:
	var indeterminate := int(info.get("total", 0)) == 0
	_detail_bar.indeterminate = indeterminate
	_stage_bar.indeterminate = indeterminate

	var stage_id: String = info.get("stage", "")
	var type_id: String = info.get("type", "")

	# The detail bar resets per type, so the heading names which one it's on.
	# Otherwise a bar that jumps back to zero looks like something went wrong.
	if stage_id != _current_stage or type_id != _current_type:
		_current_stage = stage_id
		_current_type = type_id
		var heading := _stage_label_for(stage_id)
		if not type_id.is_empty():
			heading += ": " + type_id
		_stage_label.text = heading

	var current: int = int(info.get("current", 0))
	var total: int = int(info.get("total", 0))
	var fraction: float = float(current) / float(total) if total > 0 else 0.0

	# Detail bar fills and resets once per type; the top bar spans everything,
	# falling back to the type's own numbers for stages that report no overall.
	var overall_current: int = int(info.get("overall_current", current))
	var overall_total: int = int(info.get("overall_total", total))
	var overall_fraction: float = float(overall_current) / float(overall_total) if overall_total > 0 else 0.0

	_detail_bar.value = fraction
	_stage_bar.value = _overall_fraction(stage_id, overall_fraction)

	var parts: Array[String] = []
	if not type_id.is_empty():
		parts.append(type_id)

	var label: String = info.get("label", "")
	if not label.is_empty():
		parts.append(label)

	_detail_label.text = "  ·  ".join(parts)

	# Position within this type, then the whole run in brackets. Its own
	# label, pinned right, so a changing filename can't shift it sideways.
	if total > 0:
		var text := "%d / %d" % [current, total]
		if overall_total > 0 and overall_total != total:
			text += " (%d)" % overall_total
		_count_label.text = text
	else:
		_count_label.text = ""

## Weighted so the bar tracks real time rather than jumping to 90% and sitting
## there through the one stage that actually takes a while.
func _overall_fraction(stage_id: String, stage_fraction: float) -> float:
	var total_weight: float = 0.0
	for stage in STAGES:
		total_weight += float(stage["weight"])
	if total_weight <= 0.0:
		return 0.0

	var accumulated: float = 0.0
	for stage in STAGES:
		var weight: float = float(stage["weight"])
		if stage["id"] == stage_id:
			return (accumulated + weight * stage_fraction) / total_weight
		accumulated += weight

	return accumulated / total_weight

func _stage_label_for(stage_id: String) -> String:
	for stage in STAGES:
		if stage["id"] == stage_id:
			return String(stage["label"])
	return stage_id.capitalize()

func _update_elapsed() -> void:
	var seconds: int = (Time.get_ticks_msec() - _started_ms) / 1000
	_elapsed_label.text = "%d:%02d" % [seconds / 60, seconds % 60]
