using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace PeripheralAssetTracker.Infrastructure.Discovery;

/// <summary>
/// Reads PnP device nodes through the Windows Configuration Manager API (CfgMgr32.dll), the same
/// supported API used by Device Manager and Get-PnpDevice. Read-only: it never changes device state,
/// never opens the devices and never reads input reports.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CfgMgrPnpNodeSource : IPnpNodeSource
{
    private const uint CrSuccess = 0x00;
    private const uint CrBufferSmall = 0x1A;
    private const uint CmGetIdListFilterPresent = 0x00000100;
    private const uint CmLocateDevNodeNormal = 0x00000000;

    private const uint DevPropTypeString = 0x12;
    private const uint DevPropTypeStringList = 0x2012;
    private const uint DevPropTypeGuid = 0x0D;

    private static readonly Guid DeviceBaseFmt = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    private static readonly DevPropKey DeviceDesc = new(DeviceBaseFmt, 2);
    private static readonly DevPropKey HardwareIds = new(DeviceBaseFmt, 3);
    private static readonly DevPropKey Service = new(DeviceBaseFmt, 6);
    private static readonly DevPropKey Class = new(DeviceBaseFmt, 9);
    private static readonly DevPropKey Manufacturer = new(DeviceBaseFmt, 13);
    private static readonly DevPropKey FriendlyName = new(DeviceBaseFmt, 14);
    private static readonly DevPropKey EnumeratorName = new(DeviceBaseFmt, 24);
    private static readonly DevPropKey BusReportedDeviceDesc = new(new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4);
    private static readonly DevPropKey ContainerId = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
    private static readonly DevPropKey Parent = new(new Guid("4340a6c5-93fa-4706-972c-7b648008a5a7"), 8);

    public IReadOnlyList<PnpNode> GetPresentNodes()
    {
        var nodes = new List<PnpNode>();
        foreach (var instanceId in GetPresentInstanceIds())
        {
            // Non-present devices cannot be located with CM_LOCATE_DEVNODE_NORMAL; they are skipped.
            if (CM_Locate_DevNodeW(out var devInst, instanceId, CmLocateDevNodeNormal) != CrSuccess)
            {
                continue;
            }

            nodes.Add(new PnpNode
            {
                InstanceId = instanceId,
                ParentInstanceId = ReadString(devInst, Parent),
                ClassName = ReadString(devInst, Class),
                EnumeratorName = ReadString(devInst, EnumeratorName),
                FriendlyName = ReadString(devInst, FriendlyName),
                Description = ReadString(devInst, DeviceDesc),
                Manufacturer = ReadString(devInst, Manufacturer),
                BusReportedDescription = ReadString(devInst, BusReportedDeviceDesc),
                ContainerId = ReadGuid(devInst, ContainerId),
                Service = ReadString(devInst, Service),
                HardwareIds = ReadStringList(devInst, HardwareIds)
            });
        }

        return nodes;
    }

    private static List<string> GetPresentInstanceIds()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var result = CM_Get_Device_ID_List_SizeW(out var length, null, CmGetIdListFilterPresent);
            if (result != CrSuccess)
            {
                throw new InvalidOperationException($"CM_Get_Device_ID_List_Size failed (CR 0x{result:X}).");
            }

            var buffer = new char[length];
            result = CM_Get_Device_ID_ListW(null, buffer, length, CmGetIdListFilterPresent);
            if (result == CrBufferSmall)
            {
                continue; // A device arrived between both calls; try again.
            }

            if (result != CrSuccess)
            {
                throw new InvalidOperationException($"CM_Get_Device_ID_List failed (CR 0x{result:X}).");
            }

            return SplitMultiString(buffer);
        }

        throw new InvalidOperationException("Device list kept changing while it was being read.");
    }

    private static byte[]? ReadRaw(uint devInst, DevPropKey key, uint expectedType)
    {
        uint size = 0;
        var result = CM_Get_DevNode_PropertyW(devInst, ref key, out var type, null, ref size, 0);
        if (result != CrBufferSmall || size == 0)
        {
            return null;
        }

        var buffer = new byte[size];
        result = CM_Get_DevNode_PropertyW(devInst, ref key, out type, buffer, ref size, 0);
        return result == CrSuccess && type == expectedType ? buffer : null;
    }

    private static string? ReadString(uint devInst, DevPropKey key)
    {
        var raw = ReadRaw(devInst, key, DevPropTypeString);
        if (raw is null)
        {
            return null;
        }

        var value = Encoding.Unicode.GetString(raw).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IReadOnlyList<string> ReadStringList(uint devInst, DevPropKey key)
    {
        var raw = ReadRaw(devInst, key, DevPropTypeStringList);
        return raw is null ? [] : SplitMultiString(Encoding.Unicode.GetString(raw).ToCharArray());
    }

    private static string? ReadGuid(uint devInst, DevPropKey key)
    {
        var raw = ReadRaw(devInst, key, DevPropTypeGuid);
        return raw is { Length: 16 } ? new Guid(raw).ToString("B").ToUpperInvariant() : null;
    }

    private static List<string> SplitMultiString(char[] buffer) =>
        new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey(Guid fmtid, uint pid)
    {
        public Guid FmtId = fmtid;
        public uint Pid = pid;
    }

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint pulLen, string? pszFilter, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_ListW(string? pszFilter, [Out] char[] buffer, uint bufferLen, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceId, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_DevNode_PropertyW(
        uint dnDevInst, ref DevPropKey propertyKey, out uint propertyType, [Out] byte[]? propertyBuffer,
        ref uint propertyBufferSize, uint ulFlags);
}
