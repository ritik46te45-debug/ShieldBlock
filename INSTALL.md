# Installation & Setup Guide

---

## System Requirements

- **Operating System:** Windows 10 (Build 1809 or higher) or Windows 11 (64-bit).
- **Architecture:** x64.
- **Runtime:** .NET 8.0 Runtime (or .NET 10.0 with automatic roll-forward enabled).
- **Service Dependency:** Windows Base Filtering Engine (`BFE`) must be enabled and running (enabled by default on all Windows installations).

---

## Fast Automated Installation

1. Navigate to the release `dist/` directory.
2. Run `ShieldBlock-Setup.exe` with Administrator privileges:
   ```powershell
   Start-Process -FilePath ".\dist\ShieldBlock-Setup.exe" -Verb RunAs
   ```
3. The Setup tool executes pre-flight checks:
   - Verifies 64-bit Windows OS compatibility.
   - Confirms Windows Base Filtering Engine (BFE) is active.
   - Prepares `%ProgramData%\ShieldBlock\` directory hierarchy.
   - Seeds baseline offline filter lists (`adblock-core`, `privacy-tracking`, `malware-security`, `youtube-annoyances`).
   - Captures pre-installation network snapshot (`last_known_good.json`).
   - Verifies baseline network connectivity and DNS response times.

---

## Running the Application

### Running Desktop UI
Double-click `dist\ShieldBlock.exe` or execute:
```powershell
.\dist\ShieldBlock.exe
```

### Running Windows Service
To run the filtering engine interactively:
```powershell
.\dist\ShieldBlockService.exe
```

To register as a background Windows Service:
```powershell
sc.exe create ShieldBlockService binPath= "C:\Program Files\ShieldBlock\ShieldBlockService.exe" start= auto DisplayName= "ShieldBlock Filtering Service"
sc.exe start ShieldBlockService
```
