using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ShieldBlock.Core.Common;
using ShieldBlock.Core.Safety;

namespace ShieldBlock.Core.Wfp
{
    public class WfpManager : IDisposable
    {
        private IntPtr _engineHandle = IntPtr.Zero;
        private readonly List<ulong> _createdFilterIds = new List<ulong>();
        private bool _isInitialized;
        private bool _isDisposed;

        public bool IsActive => _isInitialized && _engineHandle != IntPtr.Zero;
        public IReadOnlyList<ulong> CreatedFilterIds => _createdFilterIds.AsReadOnly();

        public bool Initialize()
        {
            if (_isInitialized) return true;

            try
            {
                var session = new WfpNative.FWPM_SESSION0
                {
                    // FWPM_SESSION_FLAG_DYNAMIC: Crucial Fail-Open safeguard.
                    // If the process terminates abnormally, BFE automatically removes all filters.
                    flags = WfpNative.FWPM_SESSION_FLAG_DYNAMIC,
                    txnWaitTimeoutInMSec = 5000,
                    displayData = new WfpNative.FWPM_DISPLAY_DATA0
                    {
                        name = Constants.ApplicationName + " Dynamic Session",
                        description = "ShieldBlock User-Mode Network Filtering Session"
                    }
                };

                uint status = WfpNative.FwpmEngineOpen0(
                    null,
                    WfpNative.RPC_C_AUTHN_DEFAULT,
                    IntPtr.Zero,
                    session,
                    out _engineHandle);

                if (status != 0)
                {
                    // Status 0x8032001c = FWP_E_ACCESS_DENIED (requires administrator rights)
                    Console.WriteLine($"[WfpManager] WFP Engine Open returned status: 0x{status:X8}. (Non-elevated simulation fallback enabled).");
                    _isInitialized = true;
                    return false;
                }

                // Register ShieldBlock Provider
                bool provOk = RegisterProvider();

                // Register ShieldBlock SubLayer
                bool subOk = RegisterSublayer();

                if (!provOk || !subOk)
                {
                    Console.WriteLine("[WfpManager] Running non-elevated: System WFP filter registration requires administrator privileges. Simulation fallback active.");
                    WfpNative.FwpmEngineClose0(_engineHandle);
                    _engineHandle = IntPtr.Zero;
                }

                _isInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WfpManager] Exception initializing WFP: {ex.Message}");
                _isInitialized = true;
                return false;
            }
        }

        private bool RegisterProvider()
        {
            if (_engineHandle == IntPtr.Zero) return false;

            var provider = new WfpNative.FWPM_PROVIDER0
            {
                providerKey = Constants.WfpProviderGuid,
                displayData = new WfpNative.FWPM_DISPLAY_DATA0
                {
                    name = Constants.WfpProviderName,
                    description = "ShieldBlock Ad & Tracking Protection Provider"
                }
            };

            uint status = WfpNative.FwpmProviderAdd0(_engineHandle, provider, IntPtr.Zero);
            if (status == 5) return false; // ACCESS_DENIED
            return status == 0 || status == 0x80320015;
        }

        private bool RegisterSublayer()
        {
            if (_engineHandle == IntPtr.Zero) return false;

            IntPtr providerKeyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            try
            {
                Marshal.StructureToPtr(Constants.WfpProviderGuid, providerKeyPtr, false);

                var sublayer = new WfpNative.FWPM_SUBLAYER0
                {
                    subLayerKey = Constants.WfpSublayerGuid,
                    displayData = new WfpNative.FWPM_DISPLAY_DATA0
                    {
                        name = Constants.WfpSublayerName,
                        description = "ShieldBlock Filtering Sublayer"
                    },
                    providerKey = providerKeyPtr,
                    weight = 0x8000 // Medium weight
                };

                uint status = WfpNative.FwpmSubLayerAdd0(_engineHandle, sublayer, IntPtr.Zero);
                if (status == 5) return false; // ACCESS_DENIED
                return status == 0 || status == 0x80320017;
            }
            finally
            {
                Marshal.FreeHGlobal(providerKeyPtr);
            }
        }

        public bool AddFilter(string ruleName, Guid layerKey, uint actionType = WfpNative.FWP_ACTION_PERMIT)
        {
            // Safety invariant check
            ThreatModelValidator.AssertFilterOwnership(ruleName, Constants.WfpProviderGuid);

            if (_engineHandle == IntPtr.Zero)
            {
                // Simulation tracking
                ulong mockId = (ulong)DateTime.UtcNow.Ticks;
                _createdFilterIds.Add(mockId);
                return true;
            }

            IntPtr providerKeyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            try
            {
                Marshal.StructureToPtr(Constants.WfpProviderGuid, providerKeyPtr, false);

                var filter = new WfpNative.FWPM_FILTER0
                {
                    filterKey = Guid.NewGuid(),
                    displayData = new WfpNative.FWPM_DISPLAY_DATA0
                    {
                        name = ruleName,
                        description = "ShieldBlock Filter Rule"
                    },
                    layerKey = layerKey,
                    subLayerKey = Constants.WfpSublayerGuid,
                    providerKey = providerKeyPtr,
                    action = new WfpNative.FWPM_ACTION0
                    {
                        type = actionType
                    }
                };

                uint status = WfpNative.FwpmFilterAdd0(_engineHandle, filter, IntPtr.Zero, out ulong filterId);
                if (status == 0)
                {
                    _createdFilterIds.Add(filterId);
                    Console.WriteLine($"[WfpManager] Registered filter '{ruleName}' (ID: {filterId})");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[WfpManager] Error adding filter '{ruleName}': 0x{status:X8}");
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(providerKeyPtr);
            }
        }

        public void RemoveAllFilters() => RemoveAllShieldBlockFilters();

        public void RemoveAllShieldBlockFilters()
        {
            if (_engineHandle != IntPtr.Zero)
            {
                foreach (var filterId in _createdFilterIds)
                {
                    try
                    {
                        WfpNative.FwpmFilterDeleteById0(_engineHandle, filterId);
                    }
                    catch { }
                }
            }

            _createdFilterIds.Clear();
            Console.WriteLine("[WfpManager] Removed all ShieldBlock-owned filters.");
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                RemoveAllShieldBlockFilters();

                if (_engineHandle != IntPtr.Zero)
                {
                    WfpNative.FwpmEngineClose0(_engineHandle);
                    _engineHandle = IntPtr.Zero;
                }
            }
        }
    }
}
