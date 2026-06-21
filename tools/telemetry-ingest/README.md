# Telemetry ingest + ETL (Phase 13)

Optional server for opt-in anonymous telemetry batches.

## Run ingest locally

```bash
pip install -r requirements.txt  # none required (stdlib only)
python tools/telemetry-ingest/server.py
# POST JSON to http://localhost:8080/v1/events
```

Environment:

- `PORT` — default 8080
- `TELEMETRY_STORE` — output directory (default `telemetry-data/`)

## Aggregate nightly

```bash
python tools/telemetry-ingest/aggregate.py telemetry-data reports/reliability-summary.json
```

See `.github/workflows/telemetry-etl.yml`.
