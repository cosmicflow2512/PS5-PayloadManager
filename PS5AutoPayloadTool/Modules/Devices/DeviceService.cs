using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Modules.Devices;

/// <summary>
/// Manages PS5 device records stored in config.
/// All device add / remove / select operations go through this service
/// so UI code-behind stays free of config mutation logic.
/// All methods are static — no state, no dependencies.
/// </summary>
public static class DeviceService
{
    // ── CRUD ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a new device to config.
    /// Returns the created <see cref="DeviceConfig"/>, or null if an entry
    /// with the same IP already exists.
    /// Automatically sets this device as the active host when it is the first one.
    /// </summary>
    public static DeviceConfig? Add(AppConfig config, string ip, string name)
    {
        if (config.Devices.Any(d => d.Ip == ip)) return null;

        var device = new DeviceConfig { Name = name, Ip = ip };
        config.Devices.Add(device);

        if (config.Devices.Count == 1)
            config.PS5Host = ip;

        return device;
    }

    /// <summary>
    /// Removes a device from config.
    /// If the removed device was the active host, the next available device
    /// becomes active (or the fallback IP "192.168.1.100" if none remain).
    /// </summary>
    public static void Remove(AppConfig config, DeviceConfig device)
    {
        config.Devices.Remove(device);

        if (config.PS5Host == device.Ip)
        {
            var next = config.Devices.FirstOrDefault();
            config.PS5Host = next?.Ip ?? "192.168.1.100";
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    /// <summary>Sets the active PS5 host to the given device.</summary>
    public static void SetSelected(AppConfig config, DeviceConfig device)
    {
        config.PS5Host                    = device.Ip;
        config.State.SelectedDeviceIp     = device.Ip;
    }

    /// <summary>Returns the currently selected device, or null if none match.</summary>
    public static DeviceConfig? GetSelected(AppConfig config)
        => config.Devices.FirstOrDefault(d => d.Ip == config.PS5Host);
}
