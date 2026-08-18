namespace GamepadApp.Services;

public class PhysicalGamepadState
{
    public bool IsConnected { get; set; }

    public long SequenceNumber { get; set; }
    public long TimestampTicks { get; set; }

    public byte LeftStickX { get; set; } = 128;
    public byte LeftStickY { get; set; } = 128;

    public byte RightStickX { get; set; } = 128;
    public byte RightStickY { get; set; } = 128;

    public byte LeftTrigger { get; set; }
    public byte RightTrigger { get; set; }

    public HashSet<string> Buttons { get; } = new();

    public bool PsPressed { get; set; }
    public bool TouchpadPressed { get; set; }

    public int? BatteryPercentage { get; set; }

    public PhysicalGamepadState Clone()
    {
        var clone = new PhysicalGamepadState();
        clone.CopyFrom(this);
        return clone;
    }

    public void CopyFrom(PhysicalGamepadState other)
    {
        IsConnected = other.IsConnected;
        SequenceNumber = other.SequenceNumber;
        TimestampTicks = other.TimestampTicks;
        LeftStickX = other.LeftStickX;
        LeftStickY = other.LeftStickY;
        RightStickX = other.RightStickX;
        RightStickY = other.RightStickY;
        LeftTrigger = other.LeftTrigger;
        RightTrigger = other.RightTrigger;
        PsPressed = other.PsPressed;
        TouchpadPressed = other.TouchpadPressed;
        BatteryPercentage = other.BatteryPercentage;

        Buttons.Clear();
        Buttons.UnionWith(other.Buttons);
    }

    public void Reset()
    {
        IsConnected = false;
        SequenceNumber = 0;
        TimestampTicks = 0;
        LeftStickX = 128;
        LeftStickY = 128;
        RightStickX = 128;
        RightStickY = 128;
        LeftTrigger = 0;
        RightTrigger = 0;
        Buttons.Clear();
        PsPressed = false;
        TouchpadPressed = false;
        BatteryPercentage = null;
    }
}
