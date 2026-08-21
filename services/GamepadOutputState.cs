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
    public bool Touch1Active { get; set; }
    public int Touch1X { get; set; }
    public int Touch1Y { get; set; }
    public int Touch1TrackingId { get; set; }
    public bool Touch2Active { get; set; }
    public int Touch2X { get; set; }
    public int Touch2Y { get; set; }
    public int Touch2TrackingId { get; set; }

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
        Touch1Active = false;
        Touch1X = 0;
        Touch1Y = 0;
        Touch1TrackingId = 0;
        Touch2Active = false;
        Touch2X = 0;
        Touch2Y = 0;
        Touch2TrackingId = 0;
    }
}
