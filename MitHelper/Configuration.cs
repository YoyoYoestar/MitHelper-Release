using Dalamud.Bindings.ImGui;
using Dalamud.Configuration;

namespace MitHelper;

public enum AbilityDisplayMode
{
    Icon,
    Name,
    Nickname
}

public enum ShowTankMits
{
    Yes,
    No
}

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// Display Icon, Name, or Nickname
    public AbilityDisplayMode DisplayMode { get; set; } = AbilityDisplayMode.Nickname;

    /// Show tank busters?
    public bool ShowTankMits { get; set; } = true;

    /// Swap default to OT
    public bool TankDefaultSwap { get; set; } = false;

    /// Swap default to Melee 2
    public bool MeleeDefaultSwap { get; set; } = false;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
