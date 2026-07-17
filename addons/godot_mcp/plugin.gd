@tool
extends EditorPlugin

var mcp_server: McpServer
var log_collector: LogCollector

func _enter_tree() -> void:
	log_collector = LogCollector.new()
	add_child(log_collector)
	
	mcp_server = McpServer.new(get_editor_interface(), log_collector)
	add_child(mcp_server)
	# Parametresiz: dinamik port aralığı (46300-46400) + lock dosyası.
	mcp_server.start()

func _exit_tree() -> void:
	if mcp_server:
		mcp_server.stop()
	if log_collector:
		log_collector.queue_free()
