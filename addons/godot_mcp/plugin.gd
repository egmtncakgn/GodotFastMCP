@tool
extends EditorPlugin

var mcp_server: McpServer
var log_collector: LogCollector

func _enter_tree() -> void:
	log_collector = LogCollector.new()
	add_child(log_collector)
	
	mcp_server = McpServer.new(get_editor_interface(), log_collector)
	add_child(mcp_server)

	# Port: env değişkeni > varsayılan 6505. Çakışmada plugin 6506/6507'ye düşer.
	var port = 6505
	if OS.has_environment("GODOT_MCP_PORT"):
		port = int(OS.get_environment("GODOT_MCP_PORT"))
	mcp_server.start(port)

func _exit_tree() -> void:
	if mcp_server:
		mcp_server.stop()
	if log_collector:
		log_collector.queue_free()
