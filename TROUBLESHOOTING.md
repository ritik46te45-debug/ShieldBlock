# Troubleshooting & Diagnostics

---

## 1. Quick Emergency Recovery

If you ever lose Internet access or suspect an issue with filtering:

### Option A: UI Emergency Button
Click the **EMERGENCY DISABLE** button located in the top-right header of the ShieldBlock window.

### Option B: Keyboard Shortcut
Press **`Ctrl + Shift + F12`** from any application in Windows. This triggers immediate filter detachment and adapter rollback.

### Option C: Standalone Rescue Utility
Run `ShieldBlockRecovery.exe` from `dist\`:
```powershell
.\dist\ShieldBlockRecovery.exe
```
This utility has zero external dependencies, works even if the main service is dead, resets all adapter DNS to DHCP, flushes Windows resolver cache, and verifies connectivity.

---

## 2. Common Scenarios

### Captive Portals (Hotels, Airports, Cafes)
When connecting to Wi-Fi networks requiring a login page:
1. ShieldBlock includes captive portal detection in `ConnectivityTester`.
2. If a captive portal is detected, click **Pause Protection (5m)** in the system tray.
3. Log in through the browser portal.
4. ShieldBlock will resume filtering after the pause expires.

### Safe Mode Activation
If ShieldBlock detects 3 consecutive unclean shutdowns (e.g. system crashes or power failures during startup), the **FailOpenWatchdog** enters **Safe Mode**:
- Normal filtering is paused.
- Network adapters remain on default DHCP settings.
- To reset Safe Mode: Open ShieldBlock UI -> Diagnostics -> Click **Reset Safe Mode & Re-enable**.

### Administrator Privileges Notice
- ShieldBlock Desktop UI can run without elevation to monitor status and adjust user preferences.
- Changing system network adapter DNS or registering live WFP filters requires Administrator privileges. If run non-elevated, ShieldBlock gracefully operates in **Simulation Fallback Mode** where queries are analyzed and logged without dropping traffic.
