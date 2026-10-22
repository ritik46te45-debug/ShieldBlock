# Security Policy & Invariant Architecture

ShieldBlock is engineered from the ground up to prevent system damage, network blackouts, or security degradation.

---

## 1. Absolute Security Invariants

The `ThreatModel` subsystem enforces nine non-negotiable invariants directly in source code:

1. **Zero Arbitrary Registry Writes:**
   ShieldBlock never writes to arbitrary Windows Registry keys. All application configurations, filter lists, snapshots, and metrics are stored strictly in atomic JSON stores under `%ProgramData%\ShieldBlock\` and `%AppData%\ShieldBlock\`.
2. **Fail-Open Internet Access:**
   Under component failure, service crash, or unexpected termination, normal internet access must continue uninterrupted. No persistent blocking states are allowed without active process supervision.
3. **No Kernel Drivers:**
   ShieldBlock runs strictly in user mode. It does not load `.sys` files, kernel hooks, or NDIS/WFP kernel drivers. This completely eliminates the possibility of Blue Screens of Death (BSODs).
4. **Windows Defender Untouched:**
   ShieldBlock never disables, weakens, or modifies Windows Defender Antivirus, Microsoft Defender SmartScreen, or real-time protection.
5. **Windows Firewall Untouched:**
   ShieldBlock never resets, overrides, or disables Windows Firewall (`MpsSvc`).
6. **Strict WFP Filter Ownership:**
   All WFP filters registered by ShieldBlock must be prefixed with `ShieldBlock-` and registered under the ShieldBlock Provider GUID:
   `{5147424C-0001-4000-8000-536869656C64}`. ShieldBlock will refuse to modify or delete any filter outside its namespace.
7. **Atomic Configuration Rollback:**
   Before any network adapter DNS setting is adjusted, an immutable snapshot of all operational adapters is captured to `last_known_good.json`.
8. **Local-Only IPC Security:**
   The Named Pipe `\\.\pipe\ShieldBlock_IPC` uses Windows access control lists (ACLs) restricted to the local machine, preventing remote access.
9. **Zero Remote Telemetry:**
   ShieldBlock does not send query telemetry, metrics, or browsing habits to any external servers.

---

## 2. Threat Model Validation & Banned Commands

The internal validator (`ThreatModelValidator`) intercepts and throws `SecurityAuditViolation` if any component attempts destructive commands:

- ❌ `netsh int ip reset` — Prohibited (destroys adapter configurations)
- ❌ `netsh winsock reset` — Prohibited (destroys catalog state)
- ❌ `netsh advfirewall reset` — Prohibited (destroys firewall rules)
- ❌ `reg delete ...` — Prohibited
- ❌ `sc delete mpssvc` / `sc config windefend` — Prohibited
- ❌ `format.com`, `diskpart.exe`, `bcdedit.exe` — Prohibited

---

## 3. Dynamic WFP Session Isolation

When connecting to the Windows Base Filtering Engine (BFE) via `FwpmEngineOpen0`, ShieldBlock sets the mandatory flag:
```c
session.flags = FWPM_SESSION_FLAG_DYNAMIC;
```
**Fail-Safe Mechanism:**
If `ShieldBlockService.exe` crashes or is forcefully terminated by Task Manager, the Windows BFE kernel driver automatically cleans up all associated filters, instantly restoring normal traffic flow.

---

## 4. HTTPS Filtering & Strict Bypass Protections

When optional HTTPS filtering is enabled by the user:
- A local Root CA is generated exclusively on the machine and stored with local machine entropy.
- **Strict Permanent Bypass List:**
  Traffic for banking, financial, government, and certificate-pinned services is **never intercepted or decrypted**:
  - Banking: `chase.com`, `bankofamerica.com`, `wellsfargo.com`, `paypal.com`, `stripe.com`, `fidelity.com`, `schwab.com`, etc.
  - Government: All `.gov` and `.mil` domains, `login.gov`, `irs.gov`, `medicare.gov`.
  - Pinned Apps: Discord, Steam, Epic Games, WhatsApp, Spotify, Signal, Windows Update.
  - Defender & Updates: `smartscreen.microsoft.com`, `windowsupdate.microsoft.com`, `definitionupdates.microsoft.com`.
- On disable or uninstall, the Root CA is automatically removed from the Windows Certificate Store.
