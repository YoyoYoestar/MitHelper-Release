using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MitHelper.Data;

namespace MitHelper.Data;

/// Converts between EditorSheet (mutable UI model) and the canonical
/// FMBG-format JSON that MitHelper reads.
public static class SheetSerializer
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented        = true,
        AllowTrailingCommas  = true,
        ReadCommentHandling  = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // ── Save ─────────────────────────────────────────────────────────────────

    public static void SaveToPluginFolder(
        EditorSheet sheet, IDalamudPluginInterface pi, IPluginLog log)
    {
        var dir = pi.AssemblyLocation.Directory?.FullName;
        if (dir == null) { log.Error("[Editor] Cannot find plugin folder"); return; }

        var path = Path.Combine(dir, sheet.SafeFileName);
        try
        {
            var json = JsonSerializer.Serialize(ToWireFormat(sheet), Opts);
            File.WriteAllText(path, json);
            log.Information($"[Editor] Saved sheet to {path}");
        }
        catch (Exception ex)
        {
            log.Error($"[Editor] Save failed: {ex.Message}");
            throw;
        }
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    public static EditorSheet? LoadFromFile(string path, IPluginLog log)
    {
        try
        {
            var json  = File.ReadAllText(path);
            var wire  = JsonSerializer.Deserialize<WireSheet>(json, Opts);
            if (wire == null) return null;
            return FromWireFormat(wire);
        }
        catch (Exception ex)
        {
            log.Error($"[Editor] Load failed: {ex.Message}");
            return null;
        }
    }

    public static List<(string Path, string Name)> ListSheets(MitSheetLoader loader)
    {
        var results = new List<(string, string)>();
        foreach (var file in loader.GetAllJsonFiles())
        {
            try
            {
                var raw  = File.ReadAllText(file);
                var wire = JsonSerializer.Deserialize<WireSheet>(raw, Opts);
                if (wire != null)
                    results.Add((file, wire.Name ?? System.IO.Path.GetFileNameWithoutExtension(file)));
            }
            catch { /* skip unparseable files */ }
        }
        return results;
    }

    // ── Wire format ──────────────────────────────────────────────────────────
    // These match the JSON structure MitHelper reads.

    private record WireSheet(
        [property: JsonPropertyName("duty")]        uint             Duty,
        [property: JsonPropertyName("name")]        string           Name,
        [property: JsonPropertyName("description")] string           Description,
        [property: JsonPropertyName("phases")]      List<WirePhase>  Phases
    );

    private record WirePhase(
        [property: JsonPropertyName("id")]          string               Id,
        [property: JsonPropertyName("name")]        string               Name,
        [property: JsonPropertyName("mechanics")]   List<WireMechanic>   Mechanics,
        [property: JsonPropertyName("tank_combos")] List<WireTankCombo>? TankCombos
    );

    private record WireMechanic(
        [property: JsonPropertyName("id")]        string                                    Id,
        [property: JsonPropertyName("name")]      string                                    Name,
        [property: JsonPropertyName("nickname")]  string?                                   Nickname,
        [property: JsonPropertyName("timestamp")] string                                    Timestamp,
        [property: JsonPropertyName("mits")]      Dictionary<string, List<WireMitEntry>>   Mits
    );

    private class WireMitEntry
    {
        [JsonPropertyName("actionId")] public List<int>?      ActionIds { get; set; }
        [JsonPropertyName("timing")]   public WireTiming?     Timing    { get; set; }
        [JsonPropertyName("note")]     public string?         Note      { get; set; }
    }

    private record WireTiming(
        [property: JsonPropertyName("txt")]  string Text,
        [property: JsonPropertyName("aID")] int    ActionId
    );

    private record WireTankCombo(
        [property: JsonPropertyName("id")]        string              Id,
        [property: JsonPropertyName("tank_mits")] List<WireMechanic>  TankMits
    );

    // ── EditorSheet → Wire ───────────────────────────────────────────────────

    private static WireSheet ToWireFormat(EditorSheet s)
    {
        return new WireSheet(
            s.Duty, s.Name, s.Description,
            s.Phases.Select(ToWirePhase).ToList()
        );
    }

    private static WirePhase ToWirePhase(EditorPhase p)
    {
        return new WirePhase(
            p.Id, p.Name,
            p.Mechanics.Select(ToWireMechanic).ToList(),
            p.HasTankCombos && p.TankCombos.Count > 0
                ? p.TankCombos.Select(ToWireTankCombo).ToList()
                : null
        );
    }

    private static WireMechanic ToWireMechanic(EditorMechanic m)
    {
        var mits = new Dictionary<string, List<WireMitEntry>>();
        foreach (var col in AllColumns())
        {
            m.Cells.TryGetValue(col, out var cell);
            mits[col] = CellToEntries(cell);
        }
        return new WireMechanic(
            m.Id, m.Name,
            string.IsNullOrWhiteSpace(m.Nickname) ? null : m.Nickname,
            m.Timestamp, mits
        );
    }

    private static WireTankCombo ToWireTankCombo(EditorTankCombo tc)
    {
        return new WireTankCombo(
            tc.Id,
            tc.TankMits.Select(tm =>
            {
                var mits = new Dictionary<string, List<WireMitEntry>>();
                var job1 = EditorTankCombo.AbbrevToJobName(tc.Job1);
                var job2 = EditorTankCombo.AbbrevToJobName(tc.Job2);
                foreach (var job in new[] { job1, job2 })
                {
                    tm.Cells.TryGetValue(job, out var cell);
                    mits[job] = CellToEntries(cell);
                }
                return new WireMechanic(
                    tm.Id, tm.Name,
                    string.IsNullOrWhiteSpace(tm.Nickname) ? null : tm.Nickname,
                    tm.Timestamp, mits
                );
            }).ToList()
        );
    }

    private static List<WireMitEntry> CellToEntries(EditorMitCell? cell)
    {
        if (cell == null || cell.Abilities.Count == 0 && string.IsNullOrWhiteSpace(cell.Note))
            return new List<WireMitEntry>();

        var ids = cell.Abilities.Select(a => a.EncodedId).ToList();
        return new List<WireMitEntry>
        {
            new() { ActionIds = ids.Count > 0 ? ids : null },
            new() { Timing = new WireTiming(cell.TimingText, cell.TimingActionId) },
            new() { Note   = cell.Note },
        };
    }

    // ── Wire → EditorSheet ───────────────────────────────────────────────────

    private static EditorSheet FromWireFormat(WireSheet w)
    {
        return new EditorSheet
        {
            Duty        = w.Duty,
            Name        = w.Name,
            Description = w.Description,
            Phases      = w.Phases.Select(FromWirePhase).ToList(),
        };
    }

    private static EditorPhase FromWirePhase(WirePhase p)
    {
        var combos = p.TankCombos?.Select(FromWireTankCombo).ToList() ?? new();
        return new EditorPhase
        {
            Id           = p.Id,
            Name         = p.Name,
            Mechanics    = p.Mechanics.Select(FromWireMechanic).ToList(),
            TankCombos   = combos,
            HasTankCombos = combos.Count > 0,
        };
    }

    private static EditorMechanic FromWireMechanic(WireMechanic m)
    {
        var em = new EditorMechanic
        {
            Id        = m.Id,
            Name      = m.Name,
            Nickname  = m.Nickname ?? "",
            Timestamp = m.Timestamp,
            Cells     = EditorMechanic.BuildEmptyCells(),
        };
        foreach (var (col, entries) in m.Mits)
            em.Cells[col] = EntriestoCell(entries);
        return em;
    }

    private static EditorTankCombo FromWireTankCombo(WireTankCombo tc)
    {
        // Derive job abbrevs from the ID suffix (e.g. "FRU-P1-WARGNB" → WAR, GNB)
        var parts   = tc.Id.Split('-');
        var suffix  = parts.LastOrDefault() ?? "";
        var job1Abbrev = suffix.Length == 6 ? suffix[..3] : "WAR";
        var job2Abbrev = suffix.Length == 6 ? suffix[3..] : "GNB";

        return new EditorTankCombo
        {
            Id       = tc.Id,
            Job1     = job1Abbrev,
            Job2     = job2Abbrev,
            TankMits = tc.TankMits.Select(m =>
            {
                var tm = new EditorTankMechanic
                {
                    Id        = m.Id,
                    Name      = m.Name,
                    Nickname  = m.Nickname ?? "",
                    Timestamp = m.Timestamp,
                    Cells     = new(),
                };
                foreach (var (job, entries) in m.Mits)
                    tm.Cells[job] = EntriestoCell(entries);
                return tm;
            }).ToList(),
        };
    }

    private static EditorMitCell EntriestoCell(List<WireMitEntry> entries)
    {
        var cell = new EditorMitCell();
        foreach (var e in entries)
        {
            if (e.ActionIds != null)
                foreach (var id in e.ActionIds)
                    cell.Abilities.Add(new EditorAbilityEntry
                    {
                        ActionId = Math.Abs(id),
                        IsBuddy  = id < 0,
                    });
            if (e.Timing != null)
            {
                cell.TimingText     = e.Timing.Text;
                cell.TimingActionId = e.Timing.ActionId;
            }
            if (!string.IsNullOrWhiteSpace(e.Note))
                cell.Note = e.Note!;
        }
        return cell;
    }

    private static IEnumerable<string> AllColumns()
    {
        foreach (var c in JobRoleMapper.AllColumns) yield return c;
        yield return "Extras";
    }
}
