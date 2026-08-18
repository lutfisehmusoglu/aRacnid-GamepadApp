using System.Diagnostics;

namespace GamepadApp.Services;

public sealed class PhysicalGamepadManager : IDisposable
{
    private readonly object sessionSync = new();
    private readonly IPhysicalGamepadProvider[] providers;

    private IPhysicalGamepadSession? currentSession;
    private PhysicalGamepadDescriptor? currentDescriptor;
    private bool disposed;

    public PhysicalGamepadManager()
    {
        // DS4 önce denenir; bu sıra çalışan ham DS4 yolunun SDL tarafından
        // devralınmasını engeller.
        providers =
        [
            new DualShock4HidProvider(),
            new SdlGamepadProvider()
        ];
    }

    public PhysicalGamepadDescriptor? CurrentDescriptor =>
        Volatile.Read(ref currentDescriptor);

    public bool IsConnected => CurrentDescriptor != null;

    public bool TryConnect()
    {
        if (disposed)
            return false;

        lock (sessionSync)
        {
            if (currentSession != null)
                return true;

            foreach (IPhysicalGamepadProvider provider in providers)
            {
                IPhysicalGamepadSession? session = null;

                try
                {
                    session = provider.TryOpen();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"Fiziksel gamepad sağlayıcısı hata verdi: {ex}");
                }

                if (session == null)
                    continue;

                currentSession = session;
                Volatile.Write(ref currentDescriptor, session.Descriptor);
                return true;
            }
        }

        return false;
    }

    internal PhysicalReadResult ReadNext(
        out PhysicalGamepadState? state)
    {
        state = null;

        IPhysicalGamepadSession? session;

        lock (sessionSync)
            session = currentSession;

        if (session == null)
            return PhysicalReadResult.Disconnected;

        PhysicalReadResult result = session.ReadNext(out state);

        if (result == PhysicalReadResult.Disconnected)
            DisconnectCurrent();

        return result;
    }

    public bool TrySetVibration(
        byte leftMotor,
        byte rightMotor,
        uint durationMs = 500)
    {
        IPhysicalGamepadSession? session;

        lock (sessionSync)
            session = currentSession;

        return session?.TrySetVibration(
            leftMotor,
            rightMotor,
            durationMs) == true;
    }

    public bool TrySetLightbar(byte red, byte green, byte blue)
    {
        IPhysicalGamepadSession? session;

        lock (sessionSync)
            session = currentSession;

        return session?.TrySetLightbar(red, green, blue) == true;
    }

    public bool WaitForPendingOutput(int timeoutMs)
    {
        IPhysicalGamepadSession? session;

        lock (sessionSync)
            session = currentSession;

        return session?.WaitForPendingOutput(timeoutMs) != false;
    }

    public void DisconnectCurrent()
    {
        IPhysicalGamepadSession? session;

        lock (sessionSync)
        {
            session = currentSession;
            currentSession = null;
            Volatile.Write(ref currentDescriptor, null);
        }

        if (session == null)
            return;

        try
        {
            session.Dispose();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        DisconnectCurrent();

        foreach (IPhysicalGamepadProvider provider in providers)
        {
            try
            {
                provider.Dispose();
            }
            catch
            {
            }
        }
    }
}
