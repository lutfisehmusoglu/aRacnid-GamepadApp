namespace GamepadApp.Services;

public class GamepadOutputState
{
    public HashSet<string> Buttons { get; } = new();
    public byte LeftTrigger { get; set; }
    public byte RightTrigger { get; set; }
    public byte LeftStickX { get; set; } = 128;
    public byte LeftStickY { get; set; } = 128;
    public byte RightStickX { get; set; } = 128;
    public byte RightStickY { get; set; } = 128;
    public bool PsPressed { get; set; }
    public bool TouchpadPressed { get; set; }

    public void Reset()
    {
        Buttons.Clear();
        LeftTrigger = 0;
        RightTrigger = 0;
        LeftStickX = 128;
        LeftStickY = 128;
        RightStickX = 128;
        RightStickY = 128;
        PsPressed = false;
        TouchpadPressed = false;
    }
}
