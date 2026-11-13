namespace ClevoControl.Core.Hardware;

public static class HardwareFactory
{
    /// <summary>
    /// Returns real hardware when InsydeDCHU.dll can be loaded and responds sensibly,
    /// otherwise a simulation (so the UI is still usable on other machines).
    /// </summary>
    public static IClevoHardware Create(bool forceSimulation = false)
    {
        if (forceSimulation) return new SimulatedHardware("forced by --simulate");

        var dchu = DchuNative.TryLoad(out var error);
        if (dchu is null) return new SimulatedHardware(error ?? "driver not found");

        try
        {
            var hw = new ClevoHardware(dchu);
            var fans = hw.ReadFans();
            if (fans.Cpu.TemperatureC is <= 0 or > 120)
                return new SimulatedHardware("EC returned no valid sensor data – unsupported model?");
            return hw;
        }
        catch (Exception ex)
        {
            return new SimulatedHardware($"driver call failed: {ex.Message}");
        }
    }
}
