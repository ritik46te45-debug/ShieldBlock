# ShieldBlock Technical Architecture

---

## High-Level Architecture Overview

ShieldBlock is built in C# targeting .NET 8 (with native roll-forward support for modern Windows environments) using a modular user-mode architecture:

```
[ ShieldBlock.UI (Windows Forms Dark GUI) ]
                    ▲
                    │ IPC (Named Pipe: \\.\pipe\ShieldBlock_IPC)
                    ▼
[ ShieldBlock.Service (Windows Service Host) ]
  ├── [ RuleEngine & AbpRuleParser ] (Exact/Suffix hash indices, L1 cache)
  ├── [ DnsProxyServer (127.0.0.53:53) ] (Async UDP/TCP, Wire parser)
  │     ├── [ DnsCache ] (TTL bounded, Transaction ID rewrite)
  │     └── [ DohClient ] (HTTP/2 Cloudflare 1.1.1.1 & Quad9 9.9.9.9)
  ├── [ WfpManager ] (User-mode fwpuclnt.dll, Dynamic sessions)
  ├── [ FilterListManager ] (Atomic downloader, SHA-256 verifier)
  ├── [ AppConnectionTracker ] (iphlpapi GetExtendedTcpTable PID map)
  ├── [ CertificateManager & HttpsMitmProxy ] (Optional, strict bypass)
  ├── [ StatsManager ] (Interlocked lock-free counters, ring buffer)
  └── [ FailOpenWatchdog & RollbackEngine ] (Crash tracking & recovery)
```

---

## 1. Rule Engine & Matching Mechanics

1. **Parser:** Single-pass streaming parser (`AbpRuleParser`) converts Adblock Plus (ABP) and uBlock Origin syntax into `FilterRule` models:
   - `||domain.com^` -> `RuleKind.DomainBlock`
   - `@@||domain.com^` -> `RuleKind.DomainAllow`
   - `domain.com##.ad-banner` -> `RuleKind.CosmeticElement`
2. **Indexing:**
   - Hash maps index domains for exact and subdomain suffix lookups.
   - For a query like `sub.tracker.ads.co.uk`, the engine looks up `sub.tracker.ads.co.uk`, then `tracker.ads.co.uk`, then `ads.co.uk`.
3. **Allowlist Precedence:** Exception rules (`@@||`) are checked before block rules.
4. **L1 Cache:** A `ConcurrentDictionary` hot cache holds up to 50,000 recent domain resolutions. Benchmarked at **> 125,000 evaluations per second** with average latency of ~6–18 µs per query.

---

## 2. Fail-Open DNS Proxy Server

- **Listeners:** Async UDP socket and TCP listener bound to `127.0.0.53:53` (with `127.0.0.1` loopback fallback).
- **Wire Parsing:** `DnsWireParser` decodes query headers, flags, and QNAME with DNS compression pointer traversal.
- **Decision Path:**
  - If **Blocked:** Generates DNS A response with `0.0.0.0` (or AAAA `::`) with TTL=300.
  - If **Allowed:** Checks `DnsCache`. If hit, rewrites the 2-byte Transaction ID and returns immediately. If miss, queries upstream DoH via `DohClient` and caches response.
  - If **Upstream Times Out (>1.2s):** Automatically falls back to direct UDP query to `1.1.1.1:53` or returns NXDOMAIN to guarantee that client applications never hang.

---

## 3. Dynamic Windows Filtering Platform (WFP) Sessions

ShieldBlock interacts with the Windows Base Filtering Engine (`BFE`) in user mode via P/Invoke to `fwpuclnt.dll`:
- **Dynamic Session:** Opened with `FWPM_SESSION_FLAG_DYNAMIC`. The Windows kernel guarantees that if the process dies, all registered filters and sublayers are immediately purged.
- **Provider & Sublayer:**
  - Provider GUID: `{5147424C-0001-4000-8000-536869656C64}`
  - Sublayer GUID: `{5147424C-0002-4000-8000-536869656C64}`
- **Filter Ownership:** The `ThreatModel` strictly verifies that every filter name begins with `ShieldBlock-` and belongs to the ShieldBlock Provider GUID before creation or deletion.

---

## 4. Crash Watchdog & Network Snapshot Recovery

1. **Atomic Snapshotting:** `SnapshotManager` captures adapter names, descriptions, IPv4/IPv6 addresses, DHCP states, and DNS server addresses into timestamped JSON files under `%ProgramData%\ShieldBlock\snapshots\` before any setting is altered.
2. **FailOpenWatchdog:**
   - Writes startup record on boot and clears on graceful shutdown.
   - If 3 consecutive unclean shutdowns are detected, the watchdog enters **Safe Mode**, immediately triggering `RollbackEngine.RollbackAllAsync()` to restore original adapter configurations.
