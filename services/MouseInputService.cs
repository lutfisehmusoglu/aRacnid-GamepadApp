using System.Runtime.InteropServices;

namespace GamepadApp.Services;

public sealed class MouseInputService
{
    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    private const double MoveMultiplier = 0.4;

    private bool hasPreviousTouch;
    private int previousTouchX;
    private int previousTouchY;
    private int activeTrackingId = -1;
    private bool leftButtonDown;

    public void ProcessTouch(PhysicalGamepadState input)
    {
        if (input.Touch1Active)
        {
            if (!hasPreviousTouch ||
                input.Touch1TrackingId != activeTrackingId)
            {
                // Yeni dokunuş: ilk frame'de imleç zıplamasın, yalnız
                // delta için başlangıç noktasını kaydet.
                previousTouchX = input.Touch1X;
                previousTouchY = input.Touch1Y;
                activeTrackingId = input.Touch1TrackingId;
                hasPreviousTouch = true;
            }
            else
            {
                int deltaX = input.Touch1X - previousTouchX;
                int deltaY = input.Touch1Y - previousTouchY;

                previousTouchX = input.Touch1X;
                previousTouchY = input.Touch1Y;

                int moveX = (int)Math.Round(deltaX * MoveMultiplier);
                int moveY = (int)Math.Round(deltaY * MoveMultiplier);

                if (moveX != 0 || moveY != 0)
                    SendRelativeMove(moveX, moveY);
            }
        }
        else
        {
            hasPreviousTouch = false;
        }

        UpdateLeftButton(input.TouchpadPressed);
    }

    private void UpdateLeftButton(bool pressed)
    {
        if (pressed && !leftButtonDown)
        {
            SendLeftButton(down: true);
            leftButtonDown = true;
        }
        else if (!pressed && leftButtonDown)
        {
            SendLeftButton(down: false);
            leftButtonDown = false;
        }
    }

    public void Reset()
    {
        if (leftButtonDown)
        {
            SendLeftButton(down: false);
            leftButtonDown = false;
        }

        hasPreviousTouch = false;
        activeTrackingId = -1;
    }

    private static void SendRelativeMove(int deltaX, int deltaY)
    {
        var input = new INPUT
        {
            type = InputMouse,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = deltaX,
                    dy = deltaY,
                    mouseData = 0,
                    dwFlags = MouseEventMove,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendLeftButton(bool down)
    {
        var input = new INPUT
        {
            type = InputMouse,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = down ? MouseEventLeftDown : MouseEventLeftUp,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint cInputs,
        INPUT[] pInputs,
        int cbSize);
}
