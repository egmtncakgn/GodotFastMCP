@tool
extends Node
class_name LogCollector

const MAX_LOGS = 500

var _logs: Array[Dictionary] = []

func collect(message: String, level: String = "info") -> void:
	_logs.append({
		"timestamp": Time.get_unix_time_from_system(),
		"level": level,
		"message": message,
	})
	if _logs.size() > MAX_LOGS:
		_logs.pop_front()

func get_logs(params: Dictionary) -> Dictionary:
	var count: int = params.get("count", 50)
	var level: String = params.get("level", "")
	
	var filtered = _logs
	if not level.is_empty():
		filtered = _logs.filter(func(l): return l["level"] == level)
	
	var result = filtered.slice(max(0, filtered.size() - count), filtered.size())
	return {"success": true, "result": {"logs": result, "total": _logs.size()}}

func get_errors(params: Dictionary) -> Dictionary:
	var errors = _logs.filter(func(l): return l["level"] in ["error", "warning"])
	return {"success": true, "result": {"errors": errors}}

func clear() -> void:
	_logs.clear()
