@tool
extends RefCounted

var _ei: EditorInterface

func _init(ei: EditorInterface) -> void:
	_ei = ei

func list_files(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "res://")
	var recursive: bool = params.get("recursive", false)
	
	var dir = DirAccess.open(path)
	if not dir:
		return {"success": false, "error": "Dizin açılamadı: " + path}
	
	var files = []
	_collect_files(dir, path, recursive, files)
	return {"success": true, "result": {"files": files}}

func _collect_files(dir: DirAccess, base: String, recursive: bool, out: Array) -> void:
	dir.list_dir_begin()
	var file_name = dir.get_next()
	while file_name != "":
		if file_name == "." or file_name == "..":
			file_name = dir.get_next()
			continue
		var full_path = base + "/" + file_name
		if dir.current_is_dir():
			out.append({"path": full_path, "type": "dir"})
			if recursive:
				var sub = DirAccess.open(full_path)
				if sub:
					_collect_files(sub, full_path, recursive, out)
		else:
			out.append({"path": full_path, "type": "file"})
		file_name = dir.get_next()
	dir.list_dir_end()

func read_file(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	if path.is_empty():
		return {"success": false, "error": "path gerekli."}
	if not FileAccess.file_exists(path):
		return {"success": false, "error": "Dosya bulunamadı: " + path}
	
	var file = FileAccess.open(path, FileAccess.READ)
	if not file:
		return {"success": false, "error": "Dosya okunamadı."}
	var content = file.get_as_text()
	file.close()
	return {"success": true, "result": {"content": content, "path": path}}

func write_file(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	var content: String = params.get("content", "")
	if path.is_empty():
		return {"success": false, "error": "path gerekli."}
	
	var file = FileAccess.open(path, FileAccess.WRITE)
	if not file:
		return {"success": false, "error": "Dosya yazılamadı: " + path}
	file.store_string(content)
	file.close()
	return {"success": true, "result": {"message": "Dosya yazıldı."}}

func delete_file(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	if path.is_empty():
		return {"success": false, "error": "path gerekli."}
	var dir = DirAccess.open(path.get_base_dir())
	if not dir:
		return {"success": false, "error": "Dizin bulunamadı."}
	dir.remove(path.get_file())
	return {"success": true, "result": {"message": "Silindi."}}

func move_file(params: Dictionary) -> Dictionary:
	var from: String = params.get("from", "")
	var to: String = params.get("to", "")
	if from.is_empty() or to.is_empty():
		return {"success": false, "error": "from ve to gerekli."}
	var err = DirAccess.rename_absolute(from, to)
	if err != OK:
		return {"success": false, "error": "Taşıma başarısız. Hata: " + str(err)}
	return {"success": true, "result": {"message": "Taşındı."}}

func reimport(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	_ei.get_resource_filesystem().reimport_files([path] if not path.is_empty() else [])
	return {"success": true, "result": {"message": "Yeniden içe aktarıldı."}}

func search_files(params: Dictionary) -> Dictionary:
	var pattern: String = params.get("pattern", "")
	var path: String = params.get("path", "res://")
	var type: String = params.get("type", "")
	if pattern.is_empty():
		return {"success": false, "error": "pattern gerekli."}
	
	var results = []
	var dir = DirAccess.open(path)
	if not dir:
		return {"success": false, "error": "Dizin bulunamadı: " + path}
	_search(dir, path, pattern, type, results)
	return {"success": true, "result": {"results": results}}

func _search(dir: DirAccess, base: String, pattern: String, type_filter: String, out: Array) -> void:
	dir.list_dir_begin()
	var name = dir.get_next()
	while name != "":
		if name == "." or name == "..":
			name = dir.get_next()
			continue
		var full = base + "/" + name
		if dir.current_is_dir():
			var sub = DirAccess.open(full)
			if sub:
				_search(sub, full, pattern, type_filter, out)
		else:
			if name.matchn(pattern) or full.matchn(pattern):
				if type_filter.is_empty() or full.ends_with(type_filter):
					out.append(full)
		name = dir.get_next()
	dir.list_dir_end()
