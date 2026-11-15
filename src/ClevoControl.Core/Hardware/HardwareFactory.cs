using ClevoControl.Core.Hardware.Linux;

namespace ClevoControl.Core.Hardware;

public static class HardwareFactory
{
    /// <summary>
    /// Returns real hardware when the platform's Clevo driver responds sensibly
    /// (Windows: InsydeDCHU.dll, Linux: tuxedo-drivers), otherwise a simulation
    /// (so the UI is still usable on other machines).
    /// </summary>
    public static IClevoHardware Create(bool forceSimulation = false)
    {
        if (forceSimulation) return new SimulatedHardware("forced by --simulate");
        if (OperatingSystem.IsLinux()) return CreateLinux();
        if (!OperatingSystem.IsWindows()) return new SimulatedHardware("unsupported operating system");

        var dchu = DchuNative.TryLoad(out var error);
        if (dchu is null) return new SimulatedHardware(error ?? "driver not found");

        try
        {
            return Validate(new ClevoHardware(dchu));
        }
        catch (Exception ex)
        {
            return new SimulatedHardware($"driver call failed: {ex.Message}");
        }
    }

    private static IClevoHardware CreateLinux()
    {
        TuxedoIo io;
        try
        {
            io = TuxedoIo.Open();
        }
        catch (Exception ex)
        {
            return new SimulatedHardware(ex.Message);
        }
        try
        {
            if (!io.IsClevo())
            {
                io.Dispose();
                return new SimulatedHardware("tuxedo_io found no Clevo interface – the clevo_acpi / clevo_wmi module is not loaded (see docs/LINUX.md)");
            }
            var hw = new LinuxClevoHardware(io);
            var result = Validate(hw);
            if (!result.IsReal) hw.Dispose();
            return result;
        }
        catch (Exception ex)
        {
            io.Dispose();
            return new SimulatedHardware($"tuxedo_io call failed: {ex.Message}");
        }
    }

    private static IClevoHardware Validate(IClevoHardware hw)
    {
        var fans = hw.ReadFans();
        if (fans.Cpu.TemperatureC is <= 0 or > 120)
            return new SimulatedHardware("EC returned no valid sensor data – unsupported model?");
        return hw;
    }
}
