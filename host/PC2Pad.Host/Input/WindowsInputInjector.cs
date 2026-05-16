using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PC2Pad.Host.Input;

public enum MouseButton
{
    Left,
    Right,
    Middle
}

public sealed class WindowsInputInjector
{
    private const int InputMouse = 0;
    private const int InputKeyboard = 1;

    private const uint KeyEventFKeyUp = 0x0002;

    private const uint MouseEventFMove = 0x0001;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFRightDown = 0x0008;
    private const uint MouseEventFRightUp = 0x0010;
    private const uint MouseEventFMiddleDown = 0x0020;
    private const uint MouseEventFMiddleUp = 0x0040;
    private const uint MouseEventFWheel = 0x0800;

    private readonly ConcurrentDictionary<ushort, bool> _pressedKeys = new();
    private readonly ConcurrentDictionary<MouseButton, bool> _pressedMouseButtons = new();
    private readonly ILogger<WindowsInputInjector> _logger;

    public WindowsInputInjector(ILogger<WindowsInputInjector> logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<ushort> PressedKeys => _pressedKeys.Keys.ToArray();
    public IReadOnlyCollection<MouseButton> PressedMouseButtons => _pressedMouseButtons.Keys.ToArray();

    public void SetKey(ushort virtualKey, bool down)
    {
        if (virtualKey == 0)
        {
            return;
        }

        if (down)
        {
            if (!_pressedKeys.TryAdd(virtualKey, true))
            {
                return;
            }

            SendKey(virtualKey, keyUp: false);
            return;
        }

        if (!_pressedKeys.TryRemove(virtualKey, out _))
        {
            return;
        }

        SendKey(virtualKey, keyUp: true);
    }

    public void TapKey(ushort virtualKey)
    {
        SetKey(virtualKey, true);
        SetKey(virtualKey, false);
    }

    public void MoveMouseRelative(int deltaX, int deltaY)
    {
        if (deltaX == 0 && deltaY == 0)
        {
            return;
        }

        SendMouse(deltaX, deltaY, mouseData: 0, MouseEventFMove);
    }

    public void SetMouseButton(MouseButton button, bool down)
    {
        if (down)
        {
            if (!_pressedMouseButtons.TryAdd(button, true))
            {
                return;
            }

            SendMouse(0, 0, mouseData: 0, MouseButtonFlag(button, down: true));
            return;
        }

        if (!_pressedMouseButtons.TryRemove(button, out _))
        {
            return;
        }

        SendMouse(0, 0, mouseData: 0, MouseButtonFlag(button, down: false));
    }

    public void TapMouseButton(MouseButton button)
    {
        SetMouseButton(button, true);
        SetMouseButton(button, false);
    }

    public void Wheel(int clicks)
    {
        if (clicks == 0)
        {
            return;
        }

        SendMouse(0, 0, clicks * 120, MouseEventFWheel);
    }

    public void ReleaseAll()
    {
        foreach (var key in _pressedKeys.Keys.ToArray())
        {
            SetKey(key, false);
        }

        foreach (var button in _pressedMouseButtons.Keys.ToArray())
        {
            SetMouseButton(button, false);
        }
    }

    private void SendKey(ushort virtualKey, bool keyUp)
    {
        var input = new Input
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? KeyEventFKeyUp : 0,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        SendInputOrWarn(input, $"VK {virtualKey}");
    }

    private void SendMouse(int deltaX, int deltaY, int mouseData, uint flags)
    {
        var input = new Input
        {
            type = InputMouse,
            U = new InputUnion
            {
                mi = new MouseInput
                {
                    dx = deltaX,
                    dy = deltaY,
                    mouseData = mouseData,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        SendInputOrWarn(input, $"mouse flags 0x{flags:X}");
    }

    private void SendInputOrWarn(Input input, string description)
    {
        var sent = SendInput(1, [input], Marshal.SizeOf<Input>());
        if (sent != 1)
        {
            _logger.LogWarning("SendInput failed for {Description}. LastWin32Error={Error}", description, Marshal.GetLastWin32Error());
        }
    }

    private static uint MouseButtonFlag(MouseButton button, bool down)
    {
        return button switch
        {
            MouseButton.Left => down ? MouseEventFLeftDown : MouseEventFLeftUp,
            MouseButton.Right => down ? MouseEventFRightDown : MouseEventFRightUp,
            MouseButton.Middle => down ? MouseEventFMiddleDown : MouseEventFMiddleUp,
            _ => 0
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput mi;

        [FieldOffset(0)]
        public KeyboardInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }
}
