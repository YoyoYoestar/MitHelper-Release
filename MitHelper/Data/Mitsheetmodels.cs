using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MitHelper.Data;

//Intro Sheet
public record MitSheet(
    [property: JsonPropertyName("duty")]        uint Duty,
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("phases")]      List<Phase> Phases
);

/// Phase Info
public record Phase(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("mechanics")]   List<Mechanic> Mechanics,
    [property: JsonPropertyName("tank_combos")] List<TankCombo>? TankCombos
);

/// Mech info
public record Mechanic(
    [property: JsonPropertyName("id")]        string Id,
    [property: JsonPropertyName("name")]      string Name,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("mits")]      Dictionary<string, List<MitEntry>> Mits
);

//Mech data 
public class MitEntry
{
    [JsonPropertyName("actionId")] public List<int>?  ActionIds { get; set; }
    [JsonPropertyName("timing")]   public MitTiming?  Timing    { get; set; }
    [JsonPropertyName("note")]     public string?     Note      { get; set; }
}
//Timing data
public record MitTiming(
    [property: JsonPropertyName("txt")]  string Text,
    [property: JsonPropertyName("aID")] int    ActionId
);

//Tank buster info
public record TankCombo(
    [property: JsonPropertyName("id")]        string         Id,
    [property: JsonPropertyName("tank_mits")] List<Mechanic> TankMits
);
