use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;

use serde_json::json;
use tauri::{AppHandle, Emitter};

use super::envelope::parse_line;
use super::{ExtendedStreamHealth, StreamHealthState};

pub struct StreamManagerState {
    pub health: Arc<StreamHealthState>,
    pub last_seq: AtomicU64,
    pub gaps_detected: AtomicU64,
    pub replay_requests: AtomicU64,
}

impl StreamManagerState {
    pub fn new(health: Arc<StreamHealthState>) -> Self {
        Self {
            health,
            last_seq: AtomicU64::new(0),
            gaps_detected: AtomicU64::new(0),
            replay_requests: AtomicU64::new(0),
        }
    }

    pub fn set_since_seq(&self, seq: u64) {
        self.last_seq.store(seq, Ordering::Relaxed);
    }

    pub fn snapshot(&self) -> ExtendedStreamHealth {
        ExtendedStreamHealth {
            connected: self.health.connected.load(Ordering::Relaxed),
            degraded: self.health.degraded.load(Ordering::Relaxed),
            last_event_ms: self.health.last_event_ms.load(Ordering::Relaxed),
            last_heartbeat_ms: self.health.last_heartbeat_ms.load(Ordering::Relaxed),
            last_seq: self.last_seq.load(Ordering::Relaxed),
            gaps_detected: self.gaps_detected.load(Ordering::Relaxed),
            replay_requests: self.replay_requests.load(Ordering::Relaxed),
        }
    }
}

pub fn build_subscribe_line(since_seq: u64) -> String {
    json!({
        "op": "subscribe",
        "version": 1,
        "sinceSeq": since_seq,
        "filters": ["*"]
    })
    .to_string()
}

pub fn handle_line(app: &AppHandle, mgr: &StreamManagerState, health: &StreamHealthState, line: &str) {
    if let Some(env) = parse_line(line) {
        if env.version > 1 {
            return;
        }
        let normalized = env.normalize();
        if normalized.event_type == "agent.heartbeat" {
            health.mark_heartbeat();
        } else {
            health.mark_event();
        }
        if let Some(seq) = normalized.seq {
            let last = mgr.last_seq.load(Ordering::Relaxed);
            if last > 0 && seq > last + 1 {
                mgr.gaps_detected.fetch_add(1, Ordering::Relaxed);
                let _ = app.emit(
                    "mg/stream",
                    json!({ "status": "gap", "from": last + 1, "to": seq - 1 }),
                );
            }
            if seq > last {
                mgr.last_seq.store(seq, Ordering::Relaxed);
            }
        }
        let _ = app.emit(
            "mg/event",
            json!({
                "type": normalized.event_type,
                "payload": normalized.payload,
                "ts": normalized.ts,
                "version": normalized.version,
                "seq": normalized.seq,
            }),
        );
        if normalized.event_type == "notification" {
            super::bridge::maybe_show_notification(app, &normalized.payload);
        }
    }
}
