using System.Collections.Generic;
using MitHelper.Data;

namespace MitHelper.Data;

// ── Mutable wrappers for in-editor use ───────────────────────────────────────
// String fields (not properties) so ImGui.InputText can take a ref to them.
// Lists and computed values stay as properties.

public class EditorSheet
{
    public uint   Duty        = 0;
    public string Name        = "New Sheet";
    public string Description = "";
    public List<EditorPhase> Phases { get; set; } = new();

    public string SafeFileName =>
        System.Text.RegularExpressions.Regex.Replace(Name.Trim(), @"[^\w\-]", "_") + ".json";
}

public class EditorPhase
{
    public string Id            = "";
    public string Name          = "New Phase";
    public bool   HasTankCombos = false;
    public List<EditorMechanic>  Mechanics  { get; set; } = new();
    public List<EditorTankCombo> TankCombos { get; set; } = new();
}

public class EditorMechanic
{
    public string Id        = "";
    public string Name      = "New Mechanic";
    public string Nickname  = "";
    public string Timestamp = "0:00";
    public Dictionary<string, EditorMitCell> Cells { get; set; } = BuildEmptyCells();

    public static Dictionary<string, EditorMitCell> BuildEmptyCells()
    {
        var d = new Dictionary<string, EditorMitCell>();
        foreach (var col in JobRoleMapper.AllColumns)
            d[col] = new EditorMitCell();
        d["Extras"] = new EditorMitCell();
        return d;
    }
}

public class EditorMitCell
{
    public List<EditorAbilityEntry> Abilities { get; set; } = new();
    public string TimingText     = "";
    public int    TimingActionId = -1;
    public string Note           = "";
}

public class EditorAbilityEntry
{
    public int  ActionId  = 0;
    public bool IsBuddy   = false;

    public int EncodedId => IsBuddy ? -ActionId : ActionId;
    public int AbsId     => System.Math.Abs(ActionId);
}

public class EditorTankCombo
{
    public string Id   = "";
    public string Job1 = "WAR";
    public string Job2 = "GNB";
    public List<EditorTankMechanic> TankMits { get; set; } = new();

    public string ComboLabel => $"{Job1}/{Job2}";

    public static readonly string[] TankAbbrevs = { "WAR", "GNB", "PLD", "DRK" };

    public static string AbbrevToJobName(string abbrev) => abbrev switch
    {
        "WAR" => "Warrior",
        "GNB" => "Gunbreaker",
        "PLD" => "Paladin",
        "DRK" => "DarkKnight",
        _     => abbrev
    };
}

public class EditorTankMechanic
{
    public string Id        = "";
    public string Name      = "New Buster";
    public string Nickname  = "";
    public string Timestamp = "0:00";
    public Dictionary<string, EditorMitCell> Cells { get; set; } = new();
}
