# Privacy Policy & Architecture

**ShieldBlock operates with an unconditional zero-telemetry guarantee.**

---

## 1. Zero Remote Telemetry

- **No Remote Logging:** ShieldBlock never transmits your DNS queries, visited domains, IP addresses, or device identifiers to any cloud server, third-party service, or developer endpoint.
- **No Analytics:** There are no embedded tracking SDKs, telemetry beacons, crash reporting libraries, or behavioral telemetry frameworks.
- **Local-Only Metrics:** All counters (ads blocked, queries filtered, latency) are computed in memory and stored locally in `%ProgramData%\ShieldBlock\data\stats.json`.

---

## 2. Upstream DNS Encryption (DNS-over-HTTPS)

When resolving unblocked domains, ShieldBlock forwards standard DNS queries over encrypted HTTPS (DoH) to trusted public privacy resolvers:
- **Cloudflare 1.1.1.1** (`https://cloudflare-dns.com/dns-query`)
- **Quad9 9.9.9.9** (`https://dns.quad9.net/dns-query`)

This ensures:
1. Your ISP (Internet Service Provider) cannot snoop on your DNS lookups.
2. Network eavesdroppers on public Wi-Fi cannot see the websites you visit.
3. Upstream queries are encrypted with TLS 1.3.

---

## 3. Storage Transparency

All files written by ShieldBlock are human-readable JSON or text files located in standard directories:
- `%ProgramData%\ShieldBlock\config\` — Application configuration
- `%ProgramData%\ShieldBlock\data\lists\` — Downloaded ABP blocklists
- `%ProgramData%\ShieldBlock\data\stats.json` — Aggregated query metrics
- `%ProgramData%\ShieldBlock\snapshots\` — Adapter DNS backup snapshots
- `%ProgramData%\ShieldBlock\logs\` — Local text diagnostic logs

You may inspect or delete these directories at any time.
