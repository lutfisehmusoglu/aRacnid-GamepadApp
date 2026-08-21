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

    public bool Touch1Active { get; set; }
    public int Touch1X { get; set; }
    public int Touch1Y { get; set; }
    public int Touch1TrackingId { get; set; }

    public bool Touch2Active { get; set; }
    public int Touch2X { get; set; }
    public int Touch2Y { get; set; }
    public int Touch2TrackingId { get; set; }

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
        Touch1Active = other.Touch1Active;
        Touch1X = other.Touch1X;
        Touch1Y = other.Touch1Y;
        Touch1TrackingId = other.Touch1TrackingId;
        Touch2Active = other.Touch2Active;
        Touch2X = other.Touch2X;
        Touch2Y = other.Touch2Y;
        Touch2TrackingId = other.Touch2TrackingId;
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
        Touch1Active = false;
        Touch1X = 0;
        Touch1Y = 0;
        Touch1TrackingId = 0;
        Touch2Active = false;
        Touch2X = 0;
        Touch2Y = 0;
        Touch2TrackingId = 0;
        BatteryPercentage = null;
    }
}
