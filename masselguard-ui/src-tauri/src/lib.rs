mod agent;
mod events;

use agent::{ensure_agent_running, AgentClient};
use events::EventBridge;
use serde_json::{json, Value};
use tauri::{AppHandle, Emitter, Manager, State};

pub struct AgentState {
    pub client: AgentClient,
}

async fn rpc(state: &AgentState, method: &str, params: Value) -> Result<Value, String> {
    state.client.call(method, params).await
}

#[tauri::command]
async fn agent_ping(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "agent.ping", json!({})).await
}

#[tauri::command]
async fn tunnel_list(
    state: State<'_, AgentState>,
    group: Option<String>,
    active_only: Option<bool>,
    search: Option<String>,
    include_archived: Option<bool>,
    sort: Option<String>,
) -> Result<Value, String> {
    rpc(
        &state,
        "tunnel.list",
        json!({
            "group": group,
            "activeOnly": active_only.unwrap_or(false),
            "search": search,
            "includeArchived": include_archived.unwrap_or(false),
            "sort": sort.unwrap_or_else(|| "name".into()),
        }),
    )
    .await
}

#[tauri::command]
async fn tunnels_list(
    state: State<'_, AgentState>,
    group: Option<String>,
    active_only: Option<bool>,
    search: Option<String>,
    include_archived: Option<bool>,
    sort: Option<String>,
) -> Result<Value, String> {
    tunnel_list(state, group, active_only, search, include_archived, sort).await
}

#[tauri::command]
async fn tunnel_get(
    state: State<'_, AgentState>,
    name: String,
    include_config: Option<bool>,
) -> Result<Value, String> {
    rpc(
        &state,
        "tunnel.get",
        json!({ "name": name, "includeConfig": include_config.unwrap_or(true) }),
    )
    .await
}

#[tauri::command]
async fn tunnel_status(state: State<'_, AgentState>, name: Option<String>) -> Result<Value, String> {
    rpc(&state, "tunnel.status", json!({ "name": name })).await
}

#[tauri::command]
async fn tunnel_connect(state: State<'_, AgentState>, name: String) -> Result<Value, String> {
    rpc(&state, "tunnel.connect", json!({ "name": name })).await
}

#[tauri::command]
async fn tunnel_disconnect(
    state: State<'_, AgentState>,
    name: Option<String>,
) -> Result<Value, String> {
    rpc(&state, "tunnel.disconnect", json!({ "name": name })).await
}

#[tauri::command]
async fn tunnel_reconnect(state: State<'_, AgentState>, name: String) -> Result<Value, String> {
    rpc(&state, "tunnel.reconnect", json!({ "name": name })).await
}

#[tauri::command]
async fn tunnel_import(
    state: State<'_, AgentState>,
    path: Option<String>,
    config: Option<String>,
    name: Option<String>,
    group: Option<String>,
    on_conflict: Option<String>,
) -> Result<Value, String> {
    rpc(
        &state,
        "tunnel.import",
        json!({
            "path": path,
            "config": config,
            "name": name,
            "group": group,
            "onConflict": on_conflict.unwrap_or_else(|| "fail".into()),
        }),
    )
    .await
}

#[tauri::command]
async fn tunnel_export(
    state: State<'_, AgentState>,
    name: String,
    dest: Option<String>,
    mode: Option<String>,
) -> Result<Value, String> {
    rpc(
        &state,
        "tunnel.export",
        json!({ "name": name, "dest": dest, "mode": mode.unwrap_or_else(|| "full".into()) }),
    )
    .await
}

#[tauri::command]
async fn tunnel_create(
    state: State<'_, AgentState>,
    name: String,
    config: String,
    group: Option<String>,
    notes: Option<String>,
    tags: Option<Vec<String>>,
) -> Result<Value, String> {
    rpc(
        &state,
        "tunnel.create",
        json!({ "name": name, "config": config, "group": group, "notes": notes, "tags": tags }),
    )
    .await
}

#[tauri::command]
async fn tunnel_clone(
    state: State<'_, AgentState>,
    name: String,
    new_name: Option<String>,
) -> Result<Value, String> {
    rpc(
        &state,
        "tunnel.clone",
        json!({ "name": name, "newName": new_name }),
    )
    .await
}

#[tauri::command]
async fn tunnel_validate(
    state: State<'_, AgentState>,
    name: Option<String>,
    config: Option<String>,
    exclude_name: Option<String>,
) -> Result<Value, String> {
    rpc(
        &state,
        "tunnel.validate",
        json!({ "name": name, "config": config, "excludeName": exclude_name }),
    )
    .await
}

#[tauri::command]
async fn tunnels_create(
    state: State<'_, AgentState>,
    name: String,
    config: String,
    group: Option<String>,
    notes: Option<String>,
    tags: Option<Vec<String>>,
) -> Result<Value, String> {
    tunnel_create(state, name, config, group, notes, tags).await
}

#[tauri::command]
async fn tunnels_clone(
    state: State<'_, AgentState>,
    name: String,
    new_name: Option<String>,
) -> Result<Value, String> {
    tunnel_clone(state, name, new_name).await
}

#[tauri::command]
async fn tunnels_validate(
    state: State<'_, AgentState>,
    name: Option<String>,
    config: Option<String>,
    exclude_name: Option<String>,
) -> Result<Value, String> {
    tunnel_validate(state, name, config, exclude_name).await
}

#[tauri::command]
async fn tunnel_update(state: State<'_, AgentState>, tunnel: Value) -> Result<Value, String> {
    rpc(&state, "tunnel.update", tunnel).await
}

#[tauri::command]
async fn tunnel_delete(state: State<'_, AgentState>, name: String) -> Result<Value, String> {
    rpc(&state, "tunnel.delete", json!({ "name": name })).await
}

#[tauri::command]
async fn wifi_current(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "wifi.current", json!({})).await
}

#[tauri::command]
async fn wifi_rules_get(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "wifi.rules.list", json!({})).await
}

#[tauri::command]
async fn wifi_rules_set(state: State<'_, AgentState>, rules: Value) -> Result<Value, String> {
    rpc(&state, "wifi.rules.set", rules).await
}

#[tauri::command]
async fn wifi_rules_test(
    state: State<'_, AgentState>,
    ssid: Option<String>,
    is_open: bool,
) -> Result<Value, String> {
    rpc(
        &state,
        "wifi.rules.test",
        json!({ "ssid": ssid, "isOpen": is_open }),
    )
    .await
}

#[tauri::command]
async fn killswitch_get(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "killswitch.status", json!({})).await
}

#[tauri::command]
async fn killswitch_set(state: State<'_, AgentState>, payload: Value) -> Result<Value, String> {
    rpc(&state, "killswitch.set", payload).await
}

#[tauri::command]
async fn config_get(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "config.get", json!({})).await
}

#[tauri::command]
async fn config_set(state: State<'_, AgentState>, patch: Value) -> Result<Value, String> {
    rpc(&state, "config.set", json!({ "patch": patch })).await
}

#[tauri::command]
async fn split_tunnel_get(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "split_tunnel.get", json!({})).await
}

#[tauri::command]
async fn split_tunnel_set(state: State<'_, AgentState>, rules: Value) -> Result<Value, String> {
    rpc(&state, "split_tunnel.set", json!({ "rules": rules })).await
}

#[tauri::command]
async fn network_lock_get(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "network_lock.get", json!({})).await
}

#[tauri::command]
async fn network_lock_set(state: State<'_, AgentState>, config: Value) -> Result<Value, String> {
    rpc(&state, "network_lock.set", json!({ "config": config })).await
}

#[tauri::command]
async fn networklock_status(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "networklock.status", json!({})).await
}

#[tauri::command]
async fn networklock_enable(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "networklock.enable", json!({})).await
}

#[tauri::command]
async fn networklock_disable(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "networklock.disable", json!({})).await
}

#[tauri::command]
async fn networklock_set_mode(state: State<'_, AgentState>, mode: String) -> Result<Value, String> {
    rpc(&state, "networklock.set_mode", json!({ "mode": mode })).await
}

#[tauri::command]
async fn networklock_set_lan_access(
    state: State<'_, AgentState>,
    enabled: bool,
    exceptions: Option<Vec<String>>,
) -> Result<Value, String> {
    rpc(
        &state,
        "networklock.set_lan_access",
        json!({ "enabled": enabled, "exceptions": exceptions }),
    )
    .await
}

#[tauri::command]
async fn networklock_set_dns_policy(
    state: State<'_, AgentState>,
    policy: String,
    exceptions: Option<Vec<String>>,
) -> Result<Value, String> {
    rpc(
        &state,
        "networklock.set_dns_policy",
        json!({ "policy": policy, "exceptions": exceptions }),
    )
    .await
}

#[tauri::command]
async fn routeguard_status(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "routeguard.status", json!({})).await
}

#[tauri::command]
async fn routeguard_capabilities(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "routeguard.capabilities", json!({})).await
}

#[tauri::command]
async fn routeguard_sync(state: State<'_, AgentState>, force: bool) -> Result<Value, String> {
    rpc(&state, "routeguard.sync", json!({ "force": force })).await
}

#[tauri::command]
async fn routeguard_routing_test(
    state: State<'_, AgentState>,
    app_path: Option<String>,
    remote_ip: Option<String>,
    domain: Option<String>,
) -> Result<Value, String> {
    rpc(
        &state,
        "routeguard.routing.test",
        json!({ "appPath": app_path, "remoteIp": remote_ip.unwrap_or_else(|| "8.8.8.8".into()), "domain": domain }),
    )
    .await
}

#[tauri::command]
async fn routeguard_start(state: State<'_, AgentState>, wait_secs: u32) -> Result<Value, String> {
    rpc(&state, "routeguard.start", json!({ "waitSecs": wait_secs })).await
}

#[tauri::command]
async fn routeguard_observability_snapshot(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "routeguard.observability.snapshot", json!({})).await
}

#[tauri::command]
async fn support_export(
    state: State<'_, AgentState>,
    tier: Option<String>,
    include_crash_reports: Option<bool>,
    include_event_history: Option<bool>,
    include_tunnel_history: Option<bool>,
    dest: Option<String>,
) -> Result<Value, String> {
    let mut result = rpc(
        &state,
        "support.export",
        json!({
            "tier": tier.unwrap_or_else(|| "sanitized".into()),
            "includeCrashReports": include_crash_reports.unwrap_or(false),
            "includeEventHistory": include_event_history.unwrap_or(true),
            "includeTunnelHistory": include_tunnel_history.unwrap_or(false),
        }),
    )
    .await?;
    if let Some(dest_path) = dest {
        if let Some(src) = result.get("path").and_then(|p| p.as_str()) {
            std::fs::copy(src, &dest_path).map_err(|e| e.to_string())?;
            if let Some(obj) = result.as_object_mut() {
                obj.insert("savedPath".into(), json!(dest_path));
            }
        }
    }
    Ok(result)
}

#[tauri::command]
async fn support_export_status(
    state: State<'_, AgentState>,
    export_id: Option<String>,
) -> Result<Value, String> {
    rpc(
        &state,
        "support.export.status",
        json!({ "exportId": export_id }),
    )
    .await
}

#[tauri::command]
async fn telemetry_summary(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "telemetry.summary", json!({})).await
}

#[tauri::command]
async fn agent_diagnostics_resources(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "agent.diagnostics.resources", json!({})).await
}

#[tauri::command]
async fn routeguard_diagnostics_export(
    state: State<'_, AgentState>,
    tier: Option<String>,
    dest: Option<String>,
) -> Result<Value, String> {
    let mut result = rpc(
        &state,
        "routeguard.diagnostics.export",
        json!({ "tier": tier.unwrap_or_else(|| "sanitized".into()) }),
    )
    .await?;
    if let Some(dest_path) = dest {
        let src = result
            .get("path")
            .and_then(|p| p.as_str())
            .or_else(|| {
                result
                    .get("routeGuard")
                    .and_then(|rg| rg.get("path"))
                    .and_then(|p| p.as_str())
            });
        if let Some(src) = src {
            std::fs::copy(src, &dest_path).map_err(|e| e.to_string())?;
            if let Some(obj) = result.as_object_mut() {
                obj.insert("savedPath".into(), json!(dest_path));
            }
        }
    }
    Ok(result)
}

#[tauri::command]
async fn history_tunnel(
    state: State<'_, AgentState>,
    limit: Option<u32>,
    tunnel_name: Option<String>,
    include_failures: Option<bool>,
) -> Result<Value, String> {
    rpc(
        &state,
        "history.tunnel",
        json!({
            "limit": limit.unwrap_or(100),
            "tunnelName": tunnel_name,
            "includeFailures": include_failures.unwrap_or(true),
        }),
    )
    .await
}

#[tauri::command]
async fn history_tunnel_clear(
    state: State<'_, AgentState>,
    tunnel_name: Option<String>,
) -> Result<Value, String> {
    rpc(
        &state,
        "history.tunnel_clear",
        json!({ "tunnelName": tunnel_name }),
    )
    .await
}

#[tauri::command]
async fn history_tunnel_export(
    state: State<'_, AgentState>,
    dest: String,
    format: Option<String>,
    limit: Option<u32>,
    tunnel_name: Option<String>,
) -> Result<Value, String> {
    rpc(
        &state,
        "history.tunnel_export",
        json!({
            "dest": dest,
            "format": format.unwrap_or_else(|| "json".into()),
            "limit": limit.unwrap_or(1000),
            "tunnelName": tunnel_name,
        }),
    )
    .await
}

#[tauri::command]
async fn read_text_file(path: String) -> Result<String, String> {
    std::fs::read_to_string(&path).map_err(|e| e.to_string())
}

#[tauri::command]
async fn save_text_file(path: String, contents: String) -> Result<(), String> {
    std::fs::write(&path, contents).map_err(|e| e.to_string())
}

#[tauri::command]
async fn save_conf_file(app: AppHandle, contents: String, suggested_name: Option<String>) -> Result<Option<String>, String> {
    use tauri_plugin_dialog::DialogExt;
    let file = app
        .dialog()
        .file()
        .set_file_name(suggested_name.unwrap_or_else(|| "tunnel.conf".into()))
        .add_filter("WireGuard config", &["conf"])
        .blocking_save_file();
    if let Some(p) = file {
        let path = p.to_string();
        std::fs::write(&path, contents).map_err(|e| e.to_string())?;
        Ok(Some(path))
    } else {
        Ok(None)
    }
}

#[tauri::command]
async fn history_wifi(state: State<'_, AgentState>, limit: Option<u32>) -> Result<Value, String> {
    rpc(
        &state,
        "history.wifi",
        json!({ "limit": limit.unwrap_or(100) }),
    )
    .await
}

#[tauri::command]
async fn public_ip_refresh(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "system.public_ip", json!({})).await
}

#[tauri::command]
async fn event_stream_status(state: State<'_, events::EventBridgeState>) -> Result<Value, String> {
    Ok(serde_json::to_value(state.manager.snapshot()).map_err(|e| e.to_string())?)
}

#[tauri::command]
async fn agent_status(state: State<'_, AgentState>) -> Result<Value, String> {
    rpc(&state, "agent.status", json!({})).await
}

#[tauri::command]
async fn agent_event_replay(
    state: State<'_, AgentState>,
    since_seq: u64,
    limit: Option<u32>,
) -> Result<Value, String> {
    rpc(
        &state,
        "agent.event_replay",
        json!({ "sinceSeq": since_seq, "limit": limit.unwrap_or(128) }),
    )
    .await
}

#[tauri::command]
async fn event_stream_report_seq(
    state: State<'_, events::EventBridgeState>,
    seq: u64,
) -> Result<(), String> {
    state.manager.set_since_seq(seq);
    Ok(())
}

#[tauri::command]
async fn pick_conf_file(app: AppHandle) -> Result<Option<String>, String> {
    use tauri_plugin_dialog::DialogExt;
    let file = app
        .dialog()
        .file()
        .add_filter("WireGuard config", &["conf"])
        .blocking_pick_file();
    Ok(file.map(|p| p.to_string()))
}

pub fn spawn_event_listener(app: AppHandle, _client: AgentClient) {
    #[cfg(windows)]
    EventBridge::start(&app, 0);

    #[cfg(not(windows))]
    {
        let health = std::sync::Arc::new(events::StreamHealthState::new());
        let mgr = std::sync::Arc::new(events::manager::StreamManagerState::new(health.clone()));
        app.manage(events::EventBridgeState { health, manager: mgr });
        let _ = app;
    }
}

pub fn setup_tray(app: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    use tauri::menu::{Menu, MenuItem, PredefinedMenuItem};
    use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};

    let open_i = MenuItem::with_id(app, "open", "Open MasselGUARD", true, None::<&str>)?;
    let quit_i = MenuItem::with_id(app, "quit", "Exit", true, None::<&str>)?;
    let menu = Menu::with_items(app, &[&open_i, &PredefinedMenuItem::separator(app)?, &quit_i])?;

    let _tray = TrayIconBuilder::new()
        .icon(app.default_window_icon().unwrap().clone())
        .menu(&menu)
        .tooltip("MasselGUARD")
        .on_menu_event(|app, event| match event.id.as_ref() {
            "open" => {
                if let Some(w) = app.get_webview_window("main") {
                    let _ = w.show();
                    let _ = w.set_focus();
                }
            }
            "quit" => app.exit(0),
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                let app = tray.app_handle();
                if let Some(w) = app.get_webview_window("main") {
                    let _ = w.show();
                    let _ = w.set_focus();
                }
            }
        })
        .build(app)?;

    Ok(())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_notification::init())
        .plugin(tauri_plugin_dialog::init())
        .setup(|app| {
            let client = AgentClient::new();
            let handle = app.handle().clone();
            tauri::async_runtime::block_on(async {
                let _ = ensure_agent_running(&handle).await;
            });
            spawn_event_listener(handle, client.clone());
            app.manage(AgentState { client });
            setup_tray(app.handle())?;
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            agent_ping,
            tunnel_list,
            tunnels_list,
            tunnel_get,
            tunnel_status,
            tunnel_connect,
            tunnel_disconnect,
            tunnel_reconnect,
            tunnel_import,
            tunnel_export,
            tunnel_create,
            tunnel_clone,
            tunnel_validate,
            tunnels_create,
            tunnels_clone,
            tunnels_validate,
            tunnel_update,
            tunnel_delete,
            wifi_current,
            wifi_rules_get,
            wifi_rules_set,
            wifi_rules_test,
            killswitch_get,
            killswitch_set,
            config_get,
            config_set,
            split_tunnel_get,
            split_tunnel_set,
            network_lock_get,
            network_lock_set,
            networklock_status,
            networklock_enable,
            networklock_disable,
            networklock_set_mode,
            networklock_set_lan_access,
            networklock_set_dns_policy,
            routeguard_status,
            routeguard_capabilities,
            routeguard_sync,
            routeguard_routing_test,
            routeguard_start,
            routeguard_observability_snapshot,
            routeguard_diagnostics_export,
            support_export,
            support_export_status,
            telemetry_summary,
            agent_diagnostics_resources,
            history_tunnel,
            history_tunnel_clear,
            history_tunnel_export,
            history_wifi,
            public_ip_refresh,
            pick_conf_file,
            read_text_file,
            save_text_file,
            save_conf_file,
            event_stream_status,
            agent_status,
            agent_event_replay,
            event_stream_report_seq,
        ])
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
