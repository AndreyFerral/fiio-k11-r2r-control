using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace K11Control.Audio;

public readonly record struct VolumeStep(uint Channel, float LevelDb, int DelayAfterMs);

public sealed record K11AudioEndpointInfo(
    string DeviceId,
    string FriendlyName,
    string InterfaceName,
    float MasterDb,
    float MasterScalar,
    float Ch0Db,
    float Ch1Db,
    bool Muted,
    float MinDb,
    float MaxDb,
    float StepDb
);

/// <summary>
/// Passive OS-level CoreAudio client for FiiO K11 R2R UAC2 Feature Unit id=10.
/// Uses standard MMDevice / IAudioEndpointVolume COM APIs without sending raw USB EP0 descriptors.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CoreAudioVolumeService
{
    private static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidIAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly PropertyKey PkeyDeviceFriendlyName =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly PropertyKey PkeyDeviceInterfaceFriendlyName =
        new(new Guid("026E516E-B814-414B-83CD-856D6FEF4822"), 2);
    private static readonly PropertyKey PkeyDeviceInstanceId =
        new(new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 256);

    private const uint DeviceStateActive = 0x00000001;
    private const uint StgmRead = 0x00000000;
    private const uint ClsctxAll = 23;
    private readonly object _txLock = new();
    private int _txDepth;
    private long _lastTxEndTick = long.MinValue;

    public IDisposable BeginTransferBatch()
    {
        Interlocked.Increment(ref _txDepth);
        return new TransferBatchScope(this);
    }

    public bool IsTransferQuiet(int cooldownMs = 350)
    {
        if (Volatile.Read(ref _txDepth) > 0)
            return false;

        long endTick = Volatile.Read(ref _lastTxEndTick);
        return endTick == long.MinValue || (Environment.TickCount64 - endTick) >= cooldownMs;
    }

    private sealed class TransferBatchScope(CoreAudioVolumeService owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            Volatile.Write(ref owner._lastTxEndTick, Environment.TickCount64);
            Interlocked.Decrement(ref owner._txDepth);
        }
    }

    public static bool IsChannelImbalanced(float ch0Db, float ch1Db) =>
        float.IsFinite(ch0Db) && float.IsFinite(ch1Db) && MathF.Abs(ch0Db - ch1Db) >= 0.5f;

    public static float ComputeTargetBalancedDb(float ch0Db, float ch1Db, float minDb = -63f, float maxDb = 0f)
    {
        if (minDb > maxDb)
        {
            (minDb, maxDb) = (maxDb, minDb);
        }

        if (!float.IsFinite(ch0Db) || !float.IsFinite(ch1Db))
        {
            return Math.Clamp(0f, minDb, maxDb);
        }

        float rounded = MathF.Round(Math.Clamp(MathF.Max(ch0Db, ch1Db), minDb, maxDb));
        return MathF.Abs(rounded) < 0.001f ? 0f : rounded;
    }

    public bool TryBalanceChannels(out K11AudioEndpointInfo? updated, out string? error)
    {
        lock (_txLock)
        {
            string? capturedError = null;
            updated = WithK11Endpoint((id, friendly, iface, vol) =>
            {
                Marshal.ThrowExceptionForHR(vol.GetChannelCount(out uint chCount));
                if (chCount < 2)
                {
                    capturedError = "Endpoint has fewer than 2 channels";
                    return null;
                }

                Marshal.ThrowExceptionForHR(vol.GetVolumeRange(out float minDb, out float maxDb, out float stepDb));
                Marshal.ThrowExceptionForHR(vol.GetChannelVolumeLevel(0, out float ch0));
                Marshal.ThrowExceptionForHR(vol.GetChannelVolumeLevel(1, out float ch1));
                float targetDb = ComputeTargetBalancedDb(ch0, ch1, minDb, maxDb);

                Guid ctx = Guid.Empty;
                Marshal.ThrowExceptionForHR(vol.SetChannelVolumeLevel(0, targetDb, ref ctx));
                Marshal.ThrowExceptionForHR(vol.SetChannelVolumeLevel(1, targetDb, ref ctx));

                Marshal.ThrowExceptionForHR(vol.GetMasterVolumeLevel(out float masterDb));
                Marshal.ThrowExceptionForHR(vol.GetMasterVolumeLevelScalar(out float masterScalar));
                Marshal.ThrowExceptionForHR(vol.GetMute(out bool muted));
                Marshal.ThrowExceptionForHR(vol.GetChannelVolumeLevel(0, out float afterCh0));
                Marshal.ThrowExceptionForHR(vol.GetChannelVolumeLevel(1, out float afterCh1));

                return new K11AudioEndpointInfo(
                    id, friendly, iface, masterDb, masterScalar, afterCh0, afterCh1, muted, minDb, maxDb, stepDb);
            });

            error = updated is null ? (capturedError ?? "K11 R2R audio endpoint not available") : null;
            return updated is not null;
        }
    }

    public K11AudioEndpointInfo? TryGetEndpointInfo()
    {
        lock (_txLock)
            return ReadEndpointInfoCore();
    }

    public bool TryGetEndpointInfoIfIdle(out K11AudioEndpointInfo? info, int cooldownMs = 350)
    {
        if (!IsTransferQuiet(cooldownMs) || !Monitor.TryEnter(_txLock))
        {
            info = null;
            return false;
        }

        try
        {
            if (!IsTransferQuiet(cooldownMs))
            {
                info = null;
                return false;
            }

            info = ReadEndpointInfoCore();
            return true;
        }
        finally
        {
            Monitor.Exit(_txLock);
        }
    }

    private K11AudioEndpointInfo? ReadEndpointInfoCore()
    {
        return WithK11Endpoint((id, friendly, iface, vol) =>
        {
            Marshal.ThrowExceptionForHR(vol.GetChannelCount(out uint chCount));
            Marshal.ThrowExceptionForHR(vol.GetMasterVolumeLevel(out float masterDb));
            Marshal.ThrowExceptionForHR(vol.GetMasterVolumeLevelScalar(out float masterScalar));
            Marshal.ThrowExceptionForHR(vol.GetMute(out bool muted));
            Marshal.ThrowExceptionForHR(vol.GetVolumeRange(out float minDb, out float maxDb, out float stepDb));

            float ch0 = masterDb;
            float ch1 = masterDb;
            if (chCount >= 1) Marshal.ThrowExceptionForHR(vol.GetChannelVolumeLevel(0, out ch0));
            if (chCount >= 2) Marshal.ThrowExceptionForHR(vol.GetChannelVolumeLevel(1, out ch1));

            return new K11AudioEndpointInfo(
                id,
                friendly,
                iface,
                masterDb,
                masterScalar,
                ch0,
                ch1,
                muted,
                minDb,
                maxDb,
                stepDb);
        });
    }

    public bool TrySetChannelVolumeDb(uint channelIndex, float levelDb)
    {
        return WithK11Endpoint((_, _, _, vol) =>
        {
            Guid ctx = Guid.Empty;
            Marshal.ThrowExceptionForHR(vol.SetChannelVolumeLevel(channelIndex, levelDb, ref ctx));
            return true;
        });
    }

    public bool TrySetMasterVolumeDb(float levelDb)
    {
        return WithK11Endpoint((_, _, _, vol) =>
        {
            Guid ctx = Guid.Empty;
            Marshal.ThrowExceptionForHR(vol.SetMasterVolumeLevel(levelDb, ref ctx));
            return true;
        });
    }

    public bool TrySetMute(bool mute)
    {
        lock (_txLock)
        {
            return WithK11Endpoint((_, _, _, vol) =>
            {
                Guid ctx = Guid.Empty;
                Marshal.ThrowExceptionForHR(vol.SetMute(mute, ref ctx));
                return true;
            });
        }
    }

    public bool TryToggleMute(out bool nowMuted)
    {
        lock (_txLock)
        {
            bool confirmedState = false;
            bool ok = WithK11Endpoint((_, _, _, vol) =>
            {
                Marshal.ThrowExceptionForHR(vol.GetMute(out bool curMuted));
                bool next = !curMuted;
                Guid ctx = Guid.Empty;
                Marshal.ThrowExceptionForHR(vol.SetMute(next, ref ctx));
                Marshal.ThrowExceptionForHR(vol.GetMute(out confirmedState));
                return true;
            });
            nowMuted = confirmedState;
            return ok;
        }
    }

    public bool TryExecuteVolumeSequence(
        Func<float, float, IReadOnlyList<VolumeStep>> buildSteps,
        out string? error)
    {
        using var txScope = BeginTransferBatch();
        lock (_txLock)
        {
            try
            {
                string? localError = null;
                bool found = WithK11Endpoint((_, _, _, vol) =>
                {
                    Marshal.ThrowExceptionForHR(vol.GetChannelCount(out uint chCount));
                    if (chCount < 2)
                    {
                        localError = $"Expected >= 2 audio channels on K11 R2R, got {chCount}.";
                        return false;
                    }

                    Marshal.ThrowExceptionForHR(vol.GetChannelVolumeLevel(0, out float ch0));
                    Marshal.ThrowExceptionForHR(vol.GetChannelVolumeLevel(1, out float ch1));

                    IReadOnlyList<VolumeStep> steps = buildSteps(ch0, ch1);
                    Guid ctx = Guid.Empty;
                    bool completed = false;
                    try
                    {
                        foreach (VolumeStep step in steps)
                        {
                            Marshal.ThrowExceptionForHR(vol.SetChannelVolumeLevel(step.Channel, step.LevelDb, ref ctx));
                            if (step.DelayAfterMs > 0)
                                Thread.Sleep(step.DelayAfterMs);
                        }
                        completed = true;
                        return true;
                    }
                    finally
                    {
                        if (!completed)
                        {
                            try
                            {
                                _ = vol.SetChannelVolumeLevel(0, ch0, ref ctx);
                                _ = vol.SetChannelVolumeLevel(1, ch1, ref ctx);
                            }
                            catch { }
                        }
                    }
                });

                if (found)
                {
                    error = null;
                    return true;
                }

                error = localError ?? "K11 R2R active audio endpoint not found.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    private static T? WithK11Endpoint<T>(Func<string, string, string, IAudioEndpointVolume, T> action)
    {
        var enumType = Type.GetTypeFromCLSID(ClsidMmDeviceEnumerator)
            ?? throw new PlatformNotSupportedException("MMDeviceEnumerator CLSID unavailable");

        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumType)!;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0 /* eRender */, DeviceStateActive, out IMMDeviceCollection collection));
            try
            {
                Marshal.ThrowExceptionForHR(collection.GetCount(out uint count));
                for (uint i = 0; i < count; i++)
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out IMMDevice device));
                    try
                    {
                        Marshal.ThrowExceptionForHR(device.GetId(out string devId));
                        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(StgmRead, out IPropertyStore props));
                        try
                        {
                            string friendly = ReadPropString(props, PkeyDeviceFriendlyName);
                            string iface = ReadPropString(props, PkeyDeviceInterfaceFriendlyName);
                            string instanceId = ReadPropString(props, PkeyDeviceInstanceId);

                            if (!IsK11R2R(friendly, iface, instanceId))
                                continue;

                            var iid = IidIAudioEndpointVolume;
                            Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object rawVol));
                            try
                            {
                                return action(devId, friendly, iface, (IAudioEndpointVolume)rawVol);
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(rawVol);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(props);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }

        return default;
    }

    private static bool IsK11R2R(string friendly, string iface, string instanceId)
    {
        if (instanceId.Contains("VID_2972&PID_0099", StringComparison.OrdinalIgnoreCase))
            return true;
        return friendly.Contains("K11", StringComparison.OrdinalIgnoreCase)
            || iface.Contains("K11", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadPropString(IPropertyStore props, PropertyKey key)
    {
        var k = key;
        if (props.GetValue(ref k, out PropVariant pv) != 0)
            return string.Empty;

        try
        {
            if (pv.vt == 31 && pv.ptrVal != IntPtr.Zero) // VT_LPWSTR
                return Marshal.PtrToStringUni(pv.ptrVal) ?? string.Empty;
            return string.Empty;
        }
        finally
        {
            _ = PropVariantClear(ref pv);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;

        public PropertyKey(Guid fmtid, uint pid)
        {
            this.fmtid = fmtid;
            this.pid = pid;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr ptrVal;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant pv);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        [PreserveSig] int VolumeStepUp(ref Guid pguidEventContext);
        [PreserveSig] int VolumeStepDown(ref Guid pguidEventContext);
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }
}
