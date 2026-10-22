# Clean Uninstallation & Rollback Guide

ShieldBlock guarantees **zero residue, zero orphaned drivers, and zero network disruption** upon removal.

---

## Clean Removal Procedure

1. Open PowerShell or Command Prompt as Administrator.
2. Execute the setup binary with the `--uninstall` flag:
   ```powershell
   Start-Process -FilePath ".\dist\ShieldBlock-Setup.exe" -ArgumentList "--uninstall" -Verb RunAs
   ```

### What the Uninstaller Does:
1. **Terminates Active Processes:** Cleanly halts `ShieldBlock.exe` and `ShieldBlockService.exe`.
2. **Purges WFP Filters:** Deletes all filters owned by Provider GUID `{5147424C-0001-4000-8000-536869656C64}`.
3. **Restores Adapter DNS:** Automatically restores all network adapters to their original snapshot DNS servers (or DHCP defaults).
4. **Removes Certificates:** Purges the local ShieldBlock Root CA from the Windows Certificate Store if optional HTTPS filtering was enabled.
5. **Flushes DNS Cache:** Calls `DnsFlushResolverCache` so Windows immediately resolves records through your default gateway/DHCP DNS.
6. **Verifies Connectivity:** Runs an end-to-end DNS and HTTP probe to confirm full Internet access before completing.

---

## Manual Emergency Reset (If Needed)

If you have already deleted the application folder and need an immediate reset:
1. Run `dist\ShieldBlockRecovery.exe`.
2. Or in an elevated PowerShell prompt, restore all adapters to DHCP:
   ```powershell
   Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | Set-DnsClientServerAddress -ResetServerAddresses
   Clear-DnsClientCache
   ```
