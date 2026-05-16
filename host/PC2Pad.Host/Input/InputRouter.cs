using PC2Pad.Host.Models;

namespace PC2Pad.Host.Input;

public sealed class InputRouter
{
    private readonly ILogger<InputRouter> _logger;

    public InputRouter(ILogger<InputRouter> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(InputMessage message, CancellationToken cancellationToken)
    {
        // MVP: not yet true input injection.
        // Phase 2: Mapping -> Virtual Controller / Keyboard / Mouse.
        if (message.Type.Equals("key", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Input key: action={Action}, keyCode={KeyCode}, scanCode={ScanCode}, source={Source}",
                message.Action,
                message.KeyCode,
                message.ScanCode,
                message.Source
            );

            return Task.CompletedTask;
        }

        if (message.Type.Equals("axis", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Input axis: LX={LX:0.00}, LY={LY:0.00}, RX={RX:0.00}, RY={RY:0.00}, LT={LT:0.00}, RT={RT:0.00}",
                message.LeftStickX,
                message.LeftStickY,
                message.RightStickX,
                message.RightStickY,
                message.LeftTrigger,
                message.RightTrigger
            );

            return Task.CompletedTask;
        }

        _logger.LogWarning("Unknown input message type: {Type}", message.Type);
        return Task.CompletedTask;
    }
}
