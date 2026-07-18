@tool
extends Node

class_name McpServer

# Sorun 22 fix: aralık 46300-46599'a genişletildi (300 port).
# C# tarafı PortCoordinator.cs ile AYNI aralıkta tutulmalı.
const PORT_RANGE_START = 46300
const PORT_RANGE_END = 46599
const BIND_ADDRESS = "127.0.0.1"
const MAX_PEERS = 16

var _tcp_server: TCPServer
var _peers: Array[WebSocketPeer] = []  # Çoklu bağlantı desteği
var _dispatcher: CommandDispatcher
var _port: int
var _bound_port: int = -1

var editor_interface: EditorInterface
var log_collector: LogCollector

func _init(ei: EditorInterface, lc: LogCollector) -> void:
	editor_interface = ei
	log_collector = lc

func start() -> void:
	_dispatcher = CommandDispatcher.new(editor_interface, log_collector)
	add_child(_dispatcher)
	_dispatcher.register()

	# 1) Manuel env override (GODOT_MCP_PORT)
	var start_port := PORT_RANGE_START
	if OS.has_environment("GODOT_MCP_PORT"):
		start_port = int(OS.get_environment("GODOT_MCP_PORT"))

	# 2) Server'ın daha önce seçtiği portu lock dosyasından oku (sıcak yeniden başlatmada stabilite)
	var cached := _read_port_lock()
	if cached > 0:
		start_port = cached

	# 3) Boş portu bul (önce cached, sonra tüm aralık)
	var candidates := PackedInt32Array()
	if _is_port_free(start_port):
		candidates.append(start_port)
	for p in range(PORT_RANGE_START, PORT_RANGE_END + 1):
		if p != start_port and _is_port_free(p):
			candidates.append(p)

	for candidate in candidates:
		_tcp_server = TCPServer.new()
		if _tcp_server.listen(candidate, BIND_ADDRESS) == OK:
			_bound_port = candidate
			# Sorun 25 fix: cached port değiştiyse lock dosyaları yeni portla güncellenir
			_write_port_lock(_bound_port)
			push_warning("[GodotMCP] WebSocket sunucusu 127.0.0.1:%d üzerinde başlatıldı. (%d aktif bağlantı destekleniyor)" % [_bound_port, MAX_PEERS])
			return
		else:
			_tcp_server = null

	push_error("[GodotMCP] Hiçbir port (%d-%d) dinlenemiyor!" % [PORT_RANGE_START, PORT_RANGE_END])

func _is_port_free(port: int) -> bool:
	# Godot'da gerçek port probe'u yok; TCPServer.listen ile deneyip geri alıyoruz.
	var s := TCPServer.new()
	var ok := s.listen(port, BIND_ADDRESS) == OK
	if ok:
		s.stop()
	return ok

func _port_lock_path() -> String:
	return OS.get_user_data_dir().path_join("GodotMCP/port.txt")

# Sorun 8 fix: C# server %LOCALAPPDATA%\GodotMCP\port.txt okuyor ama addon
# proje-özel user_data dizinine yazıyordu → iki taraf asla aynı dosyayı görmüyordu.
# Artık addon HER İKİ konuma da yazar:
#   1) proje-özel lock (kendi cache'i, geriye dönük uyumluluk)
#   2) paylaşılan global lock (C# server'ın okuduğu yer, JSON: port+project_path)
func _shared_lock_path() -> String:
	# .NET SpecialFolder.LocalApplicationData karşılığı:
	#   Windows: %LOCALAPPDATA%  |  Linux/macOS: ~/.local/share
	if OS.has_environment("LOCALAPPDATA"):
		return OS.get_environment("LOCALAPPDATA").path_join("GodotMCP/port.txt")
	return OS.get_environment("HOME").path_join(".local/share/GodotMCP/port.txt")

func _write_port_lock(port: int) -> void:
	# 1) Proje-özel lock (düz int — addon'un kendi sıcak-restart cache'i)
	var path := _port_lock_path()
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	var f1 := FileAccess.open(path, FileAccess.WRITE)
	if f1:
		f1.store_string(str(port))
		f1.close()

	# 2) Paylaşılan global lock (JSON — C# tarafı port + proje yolunu okur)
	var shared := _shared_lock_path()
	DirAccess.make_dir_recursive_absolute(shared.get_base_dir())
	var f2 := FileAccess.open(shared, FileAccess.WRITE)
	if f2:
		f2.store_string(JSON.stringify({
			"port": port,
			"project_path": ProjectSettings.globalize_path("res://"),
			"time": Time.get_unix_time_from_system()
		}))
		f2.close()

func _read_port_lock() -> int:
	var path := _port_lock_path()
	if FileAccess.file_exists(path):
		var f := FileAccess.open(path, FileAccess.READ)
		var txt := f.get_as_text().strip_edges()
		if txt.is_valid_int():
			return txt.to_int()
	return 0

func _process(_delta: float) -> void:
	# Yeni bağlantıları kabul et (MAX_PEERS ile sınırlı — sızıntı/DoS önlemi)
	if _tcp_server and _tcp_server.is_connection_available():
		var stream = _tcp_server.take_connection()
		if _peers.size() >= MAX_PEERS:
			push_error("[GodotMCP] Maksimum bağlantı sayısına (%d) ulaşıldı, bağlantı reddedildi." % MAX_PEERS)
			stream.disconnect_from_host()
		else:
			var peer = WebSocketPeer.new()
			peer.accept_stream(stream)
			_peers.append(peer)
			push_warning("[GodotMCP] Yeni bağlantı kabul edildi. Toplam: %d" % _peers.size())

	# Tüm peer'ları poll et
	for i in range(_peers.size() - 1, -1, -1):
		var peer = _peers[i]
		peer.poll()

		var state = peer.get_ready_state()
		if state == WebSocketPeer.STATE_OPEN:
			while peer.get_available_packet_count() > 0:
				var data = peer.get_packet().get_string_from_utf8()
				_handle_message(data, peer)
		elif state in [WebSocketPeer.STATE_CLOSING, WebSocketPeer.STATE_CLOSED]:
			_peers.remove_at(i)
			push_warning("[GodotMCP] Bağlantı kapandı. Kalan: %d" % _peers.size())

func _handle_message(json_text: String, peer: WebSocketPeer) -> void:
	var parsed = JSON.parse_string(json_text)
	if not parsed:
		push_error("[GodotMCP] JSON ayrıştırma hatası: " + json_text)
		return

	var request_id: String = parsed.get("id", "")
	var command: String = parsed.get("command", "")
	var params_raw = parsed.get("params", {})
	var params: Dictionary = params_raw if params_raw is Dictionary else {}

	# Sorun 31 fix: C# tarafı gönderilmeyen opsiyonel parametreleri açıkça
	# JSON null olarak iletiyor ("level": null gibi). Dictionary.get(key, default)
	# anahtar VAR ama değeri null ise default'u DEĞİL null döndürür → handler'lardaki
	# "var x: String = params.get(...)" tipli atamalar "Nil -> String" hatasıyla çöküyor
	# ve coroutine sessizce {} döndürüyordu. null değerli anahtarları "hiç gönderilmemiş"
	# sayarak sil; default'lar devreye girsin. (params.get("value", null) gibi null
	# default'lu kullanımlar etkilenmez — sonuç yine null olur.)
	var null_keys: Array = []
	for k in params.keys():
		if params[k] == null:
			null_keys.append(k)
	for k in null_keys:
		params.erase(k)

	var result = await _dispatcher.dispatch(command, params)

	var response = {
		"id": request_id,
		"success": result.get("success", false),
		"result": result.get("result", null),
		"error": result.get("error", null)
	}

	if peer and peer.get_ready_state() == WebSocketPeer.STATE_OPEN:
		peer.send_text(JSON.stringify(response))

func stop() -> void:
	for peer in _peers:
		if peer.get_ready_state() == WebSocketPeer.STATE_OPEN:
			peer.close()
	_peers.clear()
	if _tcp_server:
		_tcp_server.stop()
	push_warning("[GodotMCP] Sunucu durduruldu (port %d)." % _bound_port)
