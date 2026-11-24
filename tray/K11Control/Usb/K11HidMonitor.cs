using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using K11Control.Config;
using Microsoft.Win32.SafeHandles;

namespace K11Control.Usb;

public sealed record K11HidCollectionInfo(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    ushort VersionNumber,
    ushort UsagePage,
    ushort Usage,
    ushort InputReportByteLength,
    ushort OutputReportByteLength,
    ushort FeatureReportByteLength
);

/// <summary>
/// Passive SetupAPI + HID interface client for FiiO K11 R2R (VID_2972 & PID_0099).
/// SAFETY: Never sends IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION or Str[>=7] requests,
/// and never writes unapproved payloads to COL01/COL02.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class K11HidMonitor
{
    public const ushort FiioVendorId = 0x2972;
    public const ushort K11R2RProductId = 0x0099;

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint FileShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;

    public IReadOnlyList<K11HidCollectionInfo> EnumerateCollections()
    {
        var results = new List<K11HidCollectionInfo>();
        HidD_GetHidGuid(out Guid hidGuid);

        IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (devInfo == new IntPtr(-1))
            return results;

        try
        {
            var ifaceData = new SpDeviceInterfaceData { cbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint index = 0; SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, index, ref ifaceData); index++)
            {
                string? path = GetInterfaceDevicePath(devInfo, ref ifaceData);
                if (string.IsNullOrEmpty(path))
                    continue;

                // Filter strictly by VID/PID path before opening so unrelated HID devices are untouched.
                if (!path.Contains("vid_2972", StringComparison.OrdinalIgnoreCase)
                    || !path.Contains("pid_0099", StringComparison.OrdinalIgnoreCase))
                    continue;

                using SafeFileHandle handle = CreateFile(
                    path,
                    0, // metadata-only query access
                    FileShareReadWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid)
                    continue;

                var attrs = new HiddAttributes { Size = (uint)Marshal.SizeOf<HiddAttributes>() };
                if (!HidD_GetAttributes(handle, ref attrs))
                    continue;
                if (attrs.VendorID != FiioVendorId || attrs.ProductID != K11R2RProductId)
                    continue;

                if (!HidD_GetPreparsedData(handle, out IntPtr preparsed))
                    continue;

                try
                {
                    if (HidP_GetCaps(preparsed, out HidpCaps caps) >= 0)
                    {
                        results.Add(new K11HidCollectionInfo(
                            path,
                            attrs.VendorID,
                            attrs.ProductID,
                            attrs.VersionNumber,
                            caps.UsagePage,
                            caps.Usage,
                            caps.InputReportByteLength,
                            caps.OutputReportByteLength,
                            caps.FeatureReportByteLength));
                    }
                }
                finally
                {
                    HidD_FreePreparsedData(preparsed);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfo);
        }

        return results;
    }

    /// <summary>
    /// Controlled Stage 0 single-shot probe on COL01 (UP=0x000C, Out=16B).
    /// Must only be invoked explicitly during Stage 0 diagnostics with user consent.
    /// </summary>
    public bool TrySendStage0Col01Probe(byte[] markerBytes, out string message, AppLanguage lang = AppLanguage.En)
    {
        bool ru = lang == AppLanguage.Ru;
        var col01 = EnumerateCollections().FirstOrDefault(c => c.UsagePage == 0x000C && c.OutputReportByteLength == 16);
        if (col01 is null)
        {
            message = ru
                ? "Интерфейс COL01 (UsagePage=0x000C, Out=16B) не найден."
                : "COL01 (UsagePage=0x000C, Out=16B) not found.";
            return false;
        }

        byte[] report = new byte[16];
        report[0] = 0x01; // COL01 Report ID 1
        Array.Copy(markerBytes, 0, report, 1, Math.Min(markerBytes.Length, 15));

        using SafeFileHandle handle = CreateFile(
            col01.DevicePath,
            GenericRead | GenericWrite,
            FileShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            string winErr = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            message = ru ? $"Ошибка открытия COL01: {winErr}" : $"Open COL01 failed: {winErr}";
            return false;
        }

        if (!WriteFile(handle, report, report.Length, out int written, IntPtr.Zero))
        {
            string winErr = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            message = ru ? $"Ошибка WriteFile(COL01, 16B): {winErr}" : $"WriteFile(COL01, 16B) failed: {winErr}";
            return false;
        }

        message = ru ? $"WriteFile(COL01): отправлено {written} байт." : $"WriteFile(COL01) sent {written} bytes.";
        return true;
    }

    /// <summary>
    /// Controlled Stage 0 single-shot Feature probe on COL02 (UP=0xFF00, Feat=65B).
    /// Must only be invoked explicitly during Stage 0 diagnostics with user consent.
    /// </summary>
    public bool TrySendStage0Col02FeatureProbe(byte[] markerBytes, out string message, AppLanguage lang = AppLanguage.En)
    {
        bool ru = lang == AppLanguage.Ru;
        var col02 = EnumerateCollections().FirstOrDefault(c => c.UsagePage == 0xFF00 && c.FeatureReportByteLength == 65);
        if (col02 is null)
        {
            message = ru
                ? "Интерфейс COL02 (UsagePage=0xFF00, Feat=65B) не найден."
                : "COL02 (UsagePage=0xFF00, Feat=65B) not found.";
            return false;
        }

        byte[] report = new byte[65];
        report[0] = 0x02; // COL02 Report ID 2
        Array.Copy(markerBytes, 0, report, 1, Math.Min(markerBytes.Length, 64));

        using SafeFileHandle handle = CreateFile(
            col02.DevicePath,
            GenericRead | GenericWrite,
            FileShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            string winErr = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            message = ru ? $"Ошибка открытия COL02: {winErr}" : $"Open COL02 failed: {winErr}";
            return false;
        }

        if (!HidD_SetFeature(handle, report, (uint)report.Length))
        {
            string winErr = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            message = ru ? $"Ошибка HidD_SetFeature(COL02, 65B): {winErr}" : $"HidD_SetFeature(COL02, 65B) failed: {winErr}";
            return false;
        }

        message = ru ? "HidD_SetFeature(COL02, 65B) выполнен успешно." : "HidD_SetFeature(COL02, 65B) succeeded.";
        return true;
    }

    /// <summary>Read-only COL01 HID input listener until cancelled.</summary>
    public async Task ListenCol01InputAsync(Action<byte[]> onReport, CancellationToken ct)
    {
        var col01 = EnumerateCollections().FirstOrDefault(c => c.UsagePage == 0x000C && c.InputReportByteLength > 0);
        if (col01 is null)
            return;

        using SafeFileHandle handle = CreateFile(
            col01.DevicePath,
            GenericRead,
            FileShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
            return;

        using var stream = new FileStream(handle, FileAccess.Read, col01.InputReportByteLength, isAsync: false);
        byte[] buf = new byte[col01.InputReportByteLength];
        while (!ct.IsCancellationRequested)
        {
            int n = await Task.Run(() => stream.Read(buf, 0, buf.Length), ct).ConfigureAwait(false);
            if (n <= 0)
                break;
            onReport(buf[..n]);
        }
    }

    private static string? GetInterfaceDevicePath(IntPtr devInfo, ref SpDeviceInterfaceData ifaceData)
    {
        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);
        if (requiredSize == 0)
            return null;

        IntPtr detailBuf = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA
            if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref ifaceData, detailBuf, requiredSize, out _, IntPtr.Zero))
                return null;

            return Marshal.PtrToStringAuto(detailBuf + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuf);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll", SetLastError = true)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten,
        IntPtr lpOverlapped);
}
