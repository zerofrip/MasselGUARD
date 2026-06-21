#!/usr/bin/env python3
"""Aggregate ingested telemetry NDJSON into release reliability report."""

from __future__ import annotations

import json
import sys
from collections import defaultdict
from pathlib import Path


def main() -> None:
    src = Path(sys.argv[1] if len(sys.argv) > 1 else "telemetry-data")
    out = Path(sys.argv[2] if len(sys.argv) > 2 else "reports/reliability-summary.json")
    counters: dict[str, int] = defaultdict(int)
    installs: set[str] = set()
    batches = 0

    if src.is_dir():
        for f in sorted(src.glob("ingest-*.ndjson")):
            for line in f.read_text(encoding="utf-8").splitlines():
                if not line.strip():
                    continue
                batch = json.loads(line)
                batches += 1
                installs.add(batch.get("installId", ""))
                for m in batch.get("metrics", []):
                    key = m.get("name", "?")
                    dims = m.get("dims") or {}
                    dim_str = ",".join(f"{k}={v}" for k, v in sorted(dims.items()))
                    counters[f"{key}|{dim_str}"] += int(m.get("count", 0))

    update_ok = counters.get("update.apply|result=ok", 0)
    update_fail = counters.get("update.apply|result=fail", 0) + counters.get("update.apply|result=rollback", 0)
    total_apply = update_ok + update_fail

    report = {
        "schemaVersion": 1,
        "batches": batches,
        "uniqueInstalls": len(installs),
        "updateSuccessRate": round(100.0 * update_ok / total_apply, 2) if total_apply else None,
        "counters": dict(counters),
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Wrote {out}")


if __name__ == "__main__":
    main()
