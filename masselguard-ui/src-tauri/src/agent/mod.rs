use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

pub const PIPE_NAME: &str = r"\\.\pipe\MasselGUARD";
pub const EVENT_PIPE_NAME: &str = r"\\.\pipe\MasselGUARDAgent-events";

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct IpcRequest {
    pub jsonrpc: String,
    pub id: u64,
    pub method: String,
    #[serde(default)]
    pub params: Value,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct IpcResponse {
    pub jsonrpc: String,
    pub id: u64,
    pub result: Option<Value>,
    pub error: Option<IpcError>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct IpcError {
    pub code: i32,
    pub message: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct IpcEvent {
    pub jsonrpc: String,
    pub method: String,
    pub params: IpcEventParams,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct IpcEventParams {
    pub event: String,
    pub payload: Value,
}

#[derive(Clone)]
pub struct AgentClient {
    next_id: Arc<AtomicU64>,
    #[cfg(windows)]
    lock: Arc<Mutex<()>>,
}

impl Default for AgentClient {
    fn default() -> Self {
        Self::new()
    }
}

impl AgentClient {
    pub fn new() -> Self {
        Self {
            next_id: Arc::new(AtomicU64::new(1)),
            #[cfg(windows)]
            lock: Arc::new(Mutex::new(())),
        }
    }

    pub async fn call(&self, method: &str, params: Value) -> Result<Value, String> {
        let id = self.next_id.fetch_add(1, Ordering::Relaxed);
        let req = IpcRequest {
            jsonrpc: "2.0".into(),
            id,
            method: method.into(),
            params,
        };
        let line = serde_json::to_string(&req).map_err(|e| e.to_string())?;
        let response_line = tokio::task::spawn_blocking({
            let line = line;
            let client = self.clone();
            move || client.call_sync(&line)
        })
        .await
        .map_err(|e| e.to_string())??;

        let resp: IpcResponse = serde_json::from_str(&response_line).map_err(|e| e.to_string())?;
        if let Some(err) = resp.error {
            return Err(err.message);
        }
        resp.result.ok_or_else(|| "empty result".into())
    }

    #[cfg(windows)]
    fn call_sync(&self, line: &str) -> Result<String, String> {
        use std::io::{BufRead, BufReader, Write};
        use std::time::Duration;

        let _guard = self.lock.lock().map_err(|e| e.to_string())?;

        for attempt in 0..5 {
            match try_pipe_exchange(line) {
                Ok(r) => return Ok(r),
                Err(e) if attempt < 4 => {
                    std::thread::sleep(Duration::from_millis(300 * (attempt + 1) as u64));
                    let _ = e;
                }
                Err(e) => return Err(e),
            }
        }
        Err("agent unreachable".into())
    }

    #[cfg(not(windows))]
    fn call_sync(&self, _line: &str) -> Result<String, String> {
        Err("MasselGUARDAgent IPC is only available on Windows".into())
    }
}

#[cfg(windows)]
fn try_pipe_exchange(line: &str) -> Result<String, String> {
    use std::io::{BufRead, BufReader, Write};
    use std::os::windows::io::FromRawHandle;
    use windows::Win32::Foundation::CloseHandle;
    use windows::Win32::Storage::FileSystem::{
        CreateFileW, FILE_ATTRIBUTE_NORMAL, FILE_SHARE_READ, FILE_SHARE_WRITE, OPEN_EXISTING,
    };
    use windows::Win32::System::Pipes::WaitNamedPipeW;
    use windows_core::PCWSTR;

    const GENERIC_READ: u32 = 0x80000000;
    const GENERIC_WRITE: u32 = 0x40000000;

        let wide: Vec<u16> = format!("{}\0", PIPE_NAME)
        .encode_utf16()
        .collect();

    unsafe {
        if WaitNamedPipeW(PCWSTR(wide.as_ptr()), 5000).is_err() {
            return Err("agent pipe not available".into());
        }

        let handle = CreateFileW(
            PCWSTR(wide.as_ptr()),
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            None,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            None,
        )
        .map_err(|e| format!("CreateFileW: {e}"))?;

        let raw = handle.0 as _;
        let file = std::fs::File::from_raw_handle(raw);
        let mut writer = std::io::BufWriter::new(file.try_clone().map_err(|e| e.to_string())?);
        writeln!(writer, "{line}").map_err(|e| e.to_string())?;
        writer.flush().map_err(|e| e.to_string())?;

        let reader = BufReader::new(file);
        let response = reader
            .lines()
            .next()
            .ok_or_else(|| "no response".to_string())?
            .map_err(|e| e.to_string())?;

        CloseHandle(handle).ok();
        Ok(response)
    }
}

pub async fn ensure_agent_running(app: &tauri::AppHandle) -> Result<(), String> {
    let client = AgentClient::new();
    if client.call("agent.ping", Value::Null).await.is_ok() {
        return Ok(());
    }

    #[cfg(windows)]
    {
        use std::process::Command;
        use tauri::Manager;

        let dir = app
            .path()
            .executable_dir()
            .map_err(|e| e.to_string())?;
        let agent = dir.join("MasselGUARDAgent.exe");
        if !agent.exists() {
            return Err(format!("MasselGUARDAgent.exe not found at {}", agent.display()));
        }
        Command::new(agent)
            .spawn()
            .map_err(|e| format!("failed to spawn agent: {e}"))?;

        for _ in 0..20 {
            tokio::time::sleep(std::time::Duration::from_millis(250)).await;
            if client.call("agent.ping", Value::Null).await.is_ok() {
                return Ok(());
            }
        }
        return Err("agent did not start in time".into());
    }

    #[cfg(not(windows))]
    {
        let _ = app;
        Err("MasselGUARDAgent must run on Windows".into())
    }
}
