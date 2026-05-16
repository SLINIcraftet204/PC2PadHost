using PC2Pad.Host.Models;

namespace PC2Pad.Host.Input;

public sealed class InputRouter
{
    private const ushort VK_BACK = 0x08;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_UP = 0x26;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_DOWN = 0x28;
    private const ushort VK_A = 0x41;
    private const ushort VK_C = 0x43;
    private const ushort VK_D = 0x44;
    private const ushort VK_E = 0x45;
    private const ushort VK_F = 0x46;
    private const ushort VK_Q = 0x51;
    private const ushort VK_R = 0x52;
    private const ushort VK_S = 0x53;
    private const ushort VK_W = 0x57;

    private readonly IConfiguration _configuration;
    private readonly ILogger<InputRouter> _logger;
    private readonly WindowsInputInjector _injector;

    public InputRouter(
        IConfiguration configuration,
        ILogger<InputRouter> logger,
        WindowsInputInjector injector)
    {
        _configuration = configuration;
        _logger = logger;
        _injector = injector;
    }

    public IReadOnlyCollection<ushort> PressedKeys => _injector.PressedKeys;
    public IReadOnlyCollection<MouseButton> PressedMouseButtons => _injector.PressedMouseButtons;

    public Task HandleAsync(InputMessage message, CancellationToken cancellationToken)
    {
        var enabled = _configuration.GetValue<bool?>("PC2Pad:Input:Enabled") ?? true;
        var mode = _configuration.GetValue<string>("PC2Pad:Input:Mode") ?? "keyboard";

        if (!enabled || mode.Equals("log", StringComparison.OrdinalIgnoreCase))
        {
            LogMessage(message);
            return Task.CompletedTask;
        }

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("Input injection is only available on Windows. Message is logged only.");
            LogMessage(message);
            return Task.CompletedTask;
        }

        if (message.Type.Equals("key", StringComparison.OrdinalIgnoreCase))
        {
            HandleKey(message);
            return Task.CompletedTask;
        }

        if (message.Type.Equals("axis", StringComparison.OrdinalIgnoreCase))
        {
            HandleAxis(message);
            return Task.CompletedTask;
        }

        if (message.Type.Equals("pointer", StringComparison.OrdinalIgnoreCase))
        {
            HandlePointer(message);
            return Task.CompletedTask;
        }

        _logger.LogWarning("Unknown input message type: {Type}", message.Type);
        return Task.CompletedTask;
    }

    public void ReleaseAll()
    {
        _injector.ReleaseAll();
    }

    private void HandleKey(InputMessage message)
    {
        if (message.KeyCode is null)
        {
            return;
        }

        var virtualKey = MapAndroidKeyCode(message.KeyCode.Value);
        if (virtualKey == 0)
        {
            _logger.LogDebug("No key mapping for Android keyCode {KeyCode}.", message.KeyCode.Value);
            return;
        }

        var down = message.Action?.Equals("down", StringComparison.OrdinalIgnoreCase) == true;
        var up = message.Action?.Equals("up", StringComparison.OrdinalIgnoreCase) == true;

        if (down)
        {
            _injector.SetKey(virtualKey, true);
        }
        else if (up)
        {
            _injector.SetKey(virtualKey, false);
        }
    }

    private void HandleAxis(InputMessage message)
    {
        var threshold = _configuration.GetValue<float?>("PC2Pad:Input:AxisThreshold") ?? 0.35f;

        SetAxisPair(message.LeftStickX ?? 0f, negativeKey: VK_A, positiveKey: VK_D, threshold);
        SetAxisPair(message.LeftStickY ?? 0f, negativeKey: VK_W, positiveKey: VK_S, threshold);
        SetAxisPair(message.HatX ?? 0f, negativeKey: VK_LEFT, positiveKey: VK_RIGHT, threshold);
        SetAxisPair(message.HatY ?? 0f, negativeKey: VK_UP, positiveKey: VK_DOWN, threshold);

        if (_configuration.GetValue<bool?>("PC2Pad:Input:RightStickAsMouse") ?? true)
        {
            var mouseSensitivity = _configuration.GetValue<float?>("PC2Pad:Input:MouseSensitivity") ?? 18f;
            var dx = AxisToMouseDelta(message.RightStickX ?? 0f, mouseSensitivity);
            var dy = AxisToMouseDelta(message.RightStickY ?? 0f, mouseSensitivity);
            _injector.MoveMouseRelative(dx, dy);
        }

        if (_configuration.GetValue<bool?>("PC2Pad:Input:TriggersAsMouseButtons") ?? true)
        {
            var triggerThreshold = _configuration.GetValue<float?>("PC2Pad:Input:TriggerThreshold") ?? 0.45f;
            _injector.SetMouseButton(MouseButton.Right, (message.LeftTrigger ?? 0f) >= triggerThreshold);
            _injector.SetMouseButton(MouseButton.Left, (message.RightTrigger ?? 0f) >= triggerThreshold);
        }
    }

    private void HandlePointer(InputMessage message)
    {
        var action = message.Action ?? "move";

        if (action.Equals("move", StringComparison.OrdinalIgnoreCase))
        {
            var sensitivity = _configuration.GetValue<float?>("PC2Pad:Input:TouchMouseSensitivity") ?? 1.2f;
            var dx = (int)Math.Round((message.DeltaX ?? 0f) * sensitivity);
            var dy = (int)Math.Round((message.DeltaY ?? 0f) * sensitivity);
            _injector.MoveMouseRelative(dx, dy);
            return;
        }

        var button = MapMouseButton(message.Button);
        if (button is null)
        {
            return;
        }

        if (action.Equals("down", StringComparison.OrdinalIgnoreCase))
        {
            _injector.SetMouseButton(button.Value, true);
        }
        else if (action.Equals("up", StringComparison.OrdinalIgnoreCase))
        {
            _injector.SetMouseButton(button.Value, false);
        }
        else if (action.Equals("tap", StringComparison.OrdinalIgnoreCase))
        {
            _injector.TapMouseButton(button.Value);
        }
        else if (action.Equals("wheel", StringComparison.OrdinalIgnoreCase))
        {
            _injector.Wheel(message.Wheel ?? 0);
        }
    }

    private void SetAxisPair(float value, ushort negativeKey, ushort positiveKey, float threshold)
    {
        if (value <= -threshold)
        {
            _injector.SetKey(negativeKey, true);
            _injector.SetKey(positiveKey, false);
            return;
        }

        if (value >= threshold)
        {
            _injector.SetKey(negativeKey, false);
            _injector.SetKey(positiveKey, true);
            return;
        }

        _injector.SetKey(negativeKey, false);
        _injector.SetKey(positiveKey, false);
    }

    private static int AxisToMouseDelta(float value, float sensitivity)
    {
        if (Math.Abs(value) < 0.12f)
        {
            return 0;
        }

        var shaped = Math.Sign(value) * value * value;
        return (int)Math.Round(shaped * sensitivity);
    }

    private static MouseButton? MapMouseButton(string? button)
    {
        return (button ?? "left").Trim().ToLowerInvariant() switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => null
        };
    }

    private static ushort MapAndroidKeyCode(int keyCode)
    {
        return keyCode switch
        {
            // Android KeyEvent constants, kept numeric to avoid Android dependency in the host.
            19 => VK_UP,       // DPAD_UP
            20 => VK_DOWN,     // DPAD_DOWN
            21 => VK_LEFT,     // DPAD_LEFT
            22 => VK_RIGHT,    // DPAD_RIGHT
            23 => VK_RETURN,   // DPAD_CENTER
            96 => VK_SPACE,    // BUTTON_A
            97 => VK_ESCAPE,   // BUTTON_B
            99 => VK_E,        // BUTTON_X
            100 => VK_Q,       // BUTTON_Y
            102 => VK_SHIFT,   // BUTTON_L1
            103 => VK_CONTROL, // BUTTON_R1
            104 => VK_SPACE,   // BUTTON_L2, some controllers send L2 as key instead of axis
            105 => VK_R,       // BUTTON_R2, some controllers send R2 as key instead of axis
            106 => VK_C,       // BUTTON_THUMBL
            107 => VK_F,       // BUTTON_THUMBR
            108 => VK_RETURN,  // BUTTON_START
            109 => VK_BACK,    // BUTTON_SELECT
            110 => VK_TAB,     // BUTTON_MODE
            _ => 0
        };
    }

    private void LogMessage(InputMessage message)
    {
        if (message.Type.Equals("key", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Input key: action={Action}, keyCode={KeyCode}, scanCode={ScanCode}, source={Source}",
                message.Action,
                message.KeyCode,
                message.ScanCode,
                message.Source);
            return;
        }

        if (message.Type.Equals("axis", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Input axis: LX={LX:0.00}, LY={LY:0.00}, RX={RX:0.00}, RY={RY:0.00}, LT={LT:0.00}, RT={RT:0.00}, HAT=({HatX:0.00},{HatY:0.00})",
                message.LeftStickX,
                message.LeftStickY,
                message.RightStickX,
                message.RightStickY,
                message.LeftTrigger,
                message.RightTrigger,
                message.HatX,
                message.HatY);
            return;
        }

        if (message.Type.Equals("pointer", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Input pointer: action={Action}, button={Button}, delta=({DeltaX:0.0},{DeltaY:0.0}), wheel={Wheel}",
                message.Action,
                message.Button,
                message.DeltaX,
                message.DeltaY,
                message.Wheel);
            return;
        }

        _logger.LogWarning("Unknown input message type: {Type}", message.Type);
    }
}
