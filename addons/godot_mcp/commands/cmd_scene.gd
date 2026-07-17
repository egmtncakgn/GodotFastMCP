@tool
extends RefCounted

var _ei: EditorInterface

func _init(ei: EditorInterface) -> void:
	_ei = ei

func open_scene(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	if path.is_empty():
		return {"success": false, "error": "path parametresi gerekli."}
	if not FileAccess.file_exists(path):
		return {"success": false, "error": "Dosya bulunamadı: " + path}
	
	_ei.open_scene_from_path(path)
	return {"success": true, "result": {"path": path, "message": "Sahne açıldı."}}

func save_scene(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	var scene = _ei.get_edited_scene_root()
	if not scene:
		return {"success": false, "error": "Açık sahne yok."}
	_ei.save_scene()
	return {"success": true, "result": {"message": "Sahne kaydedildi."}}

func create_scene(params: Dictionary) -> Dictionary:
	var path: String = params.get("path", "")
	var root_type: String = params.get("root_node_type", "Node2D")
	if path.is_empty():
		return {"success": false, "error": "path gerekli."}
	
	var packed = PackedScene.new()
	var root = ClassDB.instantiate(root_type)
	if not root:
		return {"success": false, "error": "Geçersiz node tipi: " + root_type}
	root.name = path.get_file().get_basename()
	packed.pack(root)
	
	var err = ResourceSaver.save(packed, path)
	if err != OK:
		return {"success": false, "error": "Sahne kaydedilemedi. Hata kodu: " + str(err)}
	
	_ei.open_scene_from_path(path)
	return {"success": true, "result": {"path": path}}

func list_opened(params: Dictionary) -> Dictionary:
	var scenes = []
	for i in _ei.get_open_scenes().size():
		scenes.append(_ei.get_open_scenes()[i])
	return {"success": true, "result": {"scenes": scenes}}

func get_data(params: Dictionary) -> Dictionary:
	var scene = _ei.get_edited_scene_root()
	if not scene:
		return {"success": false, "error": "Açık sahne yok."}
	return {"success": true, "result": _node_to_dict(scene)}

func _node_to_dict(node: Node, depth: int = 0) -> Dictionary:
	if depth > 10:
		return {}
	var data = {
		"name": node.name,
		"type": node.get_class(),
		"path": str(node.get_path()),
		"children": [],
	}
	for child in node.get_children():
		data["children"].append(_node_to_dict(child, depth + 1))
	return data

func close_scene(params: Dictionary) -> Dictionary:
	_ei.close_scene()
	return {"success": true, "result": {"message": "Sahne kapatıldı."}}
