import { invoke } from '@tauri-apps/api/core';
import type { EventReplayResponse, ExtendedStreamHealth, NormalizedEventEnvelope } from '@mg-ui-core/types/events';
import { detectGap, parseEnvelope, type RawAgentEvent } from './parseEnvelope';
import { loadLastSeq, saveLastSeq } from './persistCursor';

const MAX_REPLAY_ATTEMPTS = 2;

export class EventStreamManager {
  private lastSeq: number | null = loadLastSeq();
  private gapReplayAttempts = 0;
  private onEvent: (evt: NormalizedEventEnvelope) => void;
  private onGap: (from: number, to: number) => void;

  constructor(
    onEvent: (evt: NormalizedEventEnvelope) => void,
    onGap?: (from: number, to: number) => void,
  ) {
    this.onEvent = onEvent;
    this.onGap = onGap ?? (() => {});
  }

  getLastSeq() {
    return this.lastSeq;
  }

  async initCursor() {
    const stored = loadLastSeq();
    if (stored != null) {
      this.lastSeq = stored;
      try {
        await invoke('event_stream_report_seq', { seq: stored });
      } catch {
        /* non-tauri dev */
      }
    }
  }

  handleRawEvent(raw: RawAgentEvent) {
    const evt = parseEnvelope(raw);
    if (evt.seq != null) {
      const gap = detectGap(this.lastSeq, evt.seq);
      if (gap) {
        this.onGap(gap.from, gap.to);
        void this.recoverGap(gap.from, gap.to);
      }
      this.lastSeq = evt.seq;
      saveLastSeq(evt.seq);
      void invoke('event_stream_report_seq', { seq: evt.seq }).catch(() => {});
    }
    this.onEvent(evt);
  }

  private async recoverGap(from: number, to: number) {
    if (this.gapReplayAttempts >= MAX_REPLAY_ATTEMPTS) return;
    this.gapReplayAttempts++;
    const sinceSeq = Math.max(0, from - 1);
    try {
      const res = await invoke<EventReplayResponse>('agent_event_replay', {
        sinceSeq,
        limit: Math.min(512, to - from + 10),
      });
      for (const evt of res.events) {
        if (evt.seq != null && (this.lastSeq == null || evt.seq > this.lastSeq)) {
          this.lastSeq = evt.seq;
        }
        this.onEvent(evt);
      }
      if (res.latestSeq > (this.lastSeq ?? 0)) {
        this.lastSeq = res.latestSeq;
        saveLastSeq(res.latestSeq);
      }
    } catch {
      /* fallback polling handles persistent gaps */
    }
  }

  resetGapAttempts() {
    this.gapReplayAttempts = 0;
  }

  async pollHealth(): Promise<ExtendedStreamHealth | null> {
    try {
      return await invoke<ExtendedStreamHealth>('event_stream_status');
    } catch {
      return null;
    }
  }
}
