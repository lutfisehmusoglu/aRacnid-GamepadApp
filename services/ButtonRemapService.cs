using GamepadApp.Models;

namespace GamepadApp.Services;

public class ButtonRemapService
{
    public string GetMappedButton(
        ControllerProfileSettings? settings,
        string physicalButton)
    {
        if (settings?.ButtonMappings == null)
            return physicalButton;

        if (settings.ButtonMappings.TryGetValue(
                physicalButton,
                out string? mappedButton) &&
            !string.IsNullOrWhiteSpace(mappedButton))
        {
            return mappedButton;
        }

        return physicalButton;
    }

    public void ApplyMappedInput(
        ControllerProfileSettings? settings,
        string physicalInput,
        byte inputValue,
        GamepadOutputState output,
        byte activationThreshold = 0)
    {
        if (inputValue == 0)
            return;

        string mappedOutput = GetMappedButton(
            settings,
            physicalInput);

        if (TryApplyStickDirection(
                mappedOutput,
                inputValue,
                byte.MaxValue,
                output))
        {
            return;
        }

        if (mappedOutput is not ("L2" or "R2") &&
            inputValue <= activationThreshold)
        {
            return;
        }

        ApplyDigitalOrTriggerOutput(mappedOutput, inputValue, output);
    }

    public void ApplyMappedStickDirection(
        ControllerProfileSettings? settings,
        string physicalDirection,
        int magnitude,
        int maximumMagnitude,
        GamepadOutputState output,
        byte digitalActivationThreshold = 30)
    {
        if (magnitude <= 0 || maximumMagnitude <= 0)
            return;

        string mappedOutput = GetMappedButton(
            settings,
            physicalDirection);

        if (TryApplyStickDirection(
                mappedOutput,
                magnitude,
                maximumMagnitude,
                output))
        {
            return;
        }

        byte intensity = (byte)Math.Clamp(
            (int)Math.Round(
                magnitude * byte.MaxValue / (double)maximumMagnitude),
            byte.MinValue,
            byte.MaxValue);

        if (mappedOutput is not ("L2" or "R2") &&
            intensity <= digitalActivationThreshold)
        {
            return;
        }

        ApplyDigitalOrTriggerOutput(mappedOutput, intensity, output);
    }

    public static bool IsStickDirection(string input)
    {
        return input is
            "LS X-" or "LS X+" or "LS Y+" or "LS Y-" or
            "RS X-" or "RS X+" or "RS Y+" or "RS Y-";
    }

    private static void ApplyDigitalOrTriggerOutput(
        string mappedOutput,
        byte inputValue,
        GamepadOutputState output)
    {

        if (mappedOutput == "L2")
        {
            output.LeftTrigger = Math.Max(
                output.LeftTrigger,
                inputValue);
            return;
        }

        if (mappedOutput == "R2")
        {
            output.RightTrigger = Math.Max(
                output.RightTrigger,
                inputValue);
            return;
        }

        output.Buttons.Add(mappedOutput);
    }

    private static bool TryApplyStickDirection(
        string mappedOutput,
        int magnitude,
        int maximumMagnitude,
        GamepadOutputState output)
    {
        if (!IsStickDirection(mappedOutput))
            return false;

        bool negativeCoordinate = mappedOutput is
            "LS X-" or "LS Y+" or "RS X-" or "RS Y+";

        int targetMaximum = negativeCoordinate ? 128 : 127;
        int scaledMagnitude = Math.Clamp(
            (int)Math.Round(
                magnitude * targetMaximum / (double)maximumMagnitude),
            0,
            targetMaximum);

        byte candidate = (byte)(negativeCoordinate
            ? 128 - scaledMagnitude
            : 128 + scaledMagnitude);

        switch (mappedOutput)
        {
            case "LS X-":
            case "LS X+":
                output.LeftStickX = SelectStronger(
                    output.LeftStickX,
                    candidate);
                break;
            case "LS Y+":
            case "LS Y-":
                output.LeftStickY = SelectStronger(
                    output.LeftStickY,
                    candidate);
                break;
            case "RS X-":
            case "RS X+":
                output.RightStickX = SelectStronger(
                    output.RightStickX,
                    candidate);
                break;
            case "RS Y+":
            case "RS Y-":
                output.RightStickY = SelectStronger(
                    output.RightStickY,
                    candidate);
                break;
        }

        return true;
    }

    private static byte SelectStronger(byte current, byte candidate)
    {
        int currentMagnitude = Math.Abs(current - 128);
        int candidateMagnitude = Math.Abs(candidate - 128);

        return candidateMagnitude > currentMagnitude
            ? candidate
            : current;
    }
}
