export type ProfileSource = 'local' | 'companion' | 'imported' | 'managed';

export type TunnelSummary = {
  name: string;
  group: string;
  source: string;
  notes: string;
  active: boolean;
  killSwitch: boolean;
  autoReconnect: boolean;
  profileSource: ProfileSource;
  favorite: boolean;
  tags: string[];
  archived: boolean;
  lastUsedAt: string | null;
  connectionCount: number;
  endpointSummary: string | null;
  configEditable: boolean;
};

export type AwgParams = {
  jc?: string;
  jmin?: string;
  jmax?: string;
  s1?: string;
  s2?: string;
  h1?: string;
  h2?: string;
  h3?: string;
  h4?: string;
};

export type TunnelKind = 'standard' | 'awg';

export type WireGuardInterfaceSection = {
  privateKey: string;
  address: string;
  dns: string;
  listenPort: string;
  mtu: string;
  awg?: AwgParams;
  extra?: Record<string, string>;
};

export type WireGuardPeerSection = {
  publicKey: string;
  presharedKey: string;
  endpoint: string;
  allowedIPs: string;
  persistentKeepalive: string;
  extra?: Record<string, string>;
};

export type WireGuardProfile = {
  interface: WireGuardInterfaceSection;
  peers: WireGuardPeerSection[];
};

export type ValidationIssue = {
  field: string;
  code: string;
  message: string;
  detail?: string;
};

export type ValidationResult = {
  valid: boolean;
  errors: ValidationIssue[];
};

export type TunnelDetailResponse = {
  summary: TunnelSummary;
  config: string | null;
  profile: WireGuardProfile | null;
};

export type ExportMode = 'full' | 'sanitized' | 'qr';

export type TunnelStatus = {
  name: string;
  group: string;
  source: string;
  active: boolean;
  adapterUp: boolean;
  rxBytes: number;
  txBytes: number;
  connectedSince: string | null;
  killSwitch: boolean;
  autoReconnect: boolean;
  peerCount: number | null;
  lastHandshakeSecsAgo: number | null;
  rxRateBps?: number;
  txRateBps?: number;
};

export type TunnelStatusResponse = {
  activeCount: number;
  primary: TunnelStatus | null;
  tunnels: TunnelStatus[];
};

export type WifiSnapshot = {
  ssid: string | null;
  isOpen: boolean;
  manualMode: boolean;
};

export type WifiRule = {
  name: string;
  ssid: string;
  tunnel: string;
  networkType: string;
  executionCount: number;
};

export type WifiRulesResponse = {
  rules: WifiRule[];
  defaultAction: string;
  defaultTunnel: string;
  openWifiTunnel: string;
  manualMode: boolean;
};

export type AgentInfo = {
  version: string;
  codename: string;
  uptimeSecs: number;
  pid: number;
};

export type AppConfig = {
  language: string;
  logLevelSetting: string;
  startWithWindows: boolean;
  autoReconnectMode: string;
  killSwitchMode: string;
  wireGuardInstallDirectory: string;
  mode: string;
  systemThemeMode: string;
  activeTheme: string;
  storeConnectionHistory: boolean;
  storeWifiHistory: boolean;
  showTimeline: boolean;
  showWifiInChart: boolean;
  splitTunnel: SplitTunnelRules;
  networkLock: NetworkLockConfig;
  routeGuardInstallPath?: string | null;
  networkLockWfpDelegation?: boolean;
  eventRingSize?: number;
  crashReportingEnabled?: boolean;
  supportExportConsentAt?: string | null;
  supportExportIncludeCrashes?: boolean;
  telemetryEnabled?: boolean;
  telemetryConsentAt?: string | null;
  telemetryUploadUrl?: string | null;
};

export type SplitRouteTarget = 'vpn' | 'direct';

export type AppSplitRule = {
  id: string;
  appPath: string;
  route: SplitRouteTarget;
  enabled: boolean;
};

export type IpSplitRule = {
  id: string;
  cidr: string;
  route: SplitRouteTarget;
  enabled: boolean;
};

export type DomainSplitRule = {
  id: string;
  pattern: string;
  route: SplitRouteTarget;
  enabled: boolean;
};

export type SplitTunnelRules = {
  appRules: AppSplitRule[];
  ipRules: IpSplitRule[];
  domainRules: DomainSplitRule[];
  useRouteGuardBridge: boolean;
};

export type NetworkLockMode = 'disabled' | 'auto' | 'alwaysOn';
export type DnsPolicy = 'strict' | 'allow_exceptions' | 'allow_dhcp';

export type NetworkLockConfig = {
  /** @deprecated migrated to mode — omitted from agent responses */
  enabled?: boolean;
  mode?: NetworkLockMode;
  lanAccessEnabled?: boolean;
  lanExceptions: string[];
  dnsPolicy?: DnsPolicy;
  dnsExceptions: string[];
  allowDhcp?: boolean;
};

export type NetworkLockDiagnostics = {
  activeFilterCount: number;
  ruleNames: string[];
  globalBlockActive: boolean;
  leakProtection: string;
  lastPolicyHash?: string | null;
  recoveryState: string;
};

export type NetworkLockStatus = {
  mode: NetworkLockMode;
  enforcementActive: boolean;
  lanAccess: { enabled: boolean; exceptions: string[] };
  dnsPolicy: { policy: DnsPolicy; exceptions: string[]; allowDhcp: boolean };
  activeTunnels: string[];
  diagnostics: NetworkLockDiagnostics;
  lastRecovery: { at: string; reason?: string } | null;
  config: NetworkLockConfig;
};

export type RouteGuardAvailability = 'absent' | 'installed' | 'running';

export type RouteGuardStatus = {
  availability: RouteGuardAvailability;
  pipe: string;
  installPath?: string;
  remote?: {
    schemaVersion?: number;
    features?: Record<string, boolean>;
    limits?: { maxAppRules?: number };
    routingModes?: string[];
  };
  negotiated?: {
    appSplitTunnel?: boolean;
    ipRouting?: boolean;
    domainRouting?: boolean;
    calloutDriver?: boolean;
    awg?: boolean;
    transport?: boolean;
    phantun?: boolean;
    lwo?: boolean;
    networkLockDelegation?: boolean;
    observability?: boolean;
    diagnosticsExport?: boolean;
    metricsHistory?: boolean;
  };
  health?: {
    score?: number;
    status?: string;
  };
  observability?: Record<string, unknown> | null;
  domain?: {
    rules?: number;
    resolvedIps?: number;
    effective?: boolean;
    kernelRedirect?: boolean;
    driverPresent?: boolean;
    redirectStats?: {
      redirectedV4?: number;
      redirectedV6?: number;
      redirectedTcpV4?: number;
      redirectedTcpV6?: number;
    };
  };
  bridge?: {
    schemaVersion?: number;
    lastSyncAt?: string | null;
    lastSyncError?: string | null;
    lastPolicyHash?: string | null;
    eventBridgeReady?: boolean;
    lastEventId?: number;
    lastDomainSync?: {
      rules?: number;
      resolvedIps?: number;
      routes?: number;
      effective?: boolean;
    };
  };
};

export type HistoryEntry = {
  tunnelName?: string;
  ssid?: string;
  connectedAt: string;
  disconnectedAt: string | null;
  source?: string;
  sessionRxBytes?: number;
  sessionTxBytes?: number;
  isOpen?: boolean;
  endpoint?: string | null;
  failureReason?: string | null;
};

export type TunnelStatsEventPayload = {
  name: string;
  rxBytes: number;
  txBytes: number;
  rxRate?: number;
  txRate?: number;
  adapterUp?: boolean;
};

export type TunnelHandshakeEventPayload = {
  name: string;
  peerCount: number;
  lastHandshakeSecsAgo: number | null;
};

export type AgentSnapshotPayload = {
  activeCount: number;
  primary: TunnelStatus | null;
  tunnels: TunnelStatus[];
  wifi: WifiSnapshot;
  networkAvailable: boolean;
  meta?: { seq: number; eventCount: number; ringCapacity: number };
};

export type AgentEventEnvelope = {
  type: string;
  payload: unknown;
  ts?: number | string;
  version?: number;
  seq?: number;
};

export * from './events';
