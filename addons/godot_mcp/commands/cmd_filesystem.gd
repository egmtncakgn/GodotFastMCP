@tool
extends RefCounted

# Sorun 7 fix (genişletildi): Godot 4.7'de RefCounted + @tool sınıfında
# EditorInterface TİPLİ üye değişken de bozuk bytecode üretiyor
# ("Internal script error! Opcode: 28"). Üye ve parametre TİPSİZ.
var _ei

func _init(ei) -> void:
	_ei = ei

func list_files(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "res://")
	var recursive: bool = params.get("recursive", false)

	var dir = DirAccess.open(path)
	if not dir:
		return {"success": false, "error": "Dizin açılamadı: " + path}

	var files = []
	_collect_files(dir, path, recursive, files, {})
	return {"success": true, "result": {"files": files}}

# Sorun 16 fix: Windows junction / symlink döngülerine karşı koruma.
# - dir.is_link(name) ile link'ler recursion'a GİRMEZ (type: "link" olarak listelenir)
# - visited sözlüğü ile aynı dizin yolu iki kez taranmaz
# - MAX_ENTRIES üst sınırı ikinci güvenlik ağı
const MAX_ENTRIES := 100000

func _collect_files(dir: DirAccess, base: String, recursive: bool, out: Array, visited: Dictionary) -> void:
	if out.size() >= MAX_ENTRIES:
		return
	dir.list_dir_begin()
	var file_name = dir.get_next()
	while file_name != "":
		if out.size() >= MAX_ENTRIES:
			break
		if file_name == "." or file_name == "..":
			file_name = dir.get_next()
			continue
		var full_path = base + "/" + file_name
		if dir.current_is_dir():
			# Link ise recursion'a girme → junction/symlink döngüsü imkânsız
			# NOT: is_link() static değil, DirAccess INSTANCE metodudur (Godot 4.7).
			if dir.is_link(file_name):
				out.append({"path": full_path, "type": "link"})
				file_name = dir.get_next()
				continue
			out.append({"path": full_path, "type": "dir"})
			if recursive:
				if visited.has(full_path):
					file_name = dir.get_next()
					continue
				visited[full_path] = true
				var sub = DirAccess.open(full_path)
				if sub:
					_collect_files(sub, full_path, recursive, out, visited)
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
	# Sorun 17 fix: uzantı filtresi case-insensitive karşılaştırılır.
	var filter_lower := type_filter.to_lower()
	_search_impl(dir, base, pattern, filter_lower, out, {})

func _search_impl(dir: DirAccess, base: String, pattern: String, filter_lower: String, out: Array, visited: Dictionary) -> void:
	dir.list_dir_begin()
	var name = dir.get_next()
	while name != "":
		if name == "." or name == "..":
			name = dir.get_next()
			continue
		var full = base + "/" + name
		if dir.current_is_dir():
			# Sorun 16: link'lere girme + visited ile döngü koruması
			# NOT: is_link() static değil, DirAccess INSTANCE metodudur (Godot 4.7).
			if dir.is_link(name) or visited.has(full):
				name = dir.get_next()
				continue
			visited[full] = true
			var sub = DirAccess.open(full)
			if sub:
				_search_impl(sub, full, pattern, filter_lower, out, visited)
		else:
			if name.matchn(pattern) or full.matchn(pattern):
				if filter_lower.is_empty() or full.to_lower().ends_with(filter_lower):
					out.append(full)
		name = dir.get_next()
	dir.list_dir_end()
