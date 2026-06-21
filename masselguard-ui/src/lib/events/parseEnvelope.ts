import type { NormalizedEventEnvelope } from '@mg-ui-core/types/events';

export type RawAgentEvent = {
  type: string;
  payload?: unknown;
  ts?: number | string | null;
  version?: number;
  seq?: number | null;
};

export function parseEnvelope(raw: RawAgentEvent): NormalizedEventEnvelope {
  const version = raw.version ?? (raw.seq != null ? 1 : 0);
  let ts: string | null = null;
  if (typeof raw.ts === 'string') ts = raw.ts;
  else if (typeof raw.ts === 'number') ts = new Date(raw.ts).toISOString();

  return {
    version,
    seq: raw.seq ?? null,
    type: raw.type,
    ts,
    payload: raw.payload ?? null,
  };
}

export function detectGap(lastSeq: number | null, incomingSeq: number | null): { from: number; to: number } | null {
  if (lastSeq == null || incomingSeq == null) return null;
  if (incomingSeq > lastSeq + 1) {
    return { from: lastSeq + 1, to: incomingSeq - 1 };
  }
  return null;
}
