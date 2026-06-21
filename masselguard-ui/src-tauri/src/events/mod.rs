use std::sync::atomic::{AtomicBool, AtomicI64, Ordering};
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};

pub mod bridge;
pub mod envelope;
pub mod health;
pub mod manager;
pub mod subscriber;

pub use bridge::{EventBridge, EventBridgeState};
pub use health::StreamHealth;

/// Shared stream health state updated by the event subscriber loop.
pub struct StreamHealthState {
    pub connected: AtomicBool,
    pub degraded: AtomicBool,
    pub last_event_ms: AtomicI64,
    pub last_heartbeat_ms: AtomicI64,
}

impl StreamHealthState {
    pub fn new() -> Self {
        Self {
            connected: AtomicBool::new(false),
            degraded: AtomicBool::new(false),
            last_event_ms: AtomicI64::new(0),
            last_heartbeat_ms: AtomicI64::new(0),
        }
    }

    pub fn snapshot(&self) -> StreamHealth {
        StreamHealth {
            connected: self.connected.load(Ordering::Relaxed),
            degraded: self.degraded.load(Ordering::Relaxed),
            last_event_ms: self.last_event_ms.load(Ordering::Relaxed),
            last_heartbeat_ms: self.last_heartbeat_ms.load(Ordering::Relaxed),
        }
    }

    pub fn mark_event(&self) {
        self.last_event_ms.store(now_ms(), Ordering::Relaxed);
    }

    pub fn mark_heartbeat(&self) {
        let now = now_ms();
        self.last_heartbeat_ms.store(now, Ordering::Relaxed);
        self.last_event_ms.store(now, Ordering::Relaxed);
    }
}

impl Default for StreamHealthState {
    fn default() -> Self {
        Self::new()
    }
}

#[derive(Debug, Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct StreamHealth {
    pub connected: bool,
    pub degraded: bool,
    pub last_event_ms: i64,
    pub last_heartbeat_ms: i64,
}

#[derive(Debug, Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ExtendedStreamHealth {
    pub connected: bool,
    pub degraded: bool,
    pub last_event_ms: i64,
    pub last_heartbeat_ms: i64,
    pub last_seq: u64,
    pub gaps_detected: u64,
    pub replay_requests: u64,
}

fn now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}
