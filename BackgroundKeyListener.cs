using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace KeyToSoundNeo;

public sealed class BackgroundKeyListener : IDisposable
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeKeyboard = 1;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const ushort RiKeyBreak = 0x0001;
    private const uint InputBufferSize = 4096;
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<RawInputHeader>();

    private readonly HwndSource _source;
    private readonly HwndSourceHook _hook;
    private readonly IntPtr _inputBuffer = Marshal.AllocHGlobal((int)InputBufferSize);
    private HashSet<Key> _trackedKeys = [];
    private bool _isRegistered;
    private bool _disposed;
    private int _hasReportedInputError;

    public event Action<Key, bool>? KeyEdge;
    public event Action<Exception>? InputFailed;

    public BackgroundKeyListener(HwndSource source)
    {
        _source = source;
        _hook = WindowHook;
        _source.AddHook(_hook);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isRegistered)
            return;

        var device = new RawInputDevice
        {
            UsagePage = 0x01,
            Usage = 0x06,
            Flags = RidevInputSink,
            Target = _source.Handle
        };
        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            throw new InvalidOperationException($"Could not enable background keyboard input (Windows error {Marshal.GetLastWin32Error()}).");

        _isRegistered = true;
    }

    public void SetTrackedKeys(IEnumerable<Key> keys) =>
        _trackedKeys = keys.ToHashSet();

    public void Stop()
    {
        if (!_isRegistered)
            return;

        var device = new RawInputDevice
        {
            UsagePage = 0x01,
            Usage = 0x06,
            Flags = RidevRemove,
            Target = IntPtr.Zero
        };
        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            throw new InvalidOperationException($"Could not disable background keyboard input (Windows error {Marshal.GetLastWin32Error()}).");

        _isRegistered = false;
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmInput || !_isRegistered)
            return IntPtr.Zero;

        Exception? failure = null;
        try
        {
            var size = InputBufferSize;
            var result = GetRawInputData(lParam, RidInput, _inputBuffer, ref size, HeaderSize);
            if (result != 0xFFFFFFFF && size >= HeaderSize + Marshal.SizeOf<RawKeyboard>())
            {
                var input = Marshal.PtrToStructure<RawInput>(_inputBuffer);
                if (input.Header.Type == RimTypeKeyboard)
                {
                    var key = ResolveKey(input.Keyboard);
                    if (_trackedKeys.Contains(key))
                        KeyEdge?.Invoke(key, (input.Keyboard.Flags & RiKeyBreak) == 0);
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            handled = true;
            DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (failure is not null && Interlocked.Exchange(ref _hasReportedInputError, 1) == 0)
            InputFailed?.Invoke(failure);
        return IntPtr.Zero;
    }

    private static Key ResolveKey(RawKeyboard keyboard)
    {
        var virtualKey = keyboard.VirtualKey;
        if (virtualKey is 0x10 or 0x11 or 0x12)
        {
            var scanCode = (uint)keyboard.MakeCode;
            if ((keyboard.Flags & 0x02) != 0)
                scanCode |= 0xE000;
            var sidedKey = MapVirtualKeyEx(scanCode, 3, GetKeyboardLayout(0));
            if (sidedKey != 0)
                virtualKey = (ushort)sidedKey;
        }

        return KeyInterop.KeyFromVirtualKey(virtualKey);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_isRegistered)
                Stop();
        }
        finally
        {
            _source.RemoveHook(_hook);
            Marshal.FreeHGlobal(_inputBuffer);
            _disposed = true;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices, uint deviceCount, uint structureSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr keyboardLayout);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VirtualKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        public RawInputHeader Header;
        public RawKeyboard Keyboard;
    }
}
