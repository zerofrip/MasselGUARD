use std::io::{BufRead, BufReader, Write};
use std::sync::Arc;
use std::time::Duration;

use tauri::{AppHandle, Emitter};

use super::manager::{build_subscribe_line, handle_line, StreamManagerState};
use super::StreamHealthState;

const EVENT_PIPE: &str = r"\\.\pipe\MasselGUARDAgent-events";

pub fn run_subscriber(app: AppHandle, health: Arc<StreamHealthState>, mgr: Arc<StreamManagerState>) {
    tauri::async_runtime::spawn(async move {
        let mut backoff_ms = 250u64;
        let mut fail_count = 0u32;

        loop {
            match read_stream(&app, &health, &mgr).await {
                Ok(()) => {
                    fail_count = 0;
                    backoff_ms = 250;
                }
                Err(e) => {
                    fail_count += 1;
                    health.connected.store(false, std::sync::atomic::Ordering::Relaxed);
                    let status = if fail_count >= 3 { "degraded" } else { "disconnected" };
                    health.degraded.store(fail_count >= 3, std::sync::atomic::Ordering::Relaxed);
                    let _ = app.emit(
                        "mg/stream",
                        serde_json::json!({ "status": status, "error": e }),
                    );
                }
            }

            tokio::time::sleep(Duration::from_millis(backoff_ms)).await;
            backoff_ms = (backoff_ms * 2).min(2000);
        }
    });
}

async fn read_stream(
    app: &AppHandle,
    health: &StreamHealthState,
    mgr: &StreamManagerState,
) -> Result<(), String> {
    let app = app.clone();
    let health = health.clone();
    let mgr = mgr.clone();
    tokio::task::spawn_blocking(move || blocking_read(&app, &health, &mgr))
        .await
        .map_err(|e| e.to_string())?
}

fn blocking_read(app: &AppHandle, health: &StreamHealthState, mgr: &StreamManagerState) -> Result<(), String> {
    use std::os::windows::io::FromRawHandle;
    use windows::Win32::Storage::FileSystem::{
        CreateFileW, FILE_ATTRIBUTE_NORMAL, FILE_SHARE_READ, FILE_SHARE_WRITE, OPEN_EXISTING,
    };
    use windows::Win32::System::Pipes::WaitNamedPipeW;
    use windows_core::PCWSTR;

    const GENERIC_READ: u32 = 0x80000000;
    const GENERIC_WRITE: u32 = 0x40000000;

    let wide: Vec<u16> = format!("{EVENT_PIPE}\0").encode_utf16().collect();

    unsafe {
        if WaitNamedPipeW(PCWSTR(wide.as_ptr()), 5000).is_err() {
            return Err("event pipe not available".into());
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
        .map_err(|e| format!("CreateFileW events: {e}"))?;

        health.connected.store(true, std::sync::atomic::Ordering::Relaxed);
        health.degraded.store(false, std::sync::atomic::Ordering::Relaxed);
        let _ = app.emit("mg/stream", serde_json::json!({ "status": "connected" }));

        let file = std::fs::File::from_raw_handle(handle.0 as _);
        let mut writer = std::io::BufWriter::new(file.try_clone().map_err(|e| e.to_string())?);
        let since = mgr.last_seq.load(std::sync::atomic::Ordering::Relaxed);
        let sub = build_subscribe_line(since);
        writeln!(writer, "{sub}").map_err(|e| e.to_string())?;
        writer.flush().map_err(|e| e.to_string())?;

        let reader = BufReader::new(file);
        for line in reader.lines() {
            let line = line.map_err(|e| e.to_string())?;
            if line.trim().is_empty() {
                continue;
            }
            handle_line(app, mgr, health, &line);
        }
    }

    Err("event stream closed".into())
}
