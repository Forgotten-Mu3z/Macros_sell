using System.Runtime.InteropServices;

namespace EditInput.Windows.Native;

/// <summary>XInput 1.4 (Windows 8+) bindings, including the undocumented VID/PID capabilities export.</summary>
internal static class XInput
{
    public const int MaxControllers = 4;
    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_DEVICE_NOT_CONNECTED = 1167;
    public const uint XINPUT_FLAG_GAMEPAD = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_VIBRATION
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_CAPABILITIES
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public XINPUT_GAMEPAD Gamepad;
        public XINPUT_VIBRATION Vibration;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_CAPABILITIES_EX
    {
        public XINPUT_CAPABILITIES Capabilities;
        public ushort VendorId;
        public ushort ProductId;
        public ushort ProductVersion;
        public ushort Unknown1;
        public uint Unknown2;
    }

    [DllImport("xinput1_4.dll")]
    public static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput1_4.dll")]
    public static extern uint XInputGetCapabilities(uint dwUserIndex, uint dwFlags, out XINPUT_CAPABILITIES pCapabilities);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetCapabilitiesExFn(uint unknown, uint userIndex, uint flags, out XINPUT_CAPABILITIES_EX caps);

    private static readonly Lazy<GetCapabilitiesExFn?> CapabilitiesEx = new(() =>
    {
        try
        {
            var module = NativeMethods.LoadLibrary("xinput1_4.dll");
            if (module == IntPtr.Zero) return null;
            var proc = NativeMethods.GetProcAddress(module, 108); // XInputGetCapabilitiesEx (ordinal only)
            return proc == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<GetCapabilitiesExFn>(proc);
        }
        catch
        {
            return null;
        }
    });

    public static bool TryGetVendorProduct(uint slot, out ushort vendor, out ushort product)
    {
        vendor = product = 0;
        try
        {
            var fn = CapabilitiesEx.Value;
            if (fn is null || fn(1, slot, 0, out var caps) != ERROR_SUCCESS) return false;
            vendor = caps.VendorId;
            product = caps.ProductId;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string DescribeController(uint slot)
    {
        if (TryGetVendorProduct(slot, out var vid, out _))
        {
            var brand = vid switch
            {
                0x045E => "Xbox Controller",
                0x054C => "PlayStation Controller (XInput)",
                0x3537 => "GameSir Controller",
                0x2DC8 => "8BitDo Controller",
                0x0E6F => "PDP Controller",
                0x24C6 => "PowerA Controller",
                0x046D => "Logitech Controller",
                0x0F0D => "HORI Controller",
                0x1532 => "Razer Controller",
                0x28DE => "Steam Virtual Controller",
                _ => null,
            };
            if (brand is not null) return brand;
        }

        try
        {
            if (XInputGetCapabilities(slot, 0, out var c) == ERROR_SUCCESS)
            {
                return c.SubType switch
                {
                    0x01 => "Xbox-compatible Controller",
                    0x02 => "Racing Wheel",
                    0x03 => "Arcade Stick",
                    0x04 => "Flight Stick",
                    0x05 => "Dance Pad",
                    0x06 or 0x07 => "Guitar Controller",
                    0x08 => "Drum Kit",
                    0x13 => "Arcade Pad",
                    _ => "XInput Controller",
                };
            }
        }
        catch { /* fall through */ }
        return "XInput Controller";
    }
}
