using System;
using System.Runtime.InteropServices;

namespace ShieldBlock.Core.Wfp
{
    public static class WfpNative
    {
        public const string FwpuclntDll = "fwpuclnt.dll";

        public const uint RPC_C_AUTHN_DEFAULT = 0xFFFFFFFF;
        public const uint FWPM_SESSION_FLAG_DYNAMIC = 0x00000001;

        // Standard WFP Layer GUIDs
        // FWPM_LAYER_ALE_AUTH_CONNECT_V4
        public static readonly Guid FWPM_LAYER_ALE_AUTH_CONNECT_V4 = new Guid("c38d57d1-05a7-4c33-904f-7fbabee60e82");
        // FWPM_LAYER_ALE_RESOURCE_ASSIGNMENT_V4
        public static readonly Guid FWPM_LAYER_ALE_RESOURCE_ASSIGNMENT_V4 = new Guid("4b12b557-0a2b-426c-9494-b7be04bf1f7d");
        // FWPM_LAYER_OUTBOUND_TRANSPORT_V4
        public static readonly Guid FWPM_LAYER_OUTBOUND_TRANSPORT_V4 = new Guid("0a331139-4456-42ee-8e40-5e589839ebf8");

        // Action types
        public const uint FWP_ACTION_BLOCK = 0x00000001;
        public const uint FWP_ACTION_PERMIT = 0x00000002;
        public const uint FWP_ACTION_CALLOUT_TERMINATING = 0x00000003;

        [StructLayout(LayoutKind.Sequential)]
        public struct FWPM_DISPLAY_DATA0
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string name;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string description;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FWPM_SESSION0
        {
            public Guid sessionKey;
            public FWPM_DISPLAY_DATA0 displayData;
            public uint flags;
            public uint txnWaitTimeoutInMSec;
            public uint processId;
            public IntPtr sid;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string username;
            public bool kernelMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FWPM_PROVIDER0
        {
            public Guid providerKey;
            public FWPM_DISPLAY_DATA0 displayData;
            public uint flags;
            public uint providerDataSize;
            public IntPtr providerData;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string serviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FWPM_SUBLAYER0
        {
            public Guid subLayerKey;
            public FWPM_DISPLAY_DATA0 displayData;
            public uint flags;
            public IntPtr providerKey;
            public uint providerDataSize;
            public IntPtr providerData;
            public ushort weight;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FWPM_ACTION0
        {
            public uint type;
            public Guid filterType; // or calloutKey
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FWPM_FILTER0
        {
            public Guid filterKey;
            public FWPM_DISPLAY_DATA0 displayData;
            public uint flags;
            public IntPtr providerKey;
            public uint providerDataSize;
            public IntPtr providerData;
            public Guid layerKey;
            public Guid subLayerKey;
            public FWP_VALUE0 weight;
            public uint numFilterConditions;
            public IntPtr filterConditions;
            public FWPM_ACTION0 action;
            public ulong context;
            public IntPtr reserved;
            public ulong filterId;
            public FWP_VALUE0 effectiveWeight;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct FWP_VALUE0
        {
            [FieldOffset(0)]
            public uint type;
            [FieldOffset(8)]
            public uint uint32;
            [FieldOffset(8)]
            public ulong uint64;
        }

        [DllImport(FwpuclntDll, EntryPoint = "FwpmEngineOpen0", ExactSpelling = true)]
        public static extern uint FwpmEngineOpen0(
            [MarshalAs(UnmanagedType.LPWStr)] string? serverName,
            uint authnService,
            IntPtr authIdentity,
            in FWPM_SESSION0 session,
            out IntPtr engineHandle);

        [DllImport(FwpuclntDll, EntryPoint = "FwpmEngineClose0", ExactSpelling = true)]
        public static extern uint FwpmEngineClose0(IntPtr engineHandle);

        [DllImport(FwpuclntDll, EntryPoint = "FwpmProviderAdd0", ExactSpelling = true)]
        public static extern uint FwpmProviderAdd0(
            IntPtr engineHandle,
            in FWPM_PROVIDER0 provider,
            IntPtr sd);

        [DllImport(FwpuclntDll, EntryPoint = "FwpmProviderDeleteByKey0", ExactSpelling = true)]
        public static extern uint FwpmProviderDeleteByKey0(
            IntPtr engineHandle,
            in Guid key);

        [DllImport(FwpuclntDll, EntryPoint = "FwpmSubLayerAdd0", ExactSpelling = true)]
        public static extern uint FwpmSubLayerAdd0(
            IntPtr engineHandle,
            in FWPM_SUBLAYER0 subLayer,
            IntPtr sd);

        [DllImport(FwpuclntDll, EntryPoint = "FwpmSubLayerDeleteByKey0", ExactSpelling = true)]
        public static extern uint FwpmSubLayerDeleteByKey0(
            IntPtr engineHandle,
            in Guid key);

        [DllImport(FwpuclntDll, EntryPoint = "FwpmFilterAdd0", ExactSpelling = true)]
        public static extern uint FwpmFilterAdd0(
            IntPtr engineHandle,
            in FWPM_FILTER0 filter,
            IntPtr sd,
            out ulong id);

        [DllImport(FwpuclntDll, EntryPoint = "FwpmFilterDeleteById0", ExactSpelling = true)]
        public static extern uint FwpmFilterDeleteById0(
            IntPtr engineHandle,
            ulong id);
    }
}
