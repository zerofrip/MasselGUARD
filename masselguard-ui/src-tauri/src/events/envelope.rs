use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct EventEnvelope {
    #[serde(default)]
    pub version: u32,
    pub seq: Option<u64>,
    pub r#type: String,
    #[serde(default)]
    pub ts: Option<Value>,
    #[serde(default)]
    pub payload: Value,
}

impl EventEnvelope {
    pub fn normalize(&self) -> NormalizedEvent {
        let ts = match &self.ts {
            Some(Value::Number(n)) => n.as_i64().map(|ms| ms.to_string()),
            Some(Value::String(s)) => Some(s.clone()),
            _ => None,
        };

        NormalizedEvent {
            version: self.version,
            seq: self.seq,
            event_type: self.r#type.clone(),
            ts,
            payload: self.payload.clone(),
        }
    }
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct NormalizedEvent {
    pub version: u32,
    pub seq: Option<u64>,
    #[serde(rename = "type")]
    pub event_type: String,
    pub ts: Option<String>,
    pub payload: Value,
}

pub fn parse_line(line: &str) -> Option<EventEnvelope> {
    if line.contains("\"op\"") {
        return None;
    }
    serde_json::from_str(line).ok()
}
