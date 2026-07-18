@tool
extends RefCounted
class_name CmdUpdate

# Sorun 7 fix (genişletildi): Godot 4.7'de RefCounted + @tool sınıfında
# EditorInterface TİPLİ üye değişken de bozuk bytecode üretiyor
# ("Internal script error! Opcode: 28"). Üye ve parametre TİPSİZ.
var _ei

func _init(ei) -> void:
	_ei = ei

# C#'tan gelen dosya içeriklerini diske yazar ve reimport eder.
# params: { "files": [ { "path": "rel/path", "content": "..." } ], "base_path": "res://addons/godot_mcp" }
func update_addon_push(params: Dictionary) -> Dictionary:
	var files = params.get("files", [])
	var base_path: String = params.get("base_path", "res://addons/godot_mcp")

	if not files is Array or files.size() == 0:
		return {"success": false, "error": "Gönderilecek dosya listesi boş."}

	var written: int = 0
	var failed: Array[String] = []
	var plugin_changed: bool = false
	var restart_required: bool = false

	for entry in files:
		if not (entry is Dictionary):
			continue
		var rel: String = entry.get("path", "")
		var content: String = entry.get("content", "")
		if rel.is_empty():
			continue
		# .uid dosyalarini yazma (Godot otomatik uretir)
		if rel.ends_with(".uid"):
			continue
		var full: String = base_path.path_join(rel)
		var dir := base_path.path_join(rel.get_base_dir())
		var err := DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(dir))
		if err != OK:
			failed.append(rel)
			continue

		# Önceki içerikle karşılaştır: sadece değişenleri yaz (gereksiz reimport önle)
		var changed: bool = true
		if FileAccess.file_exists(ProjectSettings.globalize_path(full)):
			var old := FileAccess.get_file_as_string(ProjectSettings.globalize_path(full))
			changed = old != content

		if not changed:
			continue

		# plugin.gd / plugin.cfg değişirse Godot yeniden açılmalı
		if rel in ["plugin.gd", "plugin.cfg"]:
			plugin_changed = true
			restart_required = true

		var f := FileAccess.open(ProjectSettings.globalize_path(full), FileAccess.WRITE)
		if f == null:
			failed.append(rel)
			continue
		f.store_string(content)
		f.close()
		written += 1

	# Yazılan dosyaları Godot'a tanıt (reimport)
	EditorInterface.get_resource_filesystem().scan()

	# İlk kurulumda eklentiyi otomatik etkinleştir (kullanıcı etkileşimi sıfır)
	if not _is_plugin_enabled():
		_enable_plugin()

	if failed.size() > 0:
		return {"success": false, "error": "Bazı dosyalar yazılamadı: %s" % str(failed), "result": {"written": written}}

	var msg := "Addon güncellendi (%d dosya), reimport tamamlandı." % written
	if restart_required:
		msg += " plugin.gd/plugin.cfg değişti → Godot'u yeniden açın (eklentiyi yeniden etkinleştirin)."

	return {"success": true, "result": {"written": written, "restart_required": restart_required, "message": msg}}


# Yerel bir kaynaktan addon'u günceller (örn. GODOT_PROJECT_PATH dışında bir klasör).
# params: { "source": "res://...", "base_path": "res://addons/godot_mcp" }
func update_addon(params: Dictionary) -> Dictionary:
	var source: String = params.get("source", "")
	var base_path: String = params.get("base_path", "res://addons/godot_mcp")
	if source.is_empty():
		return {"success": false, "error": "source parametresi gerekli (res:// ile başlamalı)."}

	if not DirAccess.dir_exists_absolute(source):
		return {"success": false, "error": "Kaynak klasör bulunamadı: " + source}

	# BUG FIX: count eskiden değerle geçiyordu, recursive çağrılarda kayboluyordu.
	# Artık _copy_dir yazılan dosya sayısını döndürüyor.
	var count: int = _copy_dir(source, base_path)
	EditorInterface.get_resource_filesystem().scan()
	return {"success": true, "result": {"written": count, "message": "Addon yerel kaynaktan güncellendi."}}


func _copy_dir(src: String, dst: String) -> int:
	var count: int = 0
	var da := DirAccess.open(src)
	if da == null:
		return 0
	da.list_dir_begin()
	var name := da.get_next()
	while name != "":
		if da.current_is_dir():
			if name != "." and name != "..":
				count += _copy_dir(src.path_join(name), dst.path_join(name))
		else:
			var content := FileAccess.get_file_as_string(src.path_join(name))
			var target := dst.path_join(name)
			DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(dst.path_join(name).get_base_dir()))
			var f := FileAccess.open(ProjectSettings.globalize_path(target), FileAccess.WRITE)
			if f != null:
				f.store_string(content)
				f.close()
				count += 1
		name = da.get_next()
	da.list_dir_end()
	return count


# Sorun 12 fix: Godot 4'te editor_plugins/enabled dizisi plugin ADI değil
# plugin.cfg YOLU saklar ("res://addons/godot_mcp/plugin.cfg").
# Eski kod "GodotMCP" adını kontrol edip ekliyordu → her zaman false dönüyor,
# diziye geçersiz "GodotMCP" değeri ekleniyordu.
const PLUGIN_CONFIG_PATH := "res://addons/godot_mcp/plugin.cfg"


# Godot 4'te eklentinin etkin olup olmadığını kontrol et (editor_plugins/enabled ayarı)
func _is_plugin_enabled() -> bool:
	var enabled: Array = ProjectSettings.get_setting("editor_plugins/enabled", [])
	return enabled.has(PLUGIN_CONFIG_PATH)


# Eklentiyi otomatik etkinleştir (ilk kurulumda manuel adımı ortadan kaldırır)
func _enable_plugin() -> void:
	var enabled: Array = ProjectSettings.get_setting("editor_plugins/enabled", [])
	if not enabled.has(PLUGIN_CONFIG_PATH):
		enabled.append(PLUGIN_CONFIG_PATH)
	ProjectSettings.set_setting("editor_plugins/enabled", enabled)
	ProjectSettings.save()
