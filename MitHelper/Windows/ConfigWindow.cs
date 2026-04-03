using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using MitHelper;

namespace MitHelper.Windows;

public class ConfigWindow : Window
{
    private readonly Configuration configuration = Plugin.Configuration;

    public ConfigWindow()
        : base("Config###MitHelper Config",
               ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        Size = new Vector2(500, 500);
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Choose Display Mode");
        ImGui.Separator();
        var displayModeIndex = (int)configuration.DisplayMode;
        if (ImGui.Combo("Display Mode##dm", ref displayModeIndex,
                        Enum.GetNames(typeof(AbilityDisplayMode)),
                        Enum.GetNames(typeof(AbilityDisplayMode)).Length))
        {
            configuration.DisplayMode = (AbilityDisplayMode)displayModeIndex;
            configuration.Save();
        }

        ImGui.Spacing();

        ImGui.Text("Compact Mode");
        ImGui.SameLine();
        HelpIcon("Hides the Time column in the main window. In the Tank Mit window, shows only your own column.");
        ImGui.Separator();
        var compact = configuration.CompactMode;
        if (ImGui.Checkbox("Compact##compact", ref compact))
        {
            configuration.CompactMode = compact;
            configuration.Save();
        }

        ImGui.Spacing();

        ImGui.Text("Swap Default to Tank 2");
        ImGui.SameLine();
        HelpIcon("By default tanks open as Tank 1. Enable this to open as Tank 2 instead.");
        ImGui.Separator();
        var tankDefault = configuration.TankDefaultSwap;
        if (ImGui.Checkbox("Swap Tank Default##td", ref tankDefault))
        {
            configuration.TankDefaultSwap = tankDefault;
            configuration.Save();
        }

        ImGui.Spacing();

        ImGui.Text("Swap Default to Melee 2");
        ImGui.SameLine();
        HelpIcon("By default melee jobs open as Melee 1. Enable this to open as Melee 2 instead.");
        ImGui.Separator();
        var meleeDefault = configuration.MeleeDefaultSwap;
        if (ImGui.Checkbox("Swap Melee Default##md", ref meleeDefault))
        {
            configuration.MeleeDefaultSwap = meleeDefault;
            configuration.Save();
        }

        ImGui.Spacing();

        ImGui.Text("Show Tank Busters");
        ImGui.SameLine();
        HelpIcon("Show tank buster rows in the mitsheet.");
        ImGui.Separator();
        var tankMits = configuration.ShowTankMits;
        if (ImGui.Checkbox("Show Tank Busters##tb", ref tankMits))
        {
            configuration.ShowTankMits = tankMits;
            configuration.Save();
        }

        ImGui.Spacing();

        ImGui.Text("Tank Mits: Separate Window");
        ImGui.SameLine();
        HelpIcon("Show tank buster mits in a separate window instead of inline.");
        ImGui.Separator();
        ImGui.BeginDisabled(!configuration.ShowTankMits);
        var separateWindow = configuration.TankMitSeparateWindow;
        if (ImGui.Checkbox("Separate Tank Window##stw", ref separateWindow))
        {
            configuration.TankMitSeparateWindow = separateWindow;
            configuration.Save();
        }
        ImGui.EndDisabled();
        if (!configuration.ShowTankMits)
            ImGui.TextDisabled("  (Enable 'Show Tank Busters' first)");
    }

    private static void HelpIcon(string message)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextUnformatted(FontAwesomeIcon.QuestionCircle.ToIconString());
        ImGui.PopFont();
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(message);
            ImGui.EndTooltip();
        }
    }
}
