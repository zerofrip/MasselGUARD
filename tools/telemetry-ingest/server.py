#!/usr/bin/env python3
"""Minimal telemetry ingest server (Phase 13). Opt-in cohort only — no PII."""

from __future__ import annotations

import gzip
import json
import os
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

STORE = Path(os.environ.get("TELEMETRY_STORE", "telemetry-data"))
ALLOWED_METRICS = {
    "feature.used",
    "crash.recorded",
    "update.check",
    "update.apply",
    "install.outcome",
    "driver.install",
    "network_lock.failure",
    "session.end",
    "session.start",
}
FORBIDDEN = ("endpoint", "privatekey", "ssid", "tunnelname", "publicip", "machinename")


class Handler(BaseHTTPRequestHandler):
    def do_POST(self) -> None:
        if self.path not in ("/v1/events", "/v1/events/"):
            self.send_error(404)
            return
        length = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(length)
        if self.headers.get("Content-Encoding") == "gzip":
            raw = gzip.decompress(raw)
        try:
            batch = json.loads(raw.decode("utf-8"))
        except json.JSONDecodeError:
            self.send_error(400, "invalid json")
            return
        if not validate_batch(batch):
            self.send_error(400, "forbidden fields or metrics")
            return
        STORE.mkdir(parents=True, exist_ok=True)
        day = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        out = STORE / f"ingest-{day}.ndjson"
        with out.open("a", encoding="utf-8") as f:
            f.write(json.dumps(batch, separators=(",", ":")) + "\n")
        self.send_response(204)
        self.end_headers()

    def log_message(self, fmt: str, *args) -> None:
        print(fmt % args)


def validate_batch(batch: dict) -> bool:
    if batch.get("schemaVersion") != 1:
        return False
    lower = json.dumps(batch).lower()
    for f in FORBIDDEN:
        if f'"{f}"' in lower:
            return False
    for m in batch.get("metrics", []):
        if m.get("name") not in ALLOWED_METRICS:
            return False
    return True


def main() -> None:
    port = int(os.environ.get("PORT", "8080"))
    print(f"Telemetry ingest on :{port} → {STORE.resolve()}")
    HTTPServer(("", port), Handler).serve_forever()


if __name__ == "__main__":
    main()
