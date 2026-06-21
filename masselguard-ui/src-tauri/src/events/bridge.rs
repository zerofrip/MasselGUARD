use std::sync::Arc;

use serde_json::Value;
use tauri::{AppHandle, Manager};

use super::manager::StreamManagerState;
use super::StreamHealthState;

pub struct EventBridge;

impl EventBridge {
    pub fn start(app: &AppHandle, since_seq: u64) -> Arc<StreamHealthState> {
        let health = Arc::new(StreamHealthState::new());
        let mgr = Arc::new(StreamManagerState::new(health.clone()));
        mgr.set_since_seq(since_seq);
        app.manage(EventBridgeState {
            health: health.clone(),
            manager: mgr.clone(),
        });
        #[cfg(windows)]
        super::subscriber::run_subscriber(app.clone(), health.clone(), mgr);
        health
    }
}

pub struct EventBridgeState {
    pub health: Arc<StreamHealthState>,
    pub manager: Arc<StreamManagerState>,
}

pub fn maybe_show_notification(app: &AppHandle, payload: &Value) {
    use tauri_plugin_notification::NotificationExt;

    let primary = payload
        .get("primary")
        .and_then(|v| v.as_str())
        .unwrap_or("MasselGUARD");
    let secondary = payload
        .get("secondary")
        .and_then(|v| v.as_str())
        .unwrap_or("");
    let _ = app
        .notification()
        .builder()
        .title(primary)
        .body(secondary)
        .show();
}
