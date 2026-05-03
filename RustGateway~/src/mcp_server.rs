use std::{
    io::{self, BufRead, Write},
    time::Duration,
};

use anyhow::{bail, Context};
use serde::Serialize;
use serde_json::{json, Value};

use crate::{
    read_unity_bridge_discovery, resolve_project_root, send_unity_tcp_line_with_timeout, McpArgs,
};

const SERVER_NAME: &str = "linalab-unity-ai-bridge";
const SERVER_VERSION: &str = "0.1.0";
const PROTOCOL_VERSION: &str = "2024-11-05";

#[derive(Serialize)]
struct JsonRpcResponse {
    jsonrpc: &'static str,
    id: Value,
    #[serde(skip_serializing_if = "Option::is_none")]
    result: Option<Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<JsonRpcError>,
}

#[derive(Serialize)]
struct JsonRpcError {
    code: i64,
    message: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    data: Option<Value>,
}

pub(crate) fn run_mcp_server(args: McpArgs) -> anyhow::Result<()> {
    eprintln!("Lux MCP server starting on stdio");
    let stdin = io::stdin();
    let mut stdout = io::stdout();

    for line in stdin.lock().lines() {
        let line = line.context("failed to read MCP request from stdin")?;
        let trimmed = line.trim();
        if trimmed.is_empty() {
            continue;
        }

        let response = match serde_json::from_str::<Value>(trimmed) {
            Ok(request) => handle_request(&args, request),
            Err(error) => Some(error_response(
                Value::Null,
                -32700,
                format!("Parse error: {error}"),
                None,
            )),
        };

        if let Some(response) = response {
            writeln!(stdout, "{}", serde_json::to_string(&response)?)
                .context("failed to write MCP response to stdout")?;
            stdout.flush().context("failed to flush MCP stdout")?;
        }
    }

    Ok(())
}

fn handle_request(args: &McpArgs, request: Value) -> Option<JsonRpcResponse> {
    let id = request.get("id").cloned();
    let Some(response_id) = id else {
        return None;
    };

    let method = match request.get("method").and_then(Value::as_str) {
        Some(method) => method,
        None => {
            return Some(error_response(
                response_id,
                -32600,
                "Invalid Request: method is required".to_string(),
                None,
            ))
        }
    };

    let result = match method {
        "initialize" => Ok(initialize_result()),
        "tools/list" => Ok(json!({ "tools": tool_definitions() })),
        "tools/call" => {
            handle_tools_call(args, request.get("params").cloned().unwrap_or(Value::Null))
        }
        _ => Err(mcp_error(
            -32601,
            format!("Method not found: {method}"),
            None,
        )),
    };

    Some(match result {
        Ok(result) => JsonRpcResponse {
            jsonrpc: "2.0",
            id: response_id,
            result: Some(result),
            error: None,
        },
        Err(error) => error_response(response_id, error.code, error.message, error.data),
    })
}

fn initialize_result() -> Value {
    json!({
        "protocolVersion": PROTOCOL_VERSION,
        "capabilities": { "tools": {} },
        "serverInfo": {
            "name": SERVER_NAME,
            "version": SERVER_VERSION,
        },
    })
}

fn handle_tools_call(args: &McpArgs, params: Value) -> Result<Value, McpError> {
    let params = params.as_object().ok_or_else(|| {
        mcp_error(
            -32602,
            "tools/call params must be an object".to_string(),
            None,
        )
    })?;
    let tool_name = params
        .get("name")
        .and_then(Value::as_str)
        .ok_or_else(|| mcp_error(-32602, "tool name is required".to_string(), None))?;
    let arguments = params
        .get("arguments")
        .cloned()
        .unwrap_or_else(|| json!({}));

    if !arguments.is_object() {
        return Err(mcp_error(
            -32602,
            "tool arguments must be an object".to_string(),
            None,
        ));
    }

    let payload = call_tool(args, tool_name, arguments)?;
    Ok(json!({
        "content": [{
            "type": "text",
            "text": serde_json::to_string(&payload).unwrap_or_else(|_| "null".to_string()),
        }],
    }))
}

fn call_tool(args: &McpArgs, tool_name: &str, arguments: Value) -> Result<Value, McpError> {
    match tool_name {
        "lux_get_version" => {
            return Ok(json!({ "serverName": SERVER_NAME, "serverVersion": SERVER_VERSION }))
        }
        "lux_execute_shell" => require_string(&arguments, "commandText")?,
        "lux_execute_git" => require_string(&arguments, "gitArguments")?,
        "lux_control_play_mode"
        | "lux_simulate_mouse_ui"
        | "lux_simulate_mouse_input"
        | "lux_simulate_keyboard" => require_string(&arguments, "action")?,
        _ => {}
    }

    let command = unity_command_for_tool(tool_name).ok_or_else(|| {
        mcp_error(
            -32602,
            format!("Unknown tool: {tool_name}"),
            Some(json!({ "tool": tool_name })),
        )
    })?;

    let response = send_unity_command(args, command, tool_params(tool_name, arguments))
        .map_err(|error| mcp_error(-32603, error.to_string(), None))?;

    if response.get("ok").and_then(Value::as_bool) != Some(true) {
        let message = response
            .get("errorMessage")
            .and_then(Value::as_str)
            .unwrap_or("Unity TCP request failed")
            .to_string();
        return Err(mcp_error(-32603, message, Some(response)));
    }

    Ok(extract_tool_payload(tool_name, &response))
}

fn unity_command_for_tool(tool_name: &str) -> Option<&'static str> {
    match tool_name {
        "unity_selected_file_context" => Some("get_selected_file_context"),
        "lux_context" => Some("get_lux_context"),
        "lux_get_console_logs" => Some("get_lux_console_logs"),
        "lux_get_hierarchy" => Some("get_lux_hierarchy"),
        "lux_find_game_objects" => Some("find_lux_game_objects"),
        "lux_screenshot" => Some("capture_lux_screenshot"),
        "lux_execute_shell" => Some("execute_lux_shell"),
        "lux_execute_git" => Some("execute_lux_git"),
        "lux_compile" => Some("compile_lux_project"),
        "lux_run_tests" => Some("run_lux_tests"),
        "lux_clear_console" => Some("clear_lux_console"),
        "lux_focus_window" => Some("focus_lux_window"),
        "lux_control_play_mode" => Some("control_lux_play_mode"),
        "lux_execute_dynamic_code" => Some("execute_lux_dynamic_code"),
        "lux_simulate_mouse_ui" => Some("simulate_lux_mouse_ui"),
        "lux_simulate_mouse_input" => Some("simulate_lux_mouse_input"),
        "lux_simulate_keyboard" => Some("simulate_lux_keyboard"),
        "lux_get_version" => None,
        _ => None,
    }
}

fn tool_params(tool_name: &str, arguments: Value) -> Value {
    match tool_name {
        "lux_execute_shell" | "lux_execute_git" => {
            let mut params = arguments;
            if let Some(object) = params.as_object_mut() {
                object
                    .entry("approvalGranted".to_string())
                    .or_insert(Value::Bool(false));
            }
            params
        }
        _ => arguments,
    }
}

fn send_unity_command(args: &McpArgs, command: &str, params: Value) -> anyhow::Result<Value> {
    let project_root = resolve_project_root(&args.project_path)?;
    let discovery = read_unity_bridge_discovery(&project_root)?;
    let request = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": command,
        "token": discovery.token,
        "params": params,
    });
    let response_line = send_unity_tcp_line_with_timeout(
        &discovery,
        &format!("{}\n", serde_json::to_string(&request)?),
        Duration::from_secs(30),
    )?;
    serde_json::from_str(&response_line).context("Unity TCP response was not valid JSON")
}

fn extract_tool_payload(tool_name: &str, response: &Value) -> Value {
    let payload = response.get("payload").cloned().unwrap_or(Value::Null);
    match tool_name {
        "unity_selected_file_context" => payload
            .get("selectedFileContext")
            .cloned()
            .unwrap_or(payload),
        "lux_context" => payload.get("luxContext").cloned().unwrap_or(payload),
        "lux_compile" => payload.get("compileResult").cloned().unwrap_or(payload),
        "lux_run_tests" => payload.get("testRunResult").cloned().unwrap_or(payload),
        _ => payload
            .get("luxAutomationResult")
            .cloned()
            .unwrap_or(payload),
    }
}

fn require_string(arguments: &Value, field: &str) -> Result<(), McpError> {
    let Some(value) = arguments.get(field).and_then(Value::as_str) else {
        return Err(mcp_error(-32602, format!("{field} is required"), None));
    };
    if value.trim().is_empty() {
        return Err(mcp_error(-32602, format!("{field} is required"), None));
    }
    Ok(())
}

fn tool_definitions() -> Vec<Value> {
    vec![
        tool("unity_selected_file_context", "Unity Selected File Context", "Returns Unity editor metadata for the currently selected project files without reading file contents.", empty_schema(), annotations(true, false, false)),
        tool("lux_context", "Lux Context", "Returns Lux package, remote gateway, and automation policy metadata from the Unity editor.", empty_schema(), annotations(true, false, false)),
        tool("lux_get_console_logs", "Get Lux Console Logs", "Returns Unity console log entries with optional filtering and stack traces.", schema(&[("logType", json!({"type":"string"})), ("maxCount", json!({"type":"number"})), ("searchText", json!({"type":"string"})), ("includeStackTrace", json!({"type":"boolean"})), ("useRegex", json!({"type":"boolean"})), ("searchInStackTrace", json!({"type":"boolean"}))], &[]), annotations(true, false, false)),
        tool("lux_get_hierarchy", "Get Lux Hierarchy", "Returns Unity hierarchy metadata from the editor.", schema(&[("rootPath", json!({"type":"string"})), ("maxDepth", json!({"type":"number"})), ("includeComponents", json!({"type":"boolean"})), ("includeInactive", json!({"type":"boolean"})), ("includePaths", json!({"type":"boolean"})), ("useComponentsLut", json!({"type":"boolean"})), ("useSelection", json!({"type":"boolean"}))], &[]), annotations(true, false, false)),
        tool("lux_find_game_objects", "Find Lux Game Objects", "Finds Unity game objects by name, component, tag, layer, or inherited properties.", schema(&[("namePattern", json!({"type":"string"})), ("searchMode", json!({"type":"string"})), ("requiredComponents", json!({"type":"array","items":{"type":"string"}})), ("tag", json!({"type":"string"})), ("layer", json!({"type":"string"})), ("maxResults", json!({"type":"number"})), ("includeInactive", json!({"type":"boolean"})), ("includeInheritedProperties", json!({"type":"boolean"}))], &[]), annotations(true, false, false)),
        tool("lux_screenshot", "Capture Lux Screenshot", "Captures a Unity editor screenshot through Lux automation.", schema(&[("captureMode", json!({"type":"string"})), ("windowName", json!({"type":"string"})), ("resolutionScale", json!({"type":"number"})), ("matchMode", json!({"type":"string"})), ("outputDirectory", json!({"type":"string"})), ("annotateElements", json!({"type":"boolean"})), ("elementsOnly", json!({"type":"boolean"}))], &[]), annotations(true, false, false)),
        tool("lux_get_version", "Get Lux Version", "Returns the MCP helper server version.", empty_schema(), annotations(true, false, true)),
        tool("lux_execute_shell", "Lux Execute Shell", "Runs a shell command through Lux automation policy and returns the audited result.", automation_schema(&["commandText"]), annotations(false, true, false)),
        tool("lux_execute_git", "Lux Execute Git", "Runs git arguments through Lux automation policy and returns the audited result.", automation_schema(&["gitArguments"]), annotations(false, true, false)),
        tool("lux_compile", "Compile Lux Project", "Triggers Unity compilation through the Lux bridge.", empty_schema(), annotations(false, false, false)),
        tool("lux_run_tests", "Run Lux Tests", "Runs Unity tests through the Lux bridge.", schema(&[("testPlatform", json!({"type":"string"})), ("testResults", json!({"type":"string"}))], &[]), annotations(false, false, false)),
        tool("lux_clear_console", "Clear Lux Console", "Clears the Unity console through Lux automation.", schema(&[("addConfirmationMessage", json!({"type":"boolean"}))], &[]), annotations(false, true, false)),
        tool("lux_focus_window", "Focus Lux Window", "Focuses the Lux or Unity editor window.", empty_schema(), annotations(false, false, true)),
        tool("lux_control_play_mode", "Control Lux Play Mode", "Controls or reads Unity play mode state.", schema(&[("action", json!({"type":"string","enum":["play","stop","pause","resume","status"]}))], &["action"]), annotations(false, false, false)),
        tool("lux_execute_dynamic_code", "Execute Lux Dynamic Code", "Compiles or executes dynamic C# code in the Unity editor.", schema(&[("code", json!({"type":"string"})), ("file", json!({"type":"string"})), ("parameters", json!({"type":"object"})), ("compileOnly", json!({"type":"boolean"})), ("yieldToForegroundRequests", json!({"type":"boolean"}))], &[]), annotations(false, true, false)),
        tool("lux_simulate_mouse_ui", "Simulate Lux Mouse UI", "Simulates mouse actions against Unity UI elements.", schema(&[("action", json!({"type":"string"})), ("x", json!({"type":"number"})), ("y", json!({"type":"number"})), ("fromX", json!({"type":"number"})), ("fromY", json!({"type":"number"})), ("dragSpeed", json!({"type":"number"})), ("durationMs", json!({"type":"number"})), ("button", json!({"type":"string"})), ("bypassRaycast", json!({"type":"boolean"})), ("targetPath", json!({"type":"string"})), ("dropTargetPath", json!({"type":"string"}))], &["action"]), annotations(false, false, false)),
        tool("lux_simulate_mouse_input", "Simulate Lux Mouse Input", "Simulates low-level Unity mouse input.", schema(&[("action", json!({"type":"string"})), ("x", json!({"type":"number"})), ("y", json!({"type":"number"})), ("button", json!({"type":"string"})), ("durationMs", json!({"type":"number"})), ("deltaX", json!({"type":"number"})), ("deltaY", json!({"type":"number"})), ("scrollX", json!({"type":"number"})), ("scrollY", json!({"type":"number"})), ("steps", json!({"type":"number"}))], &["action"]), annotations(false, false, false)),
        tool("lux_simulate_keyboard", "Simulate Lux Keyboard", "Simulates keyboard actions in Unity.", schema(&[("action", json!({"type":"string"})), ("key", json!({"type":"string"})), ("durationMs", json!({"type":"number"}))], &["action"]), annotations(false, false, false)),
    ]
}

fn tool(
    name: &str,
    title: &str,
    description: &str,
    input_schema: Value,
    annotations: Value,
) -> Value {
    json!({
        "name": name,
        "title": title,
        "description": description,
        "inputSchema": input_schema,
        "annotations": annotations,
    })
}

fn empty_schema() -> Value {
    schema(&[], &[])
}

fn automation_schema(required: &[&str]) -> Value {
    schema(
        &[
            ("commandText", json!({"type":"string"})),
            ("gitArguments", json!({"type":"string"})),
            ("workingDirectory", json!({"type":"string"})),
            ("actor", json!({"type":"string"})),
            ("approvalGranted", json!({"type":"boolean"})),
        ],
        required,
    )
}

fn schema(properties: &[(&str, Value)], required: &[&str]) -> Value {
    let properties = properties
        .iter()
        .map(|(name, schema)| ((*name).to_string(), schema.clone()))
        .collect::<serde_json::Map<String, Value>>();
    json!({
        "type": "object",
        "properties": properties,
        "required": required,
        "additionalProperties": false,
    })
}

fn annotations(read_only: bool, destructive: bool, idempotent: bool) -> Value {
    json!({
        "readOnlyHint": read_only,
        "destructiveHint": destructive,
        "idempotentHint": idempotent,
        "openWorldHint": false,
    })
}

#[derive(Debug)]
struct McpError {
    code: i64,
    message: String,
    data: Option<Value>,
}

fn mcp_error(code: i64, message: String, data: Option<Value>) -> McpError {
    McpError {
        code,
        message,
        data,
    }
}

fn error_response(id: Value, code: i64, message: String, data: Option<Value>) -> JsonRpcResponse {
    JsonRpcResponse {
        jsonrpc: "2.0",
        id,
        result: None,
        error: Some(JsonRpcError {
            code,
            message,
            data,
        }),
    }
}

#[allow(dead_code)]
fn ensure_known_tool(tool_name: &str) -> anyhow::Result<()> {
    if tool_name == "lux_get_version" || unity_command_for_tool(tool_name).is_some() {
        Ok(())
    } else {
        bail!("Unknown tool: {tool_name}")
    }
}
