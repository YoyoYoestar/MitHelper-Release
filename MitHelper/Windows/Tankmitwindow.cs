using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using MitHelper.Data;

namespace MitHelper.Windows;

public class TankMitWindow : Window, IDisposable
{
    private readonly MainWindow _mainWindow;
    private static readonly Vector2 IconSize = new(24, 24);
    private static readonly string[] TankPriority = { "WAR", "GNB", "PLD", "DRK" };

    // Sentinel helpers (must match MainWindow logic)
    private static bool IsBuddyMit(int id) => id < -3;

    public TankMitWindow(MainWindow mainWindow)
        : base("Tank Mits##MitHelperTankMit",
               ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _mainWindow = mainWindow;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 150),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var config = Plugin.Configuration;

        var sheets = _mainWindow.Sheets;
        if (sheets.Count == 0) { ImGui.Text("No sheet loaded."); return; }

        var sheet = sheets[_mainWindow.SheetIndex];
        if (sheet == null || sheet.Phases == null || sheet.Phases.Count == 0)
        { ImGui.Text("Sheet has no phases."); return; }

        var phaseIndex = Math.Clamp(_mainWindow.PhaseIndex, 0, sheet.Phases.Count - 1);
        var phase = sheet.Phases[phaseIndex];
        if (phase == null) { ImGui.Text("Phase data is missing."); return; }

        ImGui.Text($"Phase {phaseIndex + 1}/{sheet.Phases.Count}: {phase.Name}");
        ImGui.Separator();

        if (phase.TankCombos == null || phase.TankCombos.Count == 0)
        { ImGui.Text("No tank combos for this phase."); return; }

        var playerJobId = GetPlayerJobId();
        var (tank1Abbrev, tank2Abbrev) = DetectTankPair();
        var relevantCombos = FindRelevantTankCombos(phase.TankCombos, tank1Abbrev, tank2Abbrev);
        var tbRows = BuildRows(relevantCombos);

        if (tbRows.Count == 0) { ImGui.Text("No tank busters this phase."); return; }

        // Compact mode: show only the column for the player's tank
        // Non-compact: show both tanks
        bool compact = config.CompactMode && IsPlayerTank(playerJobId);

        string? playerAbbrev = null;
        string col1Label, col2Label;
        bool showBothCols;

        if (compact)
        {
            // Determine which column the player is in (Tank 1 or Tank 2 per prio)
            playerAbbrev = JobIdToAbbrev(playerJobId);
            col1Label = playerAbbrev;
            col2Label = ""; // unused
            showBothCols = false;
        }
        else
        {
            col1Label = tank1Abbrev ?? "Tank 1";
            col2Label = tank2Abbrev ?? "Tank 2";
            showBothCols = true;
            ImGui.Text($"{col1Label}/{col2Label}");
            ImGui.Separator();
        }

        // Build notes glossary for tank rows — always use "Tank 1"/"Tank 2" as slot keys
        var noteSlots = showBothCols
            ? new List<string> { "Tank 1", "Tank 2" }
            : new List<string> { compact && playerAbbrev == (tank1Abbrev ?? "Tank 1") ? "Tank 1" : "Tank 2" };
        var (noteGlossary, rowNoteMap) = BuildNotes(tbRows, noteSlots, tank1Abbrev, tank2Abbrev);

        int colCount = showBothCols ? 4 : 3; // Time | Mech | T1 [| T2]
        if (config.CompactMode) colCount--; // no Time col in compact

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;
        if (!ImGui.BeginTable("TankMitTable", colCount, tableFlags)) return;

        ImGui.TableSetupScrollFreeze(compact ? 1 : 2, 1);
        if (!compact) ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Mech", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableSetupColumn(col1Label, ImGuiTableColumnFlags.WidthStretch, 1f);
        if (showBothCols)
            ImGui.TableSetupColumn(col2Label, ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableHeadersRow();

        var tbColor = new Vector4(1f, 0.6f, 0.2f, 1f);

        for (int rowIdx = 0; rowIdx < tbRows.Count; rowIdx++)
        {
            var row = tbRows[rowIdx];
            ImGui.TableNextRow();
            int col = 0;

            if (!compact)
            {
                ImGui.TableSetColumnIndex(col++);
                CenterText(row.Timestamp, tbColor);
            }

            ImGui.TableSetColumnIndex(col++);
            var displayName = compact && row.Nickname != null ? row.Nickname : row.Name;
            CenterText(displayName, tbColor);

            if (showBothCols)
            {
                // Tank 1 column
                ImGui.TableSetColumnIndex(col++);
                rowNoteMap.TryGetValue((rowIdx, "Tank 1"), out var note1);
                var (t1Text, t1Aid) = GetTankCellTiming(row, "Tank 1", tank1Abbrev, tank2Abbrev);
                DrawTankCell(row, "Tank 1", tank1Abbrev, tank2Abbrev, config, note1, t1Text, t1Aid, compact);

                // Tank 2 column
                ImGui.TableSetColumnIndex(col++);
                rowNoteMap.TryGetValue((rowIdx, "Tank 2"), out var note2);
                var (t2Text, t2Aid) = GetTankCellTiming(row, "Tank 2", tank1Abbrev, tank2Abbrev);
                DrawTankCell(row, "Tank 2", tank1Abbrev, tank2Abbrev, config, note2, t2Text, t2Aid, compact);
            }
            else
            {
                string playerCol = (tank1Abbrev != null &&
                    JobNameToAbbrev_FromAbbrev(tank1Abbrev) == playerAbbrev)
                    ? "Tank 1" : "Tank 2";

                ImGui.TableSetColumnIndex(col++);
                rowNoteMap.TryGetValue((rowIdx, playerCol), out var noteP);
                var (tPText, tPAid) = GetTankCellTiming(row, playerCol, tank1Abbrev, tank2Abbrev);
                DrawTankCell(row, playerCol, tank1Abbrev, tank2Abbrev, config, noteP, tPText, tPAid, compact);
            }
        }

        // Notes glossary footer
        if (noteGlossary.Count > 0)
        {
            ImGui.TableNextRow();
            int col = 0;
            if (!compact) { ImGui.TableSetColumnIndex(col++); ImGui.Text(""); }
            ImGui.TableSetColumnIndex(col++);
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.5f, 1f), "Notes");

            for (int ci = 0; ci < noteSlots.Count; ci++)
            {
                ImGui.TableSetColumnIndex(col + ci);
                var slot = noteSlots[ci];
                var entries = noteGlossary
                    .Where(kv => kv.Key.ColName == slot)
                    .OrderBy(kv => kv.Value.StarCount)
                    .Select(kv => $"{kv.Value.Stars} {kv.Value.NoteText}")
                    .ToList();
                if (entries.Count > 0)
                    ImGui.TextWrapped(string.Join("\n", entries));
            }
        }

        ImGui.EndTable();
    }

    // ─── Cell drawing ─────────────────────────────────────────────────────────

    private static string BuddyLabel(bool compact) => compact ? "Buddy:" : "Buddy Mit:";
    private static string CompactTimingLabel(string s) =>
        s.Replace("(Late)", "(L)").Replace("(Early)", "(E)");

    private static (string? Text, int ActionId) GetTankCellTiming(TankBusterRow row, string colName,
        string? tank1Abbrev, string? tank2Abbrev)
    {
        var targetAbbrev = colName == "Tank 1" ? tank1Abbrev : tank2Abbrev;
        var matchedJob   = FindMatchedJob(row.TimingByJob, colName, targetAbbrev);
        if (matchedJob != null && row.TimingByJob.TryGetValue(matchedJob, out var t))
            return (t.Text, t.ActionId);
        return (null, -1);
    }

    private static void DrawTankCell(TankBusterRow row, string colName,
        string? tank1Abbrev, string? tank2Abbrev,
        Configuration config, string? noteSuffix,
        string? timingText, int timingAid, bool compact)
    {
        var targetAbbrev = colName == "Tank 1" ? tank1Abbrev : tank2Abbrev;
        var matchedJob   = FindMatchedJob(row.OwnIds, colName, targetAbbrev);

        if (matchedJob == null)
        {
            CenterText("-", null);
            return;
        }

        uint colJobId = targetAbbrev != null ? AbbrevToJobId(targetAbbrev) : 0;

        row.OwnIds.TryGetValue(matchedJob, out var ownRaw);
        var buddyMatchedJob = FindMatchedJob(row.BuddyIds, colName, targetAbbrev);
        row.BuddyIds.TryGetValue(buddyMatchedJob ?? "", out var buddyList);

        string? displayTiming = string.IsNullOrEmpty(timingText) ? null
            : (compact ? CompactTimingLabel(timingText) : timingText);

        if (config.DisplayMode == AbilityDisplayMode.Icon)
        {
            var abilityIds = new List<uint>();
            foreach (var id in ownRaw ?? new List<int>())
            {
                if (id == 0)
                { if (colJobId != 0 && JobRoleMapper.PartyMitsTanks.TryGetValue(colJobId, out var m)) abilityIds.Add(m); }
                else if (id == -1) { }
                else if (id > 0 && AbilityExtraInfoData.AbilitiesInfo.ContainsKey((uint)id))
                    abilityIds.Add((uint)id);
                else if (id < -1)
                { var uid = (uint)id; if (AbilityExtraInfoData.AbilitiesInfo.ContainsKey(uid)) abilityIds.Add(uid); }
            }
            DrawIconCell(abilityIds, buddyList ?? new List<uint>(),
                         timingText, timingAid, noteSuffix, compact);
        }
        else
        {
            var parts = new List<string>();
            foreach (var id in ownRaw ?? new List<int>())
            {
                if (id == 0)
                { if (colJobId != 0 && JobRoleMapper.PartyMitsTanks.TryGetValue(colJobId, out var m)) parts.Add(FormatAbility(m, config.DisplayMode)); }
                else if (id == -1) { }
                else if (id > 0) { var s = FormatAbility((uint)id, config.DisplayMode); if (s.Length > 0) parts.Add(s); }
                else { var uid = (uint)id; if (AbilityExtraInfoData.AbilitiesInfo.ContainsKey(uid)) parts.Add(FormatAbility(uid, config.DisplayMode)); }
            }
            if (buddyList != null && buddyList.Count > 0)
            {
                var bl  = BuddyLabel(compact);
                var bp  = buddyList.Select(id => FormatAbility(id, config.DisplayMode)).Where(s => s.Length > 0).ToList();
                if (bp.Count > 0) parts.Add(bl + " " + string.Join("/", bp));
            }
            var text = parts.Count > 0 ? string.Join("/", parts) : "-";
            if (!string.IsNullOrEmpty(displayTiming)) text += " " + displayTiming;
            if (!string.IsNullOrEmpty(noteSuffix))    text += noteSuffix;
            CenterText(text, null);
        }
    }

    private static void DrawIconCell(List<uint> abilityIds, List<uint> buddyIds,
                                     string? timingText, int timingAid,
                                     string? noteSuffix, bool compact)
    {
        if (abilityIds.Count == 0 && buddyIds.Count == 0) { CenterText("-", null); return; }

        string buddyLbl = BuddyLabel(compact);
        string? displayTiming = string.IsNullOrEmpty(timingText) ? null
            : (compact ? CompactTimingLabel(timingText) : timingText);

        bool wholeCell = displayTiming != null &&
            (timingAid == -1 || !AbilityMatchesTiming(abilityIds, buddyIds, timingAid));

        const float IW = 24f, IG = 2f, BG = 4f, PLG = 2f;
        float blW  = buddyIds.Count > 0 ? ImGui.CalcTextSize(buddyLbl).X : 0f;
        float totalW = 0f;
        if (abilityIds.Count > 0) totalW += abilityIds.Count * IW + Math.Max(0, abilityIds.Count - 1) * IG;
        if (buddyIds.Count > 0)
        { if (abilityIds.Count > 0) totalW += BG; totalW += blW + PLG + buddyIds.Count * IW + Math.Max(0, buddyIds.Count - 1) * IG; }
        if (displayTiming != null)
            totalW += ImGui.CalcTextSize(displayTiming).X + 2f; // whether per-icon or whole-cell
        CenterCursorForWidth(totalW);

        bool first = true;

        void Render(uint id)
        {
            if (!first) ImGui.SameLine(0, IG); first = false;
            if (!AbilityExtraInfoData.AbilitiesInfo.TryGetValue(id, out var info)) return;
            if (info.IconId == 0) { ImGui.Text(info.Nickname); return; }
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(info.IconId)).GetWrapOrEmpty();
            ImGui.Image(wrap.Handle, IconSize);
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.Text(info.Nickname); ImGui.EndTooltip(); }
        }

        void MaybeTimingBefore(uint id)
        {
            if (displayTiming == null || wholeCell) return;
            uint matchId = (uint)Math.Abs(timingAid);
            if (id != matchId) return;
            if (!first) ImGui.SameLine(0, 2);
            first = false;
            ImGui.TextDisabled(displayTiming);
        }

        foreach (var id in abilityIds) { MaybeTimingBefore(id); Render(id); }
        if (buddyIds.Count > 0)
        {
            if (!first) ImGui.SameLine(0, BG); first = false;
            ImGui.TextDisabled(buddyLbl);
            foreach (var id in buddyIds) { MaybeTimingBefore(id); ImGui.SameLine(0, PLG); Render(id); }
        }
        if (wholeCell && !string.IsNullOrEmpty(displayTiming))
        { ImGui.SameLine(0, 2); ImGui.TextDisabled(displayTiming); }
        if (!string.IsNullOrEmpty(noteSuffix))
        { ImGui.SameLine(0, 2); ImGui.TextDisabled(noteSuffix); }
    }

    private static bool AbilityMatchesTiming(List<uint> abilityIds, List<uint> buddyIds, int aID)
    {
        if (aID == -1) return false;
        uint mid = (uint)Math.Abs(aID);
        return abilityIds.Contains(mid) || buddyIds.Contains(mid);
    }

    // ─── Centering ────────────────────────────────────────────────────────────

    private static void CenterCursorForWidth(float w)
    { var off = (ImGui.GetColumnWidth() - w) * 0.5f; if (off > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + off); }

    private static void CenterText(string text, Vector4? color)
    {
        CenterCursorForWidth(ImGui.CalcTextSize(text).X);
        if (color.HasValue) ImGui.TextColored(color.Value, text);
        else ImGui.TextUnformatted(text);
    }

    // ─── Notes ────────────────────────────────────────────────────────────────

    private record NoteKey(string ColName, string NoteText);
    private record NoteGlossaryEntry(string NoteText, string Stars, int StarCount);

    private static (Dictionary<NoteKey, NoteGlossaryEntry>, Dictionary<(int, string), string>)
        BuildNotes(List<TankBusterRow> rows, List<string> colSlots,
                   string? tank1Abbrev, string? tank2Abbrev)
    {
        var glossary = new Dictionary<NoteKey, NoteGlossaryEntry>();
        var rowMap   = new Dictionary<(int, string), string>();
        var colIdx   = colSlots.ToDictionary(c => c, _ => new Dictionary<string, int>(StringComparer.Ordinal));

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            foreach (var slot in colSlots)
            {
                // slot is "Tank 1" or "Tank 2"; look up the note by matching job name
                var targetAbbrev = slot == "Tank 1" ? tank1Abbrev : tank2Abbrev;
                var matchedJob   = FindMatchedJob(row.NotesByJob, slot, targetAbbrev);
                if (matchedJob == null || !row.NotesByJob.TryGetValue(matchedJob, out var note)) continue;
                if (string.IsNullOrWhiteSpace(note)) continue;
                if (!colIdx.TryGetValue(slot, out var idx)) continue;

                if (!idx.TryGetValue(note, out var starCount))
                {
                    starCount = idx.Count + 1;
                    idx[note] = starCount;
                    glossary[new NoteKey(slot, note)] =
                        new NoteGlossaryEntry(note, new string('*', starCount), starCount);
                }
                rowMap[(i, slot)] = new string('*', starCount);
            }
        }
        return (glossary, rowMap);
    }

    // ─── Row model ────────────────────────────────────────────────────────────

    private record TankBusterRow(
        string Timestamp, string Name, string? Nickname,
        Dictionary<string, List<int>>   OwnIds,
        Dictionary<string, List<uint>>  BuddyIds,
        Dictionary<string, string>      NotesByJob,           // job-name → note
        Dictionary<string, (string Text, int ActionId)> TimingByJob  // job-name → (text, aID)
    );

    private static List<TankBusterRow> BuildRows(List<TankCombo> combos)
    {
        var rows = new List<(TimeSpan ts, TankBusterRow row)>();
        foreach (var combo in combos)
        {
            foreach (var mech in combo.TankMits)
            {
                var ownIds     = new Dictionary<string, List<int>>();
                var buddyIds   = new Dictionary<string, List<uint>>();
                var notesByJob = new Dictionary<string, string>();
                var timings    = new Dictionary<string, (string Text, int ActionId)>();

                foreach (var (role, entries) in mech.Mits)
                {
                    var own   = new List<int>();
                    var buddy = new List<uint>();
                    string note       = "";
                    string timingText = "";
                    int    timingAid  = -1;
                    foreach (var e in entries)
                    {
                        if (e.ActionIds != null)
                            foreach (var id in e.ActionIds)
                            {
                                if (id < -3)
                                    buddy.Add((uint)Math.Abs(id));
                                else if (id == (int)Abilities.NascentFlash ||
                                         id == (int)Abilities.Intervention)
                                    buddy.Add((uint)id);
                                else
                                    own.Add(id);
                            }
                        if (!string.IsNullOrWhiteSpace(e.Note)) note = e.Note!;
                        if (e.Timing != null && !string.IsNullOrWhiteSpace(e.Timing.Text))
                        { timingText = e.Timing.Text; timingAid = e.Timing.ActionId; }
                    }
                    if (own.Count > 0)        ownIds[role]     = own;
                    if (buddy.Count > 0)      buddyIds[role]   = buddy;
                    if (note.Length > 0)      notesByJob[role] = note;
                    if (timingText.Length > 0) timings[role]   = (timingText, timingAid);
                }

                var ts = ParseTimestamp(mech.Timestamp);
                rows.Add((ts, new TankBusterRow(mech.Timestamp, mech.Name, mech.Nickname,
                    ownIds, buddyIds, notesByJob, timings)));
            }
        }
        rows.Sort((a, b) => a.ts.CompareTo(b.ts));
        return rows.Select(r => r.row).ToList();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static (string? t1, string? t2) DetectTankPair()
    {
        var tanks = new List<(uint entityId, string abbrev)>();
        foreach (var m in Plugin.PartyList)
        {
            var job = m.ClassJob.Value;
            if (job.RowId == 0 || job.Role != 1) continue;
            var abbrev = job.Abbreviation.ToString();
            if (!string.IsNullOrEmpty(abbrev)) tanks.Add((m.EntityId, abbrev));
        }
        if (tanks.Count < 2) return (null, null);
        tanks.Sort((a, b) => {
            var pa = Array.IndexOf(TankPriority, a.abbrev);
            var pb = Array.IndexOf(TankPriority, b.abbrev);
            if (pa < 0) pa = TankPriority.Length;
            if (pb < 0) pb = TankPriority.Length;
            return pa.CompareTo(pb);
        });
        return (tanks[0].abbrev, tanks[1].abbrev);
    }

    private static List<TankCombo> FindRelevantTankCombos(
        List<TankCombo> combos, string? t1, string? t2)
    {
        if (t1 == null || t2 == null)
            return combos.Count > 0 ? new List<TankCombo> { combos[0] } : new List<TankCombo>();
        var fwd = $"{t1}{t2}"; var bwd = $"{t2}{t1}";
        var matched = combos.Where(c =>
            c.Id.EndsWith(fwd, StringComparison.OrdinalIgnoreCase) ||
            c.Id.EndsWith(bwd, StringComparison.OrdinalIgnoreCase)).ToList();
        return matched.Count > 0 ? matched : new List<TankCombo> { combos[0] };
    }

    private static string? FindMatchedJob<T>(Dictionary<string, T> dict, string colName, string? targetAbbrev)
    {
        if (targetAbbrev != null)
            foreach (var key in dict.Keys)
                if (JobNameToAbbrev(key).Equals(targetAbbrev, StringComparison.OrdinalIgnoreCase))
                    return key;
        var keys = dict.Keys.ToList();
        if (colName == "Tank 1" && keys.Count >= 1) return keys[0];
        if (colName == "Tank 2" && keys.Count >= 2) return keys[1];
        return null;
    }

    private static uint AbbrevToJobId(string a) => a.ToUpperInvariant() switch
    { "WAR" => 21, "PLD" => 19, "DRK" => 32, "GNB" => 37, "BRD" => 23, "MCH" => 31, "DNC" => 38, _ => 0 };

    private static string JobIdToAbbrev(uint id) => id switch
    { 21 => "WAR", 19 => "PLD", 32 => "DRK", 37 => "GNB", 23 => "BRD", 31 => "MCH", 38 => "DNC", _ => "???" };

    // For compact mode: compare abbrev directly
    private static string JobNameToAbbrev_FromAbbrev(string abbrev) => abbrev;

    private static bool IsPlayerTank(uint jobId) => JobRoleMapper.IsTank(jobId);

    private static string FormatAbility(uint id, AbilityDisplayMode mode)
    {
        if (!AbilityExtraInfoData.AbilitiesInfo.TryGetValue(id, out var info)) return "";
        return mode switch { AbilityDisplayMode.Name => info.Name, _ => info.Nickname };
    }

    private static TimeSpan ParseTimestamp(string ts)
    {
        var p = ts.Split(':');
        if (p.Length == 2 && int.TryParse(p[0], out var m) && int.TryParse(p[1], out var s))
            return new TimeSpan(0, m, s);
        return TimeSpan.Zero;
    }

    private static string JobNameToAbbrev(string j) => j switch
    { "Warrior" => "WAR", "Paladin" => "PLD", "DarkKnight" => "DRK", "Gunbreaker" => "GNB",
      _ => j.ToUpperInvariant() };

    private static uint GetPlayerJobId()
    { var ps = Plugin.PlayerState; if (!ps.IsLoaded || !ps.ClassJob.IsValid) return 0; return ps.ClassJob.RowId; }
}
