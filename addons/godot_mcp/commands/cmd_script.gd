@tool
extends RefCounted

# Sorun 7 fix (genişletildi): Godot 4.7'de RefCounted + @tool sınıfında
# EditorInterface TİPLİ üye değişken de bozuk bytecode üretiyor
# ("Internal script error! Opcode: 28"). Üye ve parametre TİPSİZ.
var _ei

func _init(ei) -> void:
	_ei = ei

func read_script(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	if path.is_empty():
		return {"success": false, "error": "path gerekli."}
	if not FileAccess.file_exists(path):
		return {"success": false, "error": "Dosya bulunamadı: " + path}
	
	var file = FileAccess.open(path, FileAccess.READ)
	if not file:
		return {"success": false, "error": "Dosya okunamadı: " + path}
	var content = file.get_as_text()
	file.close()
	
	return {"success": true, "result": {"content": content, "path": path}}

func create_script(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	var content: String = params.get("content", "")
	var language: String = params.get("language", "gd")
	
	if path.is_empty():
		return {"success": false, "error": "path gerekli."}
	
	var file = FileAccess.open(path, FileAccess.WRITE)
	if not file:
		return {"success": false, "error": "Dosya oluşturulamadı: " + path}
	file.store_string(content)
	file.close()
	
	return {"success": true, "result": {"path": path, "message": "Script oluşturuldu."}}

func update_script(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	var content: String = params.get("content", "")
	
	if path.is_empty() or content.is_empty():
		return {"success": false, "error": "path ve content gerekli."}
	
	var file = FileAccess.open(path, FileAccess.WRITE)
	if not file:
		return {"success": false, "error": "Dosya yazılamadı: " + path}
	file.store_string(content)
	file.close()
	
	return {"success": true, "result": {"message": "Script güncellendi."}}

func delete_script(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	if path.is_empty():
		return {"success": false, "error": "path gerekli."}
	
	var dir = DirAccess.open(path.get_base_dir())
	if not dir:
		return {"success": false, "error": "Dizin bulunamadı."}
	dir.remove(path.get_file())
	
	return {"success": true, "result": {"message": "Script silindi."}}

func attach_to_node(params: Dictionary) -> Dictionary:
	var node_path: String = params.get("node_path", "")
	var script_path: String = params.get("script_path", "")
	
	if node_path.is_empty() or script_path.is_empty():
		return {"success": false, "error": "node_path ve script_path gerekli."}
	
	var node = _ei.get_edited_scene_root().get_node_or_null(NodePath(node_path))
	if not node:
		return {"success": false, "error": "Node bulunamadı: " + node_path}
	
	var script = load(script_path)
	if not script:
		return {"success": false, "error": "Script yüklenemedi: " + script_path}
	
	node.set_script(script)
	return {"success": true, "result": {"message": "Script node'a bağlandı."}}

func get_errors(params: Dictionary) -> Dictionary:
	# Sorun 13 fix: ScriptEditor API'si sadece AKTİF script'in uyarılarını verir.
	# path verilmişse ve aktif script'le eşleşmiyorsa sessizce yoksaymak yerine
	# açık hata döndür.
	var path: String = params.get("path", "")
	var script_editor = _ei.get_script_editor()
	if not script_editor:
		return {"success": false, "error": "Script editor bulunamadı."}

	if not path.is_empty():
		var current = script_editor.get_current_script()
		var current_path: String = current.resource_path if current else ""
		if current_path != path:
			return {"success": false, "error": "Sadece aktif script destekleniyor (aktif: '%s'). Script'i editor'de açın veya path parametresini boş bırakın." % current_path}

	if not script_editor.has_method("get_warnings"):
		return {"success": false, "error": "Bu Godot sürümünde ScriptEditor.get_warnings() API'si bulunmuyor."}

	var errors = script_editor.call("get_warnings")
	return {"success": true, "result": {"errors": errors}}
