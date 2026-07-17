@tool
extends Node
class_name LogCollector

# Godot 4.5+ custom logger: editor genelindeki TÜM logları yakalar
# (print, push_error, push_warning, script parse/runtime hataları, engine hataları).
# OS.add_logger() ile kaydedilir; callback'ler farklı thread'lerden çağrılabilir
# bu yüzden _mutex ile koruyoruz.

const MAX_LOGS = 500

var _logs: Array[Dictionary] = []
var _mutex := Mutex.new()

# Logger alt sınıfı: OS.add_logger bunu kabul eder.
class GodotMCPLogger extends Logger:
	var _collector: LogCollector

	func _init(collector: LogCollector) -> void:
		_collector = collector

	func _log_message(message: String, error: bool) -> void:
		_collector.collect(message, "error" if error else "info")

	func _log_error(_function: String, _file: String, _line: int, _code: String,
					_rationale: String, _editor_notify: bool, error_type: int, _script_backtraces: Array) -> void:
		var level := "error"
		if error_type == Logger.ERROR_TYPE_WARNING:
			level = "warning"
		_collector.collect(_rationale if _rationale != "" else _code, level)

func _ready() -> void:
	if OS.has_method("add_logger"):
		var l := GodotMCPLogger.new(self)
		OS.add_logger(l)

func collect(message: String, level: String = "info") -> void:
	_mutex.lock()
	_logs.append({
		"timestamp": Time.get_unix_time_from_system(),
		"level": level,
		"message": message,
	})
	if _logs.size() > MAX_LOGS:
		_logs.pop_front()
	_mutex.unlock()

func get_logs(params: Dictionary) -> Dictionary:
	_mutex.lock()
	var count: int = params.get("count", 50)
	var level: String = params.get("level", "")
	var filtered = _logs
	if not level.is_empty():
		filtered = _logs.filter(func(l): return l["level"] == level)
	var result = filtered.slice(max(0, filtered.size() - count), filtered.size())
	var total = _logs.size()
	_mutex.unlock()
	return {"success": true, "result": {"logs": result, "total": total}}

func get_errors(params: Dictionary) -> Dictionary:
	_mutex.lock()
	var errors = _logs.filter(func(l): return l["level"] in ["error", "warning"])
	_mutex.unlock()
	return {"success": true, "result": {"errors": errors}}

func clear() -> void:
	_mutex.lock()
	_logs.clear()
	_mutex.unlock()
