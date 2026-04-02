using System;
using System.Drawing;
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
        Size = new Vector2(232, 90);
    } 
    
    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Swap Default to Melee 2");
        ImGui.SameLine();
        HelpIcon("By default, the plugin will show Melee 2 mits when you select a Melee job.");
        ImGui.Separator();
        var meleeDefault = configuration.MeleeDefaultSwap;
        if (ImGui.Checkbox("Swap", ref meleeDefault))
        {
            configuration.MeleeDefaultSwap = meleeDefault;
            // Can save immediately on change if you don't want to provide a "Save and Close" button
            configuration.Save();
        }
        
        ImGui.Text("Swap Default to Tank 2");
        ImGui.SameLine();
        HelpIcon("By default, the plugin will show Tank 2 mits when you select a Tank job.");
        ImGui.Separator();
        var tankDefault = configuration.TankDefaultSwap;
        if (ImGui.Checkbox("Swap", ref meleeDefault))
        {
            configuration.TankDefaultSwap = tankDefault;
            // Can save immediately on change if you don't want to provide a "Save and Close" button
            configuration.Save();
        }
        
        ImGui.Text("Choose Display Mode");
        ImGui.SameLine();
        ImGui.Separator();
        var displayModeIndex = (int)configuration.DisplayMode;
        ImGui.Combo("Display Mode", ref displayModeIndex, Enum.GetNames(typeof(AbilityDisplayMode)));
        configuration.DisplayMode = (AbilityDisplayMode)displayModeIndex;
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
