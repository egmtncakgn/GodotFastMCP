@tool
extends RefCounted

var _ei: EditorInterface

func _init(ei: EditorInterface) -> void:
	_ei = ei

func find_node(params: Dictionary) -> Dictionary:
	var name_filter: String = params.get("name", "")
	var type_filter: String = params.get("type", "")
	var path_filter: String = params.get("path", "")
	
	var scene_root = _ei.get_edited_scene_root()
	if not scene_root:
		return {"success": false, "error": "Açık sahne yok."}
	
	var results = []
	_collect_nodes(scene_root, name_filter, type_filter, path_filter, results)
	
	return {"success": true, "result": {"nodes": results}}

func _collect_nodes(node: Node, name_f: String, type_f: String, path_f: String, out: Array) -> void:
	if not _matches(node, name_f, type_f, path_f):
		out.append({"name": node.name, "type": node.get_class(), "path": str(node.get_path())})
	for child in node.get_children():
		_collect_nodes(child, name_f, type_f, path_f, out)

func _matches(node: Node, name_f: String, type_f: String, path_f: String) -> bool:
	if not name_f.is_empty() and node.name != name_f:
		return false
	if not type_f.is_empty() and node.get_class() != type_f:
		return false
	if not path_f.is_empty() and str(node.get_path()) != path_f:
		return false
	return true

func create_node(params: Dictionary) -> Dictionary:
	var type: String = params.get("type", "")
	var name: String = params.get("name", "")
	var parent_path: String = params.get("parent_path", "")
	
	if type.is_empty() or name.is_empty():
		return {"success": false, "error": "type ve name parametreleri gerekli."}
	
	var parent: Node
	if parent_path.is_empty():
		parent = _ei.get_edited_scene_root()
	else:
		parent = _ei.get_edited_scene_root().get_node_or_null(NodePath(parent_path))
	
	if not parent:
		return {"success": false, "error": "Üst node bulunamadı: " + parent_path}
	
	var node = ClassDB.instantiate(type)
	if not node:
		return {"success": false, "error": "Geçersiz node tipi: " + type}
	node.name = name
	parent.add_child(node)
	node.set_owner(_ei.get_edited_scene_root())
	
	return {"success": true, "result": {"path": str(node.get_path()), "name": node.name}}

func get_properties(params: Dictionary) -> Dictionary:
	var node_path: String = params.get("node_path", "")
	if node_path.is_empty():
		return {"success": false, "error": "node_path gerekli."}
	
	var node = _ei.get_edited_scene_root().get_node_or_null(NodePath(node_path))
	if not node:
		return {"success": false, "error": "Node bulunamadı: " + node_path}
	
	var props = []
	for i in node.get_property_list().size():
		var p = node.get_property_list()[i]
		if p["usage"] & PROPERTY_USAGE_EDITOR:
			props.append({"name": p["name"], "value": node.get(p["name"])})
	
	return {"success": true, "result": {"properties": props}}

func set_property(params: Dictionary) -> Dictionary:
	var node_path: String = params.get("node_path", "")
	var property: String = params.get("property", "")
	var value = params.get("value", null)
	
	if node_path.is_empty() or property.is_empty():
		return {"success": false, "error": "node_path ve property gerekli."}
	
	var node = _ei.get_edited_scene_root().get_node_or_null(NodePath(node_path))
	if not node:
		return {"success": false, "error": "Node bulunamadı: " + node_path}
	
	node.set(property, value)
	return {"success": true, "result": {"message": "Property ayarlandı: " + property}}

func set_properties(params: Dictionary) -> Dictionary:
	var node_path: String = params.get("node_path", "")
	var properties: Dictionary = params.get("properties", {})
	
	if node_path.is_empty():
		return {"success": false, "error": "node_path gerekli."}
	
	var node = _ei.get_edited_scene_root().get_node_or_null(NodePath(node_path))
	if not node:
		return {"success": false, "error": "Node bulunamadı: " + node_path}
	
	for key in properties:
		node.set(key, properties[key])
	
	return {"success": true, "result": {"message": "Property'ler ayarlandı."}}

func delete_node(params: Dictionary) -> Dictionary:
	var node_path: String = params.get("node_path", "")
	if node_path.is_empty():
		return {"success": false, "error": "node_path gerekli."}
	
	var node = _ei.get_edited_scene_root().get_node_or_null(NodePath(node_path))
	if not node:
		return {"success": false, "error": "Node bulunamadı: " + node_path}
	
	node.queue_free()
	return {"success": true, "result": {"message": "Node silindi."}}

func reparent_node(params: Dictionary) -> Dictionary:
	var node_path: String = params.get("node_path", "")
	var new_parent_path: String = params.get("new_parent_path", "")
	
	if node_path.is_empty() or new_parent_path.is_empty():
		return {"success": false, "error": "node_path ve new_parent_path gerekli."}
	
	var node = _ei.get_edited_scene_root().get_node_or_null(NodePath(node_path))
	var new_parent = _ei.get_edited_scene_root().get_node_or_null(NodePath(new_parent_path))
	
	if not node or not new_parent:
		return {"success": false, "error": "Node veya yeni üst bulunamadı."}
	
	var old_parent = node.get_parent()
	old_parent.remove_child(node)
	new_parent.add_child(node)
	node.set_owner(_ei.get_edited_scene_root())
	
	return {"success": true, "result": {"message": "Node taşındı."}}

func duplicate_node(params: Dictionary) -> Dictionary:
	var node_path: String = params.get("node_path", "")
	var new_name: String = params.get("new_name", "")
	
	if node_path.is_empty():
		return {"success": false, "error": "node_path gerekli."}
	
	var node = _ei.get_edited_scene_root().get_node_or_null(NodePath(node_path))
	if not node:
		return {"success": false, "error": "Node bulunamadı: " + node_path}
	
	var dup = node.duplicate()
	if not new_name.is_empty():
		dup.name = new_name
	node.get_parent().add_child(dup)
	dup.set_owner(_ei.get_edited_scene_root())
	
	return {"success": true, "result": {"path": str(dup.get_path()), "name": dup.name}}

func rename_node(params: Dictionary) -> Dictionary:
	var node_path: String = params.get("node_path", "")
	var new_name: String = params.get("new_name", "")
	
	if node_path.is_empty() or new_name.is_empty():
		return {"success": false, "error": "node_path ve new_name gerekli."}
	
	var node = _ei.get_edited_scene_root().get_node_or_null(NodePath(node_path))
	if not node:
		return {"success": false, "error": "Node bulunamadı: " + node_path}
	
	node.name = new_name
	return {"success": true, "result": {"message": "Node yeniden adlandırıldı."}}

func instance_scene(params: Dictionary) -> Dictionary:
	var scene_path: String = params.get("scene_path", "")
	var parent_path: String = params.get("parent_path", "")
	var name: String = params.get("name", "")
	
	if scene_path.is_empty():
		return {"success": false, "error": "scene_path gerekli."}
	
	var packed: PackedScene = load(scene_path) as PackedScene
	if not packed:
		return {"success": false, "error": "Sahne yüklenemedi: " + scene_path}
	
	var parent: Node
	if parent_path.is_empty():
		parent = _ei.get_edited_scene_root()
	else:
		parent = _ei.get_edited_scene_root().get_node_or_null(NodePath(parent_path))
	
	if not parent:
		return {"success": false, "error": "Üst node bulunamadı."}
	
	var instance = packed.instantiate()
	if not name.is_empty():
		instance.name = name
	parent.add_child(instance)
	instance.set_owner(_ei.get_edited_scene_root())
	
	return {"success": true, "result": {"path": str(instance.get_path())}}
