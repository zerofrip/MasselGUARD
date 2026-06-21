import type { AwgParams, ValidationIssue, WireGuardProfile } from '@mg-ui-core/types';

const AWG_KEYS: Record<string, keyof AwgParams> = {
  jc: 'jc',
  jmin: 'jmin',
  jmax: 'jmax',
  s1: 's1',
  s2: 's2',
  h1: 'h1',
  h2: 'h2',
  h3: 'h3',
  h4: 'h4',
};

export function isAwgProfile(profile: WireGuardProfile): boolean {
  const awg = profile.interface.awg;
  if (!awg) return false;
  return Object.values(awg).some((v) => v != null && String(v).trim() !== '');
}

export function buildConfigFromProfile(profile: WireGuardProfile): string {
  const lines: string[] = ['[Interface]'];
  const iface = profile.interface;
  if (iface.privateKey) lines.push(`PrivateKey = ${iface.privateKey.trim()}`);
  if (iface.address) lines.push(`Address = ${iface.address.trim()}`);
  if (iface.dns) lines.push(`DNS = ${iface.dns.trim()}`);
  if (iface.listenPort) lines.push(`ListenPort = ${iface.listenPort.trim()}`);
  if (iface.mtu) lines.push(`MTU = ${iface.mtu.trim()}`);

  if (iface.awg) {
    const awg = iface.awg;
    if (awg.jc) lines.push(`Jc = ${awg.jc.trim()}`);
    if (awg.jmin) lines.push(`Jmin = ${awg.jmin.trim()}`);
    if (awg.jmax) lines.push(`Jmax = ${awg.jmax.trim()}`);
    if (awg.s1) lines.push(`S1 = ${awg.s1.trim()}`);
    if (awg.s2) lines.push(`S2 = ${awg.s2.trim()}`);
    if (awg.h1) lines.push(`H1 = ${awg.h1.trim()}`);
    if (awg.h2) lines.push(`H2 = ${awg.h2.trim()}`);
    if (awg.h3) lines.push(`H3 = ${awg.h3.trim()}`);
    if (awg.h4) lines.push(`H4 = ${awg.h4.trim()}`);
  }

  if (iface.extra) {
    for (const [k, v] of Object.entries(iface.extra)) {
      if (v.trim()) lines.push(`${k} = ${v.trim()}`);
    }
  }

  for (const peer of profile.peers) {
    lines.push('');
    lines.push('[Peer]');
    if (peer.publicKey) lines.push(`PublicKey = ${peer.publicKey.trim()}`);
    if (peer.presharedKey) lines.push(`PresharedKey = ${peer.presharedKey.trim()}`);
    if (peer.endpoint) lines.push(`Endpoint = ${peer.endpoint.trim()}`);
    lines.push(`AllowedIPs = ${peer.allowedIPs?.trim() || '0.0.0.0/0, ::/0'}`);
    if (peer.persistentKeepalive) lines.push(`PersistentKeepalive = ${peer.persistentKeepalive.trim()}`);
  }

  return lines.join('\n') + '\n';
}

export function emptyProfile(): WireGuardProfile {
  return {
    interface: { privateKey: '', address: '', dns: '', listenPort: '', mtu: '', awg: {} },
    peers: [{ publicKey: '', presharedKey: '', endpoint: '', allowedIPs: '0.0.0.0/0, ::/0', persistentKeepalive: '25' }],
  };
}

export function parseProfileFromConfig(conf: string): WireGuardProfile {
  const profile = emptyProfile();
  let section = '';
  let peerIdx = 0;

  for (const rawLine of conf.split('\n')) {
    const line = rawLine.trim();
    if (line.startsWith('[') && line.endsWith(']')) {
      section = line.toLowerCase();
      if (section === '[peer]' && peerIdx > 0) {
        profile.peers.push({ publicKey: '', presharedKey: '', endpoint: '', allowedIPs: '', persistentKeepalive: '' });
        peerIdx = profile.peers.length - 1;
      } else if (section === '[peer]') {
        peerIdx = 0;
      }
      continue;
    }
    if (!line.includes('=') || line.startsWith('#')) continue;
    const eq = line.indexOf('=');
    const key = line.slice(0, eq).trim().toLowerCase();
    const val = line.slice(eq + 1).trim();
    const peer = profile.peers[peerIdx];

    if (section === '[interface]') {
      if (key === 'privatekey') profile.interface.privateKey = val;
      else if (key === 'address') profile.interface.address = val;
      else if (key === 'dns') profile.interface.dns = val;
      else if (key === 'listenport') profile.interface.listenPort = val;
      else if (key === 'mtu') profile.interface.mtu = val;
      else if (key in AWG_KEYS) {
        profile.interface.awg = profile.interface.awg ?? {};
        profile.interface.awg[AWG_KEYS[key]] = val;
      } else {
        profile.interface.extra = profile.interface.extra ?? {};
        profile.interface.extra[line.slice(0, eq).trim()] = val;
      }
    } else if (section === '[peer]' && peer) {
      if (key === 'publickey') peer.publicKey = val;
      else if (key === 'presharedkey') peer.presharedKey = val;
      else if (key === 'endpoint') peer.endpoint = val;
      else if (key === 'allowedips') peer.allowedIPs = val;
      else if (key === 'persistentkeepalive') peer.persistentKeepalive = val;
    }
  }

  return profile;
}

export function validateClient(name: string, config: string): ValidationIssue[] {
  const errors: ValidationIssue[] = [];
  if (!name.trim()) errors.push({ field: 'name', code: 'missing_name', message: 'Tunnel name is required.' });
  if (!config.trim()) errors.push({ field: 'config', code: 'empty_config', message: 'Config is empty.' });
  if (!config.includes('[Interface]')) errors.push({ field: 'config', code: 'invalid_config', message: 'Missing [Interface] section.' });
  if (!/PrivateKey\s*=/i.test(config)) errors.push({ field: 'interface.privateKey', code: 'invalid_key', message: 'Missing PrivateKey.' });
  if (!/PublicKey\s*=/i.test(config)) errors.push({ field: 'peer.publicKey', code: 'invalid_key', message: 'Missing PublicKey.' });
  if (!/Endpoint\s*=/i.test(config)) errors.push({ field: 'peer.endpoint', code: 'invalid_endpoint', message: 'Missing Endpoint.' });
  const ep = config.match(/Endpoint\s*=\s*(.+)/i)?.[1]?.trim();
  if (ep && !/^[^:]+:\d+$/.test(ep)) {
    errors.push({ field: 'peer.endpoint', code: 'invalid_endpoint', message: `Endpoint '${ep}' must be host:port.` });
  }
  const jc = config.match(/^Jc\s*=\s*(\d+)/im)?.[1];
  if (jc && Number(jc) > 0) {
    if (!/^Jmin\s*=/im.test(config) || !/^Jmax\s*=/im.test(config)) {
      errors.push({ field: 'interface.awg.jc', code: 'awg_jc', message: 'Jc > 0 requires Jmin and Jmax.' });
    }
  }
  return errors;
}

export function normalizeTunnelDetail(raw: Record<string, unknown>): {
  summary: import('@mg-ui-core/types').TunnelSummary;
  config: string | null;
  profile: WireGuardProfile | null;
} {
  const summary = (raw.summary ?? raw) as import('@mg-ui-core/types').TunnelSummary;
  const config = (raw.config as string) ?? null;
  const p = raw.profile as Record<string, unknown> | null;
  let profile: WireGuardProfile | null = null;
  if (p) {
    const iface = (p.interface ?? p.Interface) as Record<string, string> | undefined;
    const peersRaw = (p.peers ?? p.Peers) as Record<string, string>[] | undefined;
    profile = {
      interface: {
        privateKey: iface?.privateKey ?? iface?.PrivateKey ?? '',
        address: iface?.address ?? iface?.Address ?? '',
        dns: iface?.dns ?? iface?.Dns ?? '',
        listenPort: iface?.listenPort ?? iface?.ListenPort ?? '',
        mtu: iface?.mtu ?? iface?.Mtu ?? '',
      },
      peers: (peersRaw ?? []).map((peer) => ({
        publicKey: peer.publicKey ?? peer.PublicKey ?? '',
        presharedKey: peer.presharedKey ?? peer.PresharedKey ?? '',
        endpoint: peer.endpoint ?? peer.Endpoint ?? '',
        allowedIPs: peer.allowedIPs ?? peer.AllowedIPs ?? '',
        persistentKeepalive: peer.persistentKeepalive ?? peer.PersistentKeepalive ?? '',
      })),
    };
  } else if (config) {
    profile = parseProfileFromConfig(config);
  }
  return { summary, config, profile };
}
