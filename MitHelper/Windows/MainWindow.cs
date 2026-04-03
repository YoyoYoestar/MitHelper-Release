using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using MitHelper.Data;

namespace MitHelper.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly MitSheetLoader sheetLoader;
    private readonly Plugin plugin;

    private List<MitSheet> sheets = new();
    private int sheetIndex = 0;
    private int phaseIndex = 0;

    private List<MitSheet> allSheets = new();
    private int pickerIndex = 0;
    private bool manualOverride = false;

    private readonly List<int> columns = new();
    private uint lastDuty = 0;
    private bool wasinCombat = false;

    private readonly IPluginLog _log;

    private const int MaxExtraColumns = 10;
    private static readonly string[] AllColumns = JobRoleMapper.AllColumns;
    private static readonly string[] TankPriority = { "WAR", "GNB", "PLD", "DRK" };

    // Sentinel range: -1=None, -2=TankLb, -3=KitchenSink are OWN abilities (non-buddy)
    // Anything < -3 is a buddy-mit encoded as -(realAbilityId)
    private static bool IsNamedSentinel(int id) => id is -1 or -2 or -3;
    private static bool IsBuddyMit(int id) => id < -3;

    public MainWindow(Plugin plugin, MitSheetLoader sheetLoader, IPluginLog log)
        : base("Mit Helper##MitHelperSheet",
               ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        this.sheetLoader = sheetLoader ?? throw new ArgumentNullException(nameof(sheetLoader));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        allSheets = sheetLoader.LoadAllSheets();
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    public void NextPhase() => ChangePhase(1);
    public void PrevPhase() => ChangePhase(-1);

    private void ChangePhase(int delta)
    {
        if (sheets.Count == 0) return;
        var sheet = sheets[sheetIndex];
        if (sheet?.Phases == null || sheet.Phases.Count == 0) return;
        phaseIndex = Math.Clamp(phaseIndex + delta, 0, sheet.Phases.Count - 1);
       
    }

    private void ResetToPhase1() => phaseIndex = 0;

    public int PhaseIndex => phaseIndex;
    public List<MitSheet> Sheets => sheets;
    public int SheetIndex => sheetIndex;

    public override void Draw()
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        var inCombat = localPlayer?.StatusFlags.HasFlag(StatusFlags.InCombat) ?? false;
        if (wasinCombat && !inCombat) ResetToPhase1();
        wasinCombat = inCombat;

        var territoryId = Plugin.ClientState.TerritoryType;
        if (territoryId != lastDuty)
        {
            lastDuty = territoryId;
            sheets = sheetLoader.LoadSheetsForDuty(territoryId);
            sheetIndex = 0; phaseIndex = 0;
            ResetColumnsForPlayer(); manualOverride = false;
        }

        if (ImGui.Button("Settings") && plugin != null) plugin.ToggleConfigUi();

        if (allSheets.Count > 0 && (sheets.Count == 0 || manualOverride))
        {
            ImGui.SameLine();
            var sheetNames = allSheets.Select(s => s.Name).ToArray();
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("##sheetpicker", ref pickerIndex, sheetNames, sheetNames.Length))
            {
                sheets = new List<MitSheet> { allSheets[pickerIndex] };
                sheetIndex = 0; phaseIndex = 0;
                ResetColumnsForPlayer();
            }
            if (manualOverride)
            {
                ImGui.SameLine();
                if (ImGui.Button("Clear Override"))
                {
                    manualOverride = false;
                    sheets = sheetLoader.LoadSheetsForDuty(territoryId);
                    sheetIndex = 0; phaseIndex = 0;
                    ResetColumnsForPlayer();
                }
            }
        }
        else if (sheets.Count > 0 && !manualOverride)
        {
            ImGui.SameLine();
            if (ImGui.Button("Override Sheet"))
            {
                manualOverride = true; pickerIndex = 0;
                sheets = allSheets.Count > 0 ? new List<MitSheet> { allSheets[0] } : sheets;
                sheetIndex = 0; phaseIndex = 0;
                ResetColumnsForPlayer();
            }
        }

        if (sheets.Count == 0) { ImGui.NewLine(); ImGui.Text("No mitsheets for this duty."); return; }

        var activeSheet = sheets[sheetIndex];
        if (activeSheet == null || activeSheet.Phases == null || activeSheet.Phases.Count == 0)
        { ImGui.NewLine(); ImGui.Text("Sheet has no phases."); return; }
        phaseIndex = Math.Clamp(phaseIndex, 0, activeSheet.Phases.Count - 1);

        ImGui.BeginDisabled(phaseIndex == 0);
        if (ImGui.Button("< Prev Phase")) ChangePhase(-1);
        ImGui.EndDisabled();
        ImGui.SameLine();
        var currentPhase = activeSheet.Phases[phaseIndex];
        if (currentPhase == null) { ImGui.NewLine(); ImGui.Text("Phase data is missing."); return; }
        ImGui.Text($"Phase {phaseIndex + 1}/{activeSheet.Phases.Count}: {currentPhase.Name}");
        ImGui.SameLine();
        ImGui.BeginDisabled(phaseIndex >= activeSheet.Phases.Count - 1);
        if (ImGui.Button("Next Phase >")) ChangePhase(1);
        ImGui.EndDisabled();
        ImGui.SameLine();

        var canAdd = columns.Count < 1 + MaxExtraColumns;
        ImGui.BeginDisabled(!canAdd);
        if (ImGui.Button("+ Add Column")) AddDefaultColumn();
        ImGui.EndDisabled();

        ImGui.Separator();

        using var child = ImRaii.Child("SheetScroll", Vector2.Zero, false, ImGuiWindowFlags.HorizontalScrollbar);
        if (!child.Success) return;

        DrawPhase(activeSheet, currentPhase, -1);
    }

    // ─── DrawPhase ────────────────────────────────────────────────────────────

    private void DrawPhase(MitSheet sheet, Phase phase, int comboOverrideIndex)
    {
        var config      = Plugin.Configuration;
        var playerJobId = GetPlayerJobId();
        var compact     = config.CompactMode;

        var (tank1Abbrev, tank2Abbrev) = DetectTankPairForPhase(phase, comboOverrideIndex);
        var (myColJobId, otherColJobId) = ResolvePartyMitJobIds(playerJobId, config, tank1Abbrev, tank2Abbrev);
        var (tank1Label, tank2Label)   = GetPlayerRelativeTankLabels(playerJobId, config, tank1Abbrev, tank2Abbrev);

        bool separateTankWindow = config.TankMitSeparateWindow && config.ShowTankMits;
        var rows = BuildRows(phase, config.ShowTankMits && !separateTankWindow,
                             playerJobId, tank1Abbrev, tank2Abbrev, comboOverrideIndex);

        if (rows.Count == 0) { ImGui.Text("No mits for this phase."); return; }

        for (int i = 0; i < columns.Count; i++)
            columns[i] = Math.Clamp(columns[i], 0, AllColumns.Length - 1);
        if (columns.Count == 0) { ImGui.Text("No columns selected."); return; }

        var visibleColNames = columns.Select(ci => AllColumns[ci]).ToList();
        var (noteGlossary, rowNoteMap) = BuildNotes(rows, visibleColNames);

        // Column count: compact hides Time; always has Mech + N data cols
        int fixedCols   = compact ? 1 : 2; // Mech only, or Time+Mech
        int tableColCount = fixedCols + columns.Count;

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;
        if (!ImGui.BeginTable("MitTable", tableColCount, tableFlags)) return;

        ImGui.TableSetupScrollFreeze(fixedCols, 1);
        if (!compact) ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Mech", ImGuiTableColumnFlags.WidthFixed, 160);

        // Col[0]: fixed at computed content width. Col[1+]: stretch equally.
        var colWidths = ComputeColumnWidths(rows, columns, playerJobId, config,
                                            tank1Abbrev, tank2Abbrev, myColJobId, otherColJobId, compact);
        for (int ci = 0; ci < columns.Count; ci++)
        {
            if (ci == 0)
                ImGui.TableSetupColumn($"##col{ci}", ImGuiTableColumnFlags.WidthFixed, colWidths[ci]);
            else
                ImGui.TableSetupColumn($"##col{ci}", ImGuiTableColumnFlags.WidthStretch, 1f);
        }

        // ── Header row ──
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        int col = 0;
        if (!compact) { ImGui.TableSetColumnIndex(col++); ImGui.TableHeader("Time"); }
        ImGui.TableSetColumnIndex(col++); ImGui.TableHeader("Mechanic");

        int? removeRequest = null;
        string[] displayColumns = AllColumns
            .Select(c => c == "Tank 1" ? tank1Label : c == "Tank 2" ? tank2Label : c)
            .ToArray();

        for (int ci = 0; ci < columns.Count; ci++)
        {
            ImGui.TableSetColumnIndex(col + ci);
            ImGui.SetNextItemWidth(100);
            var current = columns[ci];
            if (ImGui.Combo($"##hdr{ci}", ref current, displayColumns, displayColumns.Length))
                columns[ci] = current;
            if (columns.Count > 1)
            {
                ImGui.SameLine(0, 2);
                if (ImGui.SmallButton($"X##rm{ci}")) removeRequest = ci;
            }
        }
        if (removeRequest.HasValue) columns.RemoveAt(removeRequest.Value);

        // ── Data rows ──
        float iconH = config.DisplayMode == AbilityDisplayMode.Icon ? IconSize.Y + 4f : 0f;

        for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
        {
            var row = rows[rowIdx];
            if (config.DisplayMode == AbilityDisplayMode.Icon && iconH > 0)
                ImGui.TableNextRow(ImGuiTableRowFlags.None, iconH);
            else
                ImGui.TableNextRow();

            col = 0;
            var tbColor = new Vector4(1f, 0.6f, 0.2f, 1f);

            if (!compact)
            {
                ImGui.TableSetColumnIndex(col++);
                CenterText(row.Timestamp, row.IsTankBuster ? tbColor : null);
            }

            ImGui.TableSetColumnIndex(col++);
            var displayName = compact && row.Nickname != null ? row.Nickname : row.Name;
            CenterText(displayName, row.IsTankBuster ? tbColor : null);

            for (int ci = 0; ci < columns.Count; ci++)
            {
                ImGui.TableSetColumnIndex(col + ci);
                var colName = AllColumns[columns[ci]];

                // Collect timing info for this cell
                var (timingText, timingAid) = GetTimingInfo(row, colName, tank1Abbrev, tank2Abbrev);
                string? timingDisplay = string.IsNullOrEmpty(timingText) ? null
                    : (compact ? CompactTimingLabel(timingText) : timingText);

                if (config.DisplayMode == AbilityDisplayMode.Icon)
                {
                    var abilityIds = GetAbilityIdsForCell(row, colName, playerJobId, config,
                                                          tank1Abbrev, tank2Abbrev, myColJobId, otherColJobId);
                    var buddyIds   = GetBuddyIdsForCell(row, colName, tank1Abbrev, tank2Abbrev);
                    rowNoteMap.TryGetValue((rowIdx, colName), out var noteSuffix);
                    DrawIconCell(abilityIds, buddyIds, timingText, timingAid, noteSuffix, compact);
                }
                else
                {
                    var cellText = GetCellText(row, colName, playerJobId, config,
                                               tank1Abbrev, tank2Abbrev, myColJobId, otherColJobId);
                    rowNoteMap.TryGetValue((rowIdx, colName), out var noteSuffix);
                    if (!string.IsNullOrEmpty(timingDisplay)) cellText += " " + timingDisplay;
                    if (!string.IsNullOrEmpty(noteSuffix))    cellText += noteSuffix;
                    CenterText(cellText, null);
                }
            }
        }

        // ── Notes glossary row ──
        if (noteGlossary.Count > 0)
        {
            ImGui.TableNextRow();
            col = 0;
            if (!compact) { ImGui.TableSetColumnIndex(col++); ImGui.Text(""); }
            ImGui.TableSetColumnIndex(col++);
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.5f, 1f), "Notes");

            for (int ci = 0; ci < columns.Count; ci++)
            {
                ImGui.TableSetColumnIndex(col + ci);
                var colName = AllColumns[columns[ci]];
                var notesForCol = noteGlossary
                    .Where(kv => kv.Key.ColName == colName)
                    .OrderBy(kv => kv.Value.StarCount)
                    .Select(kv => $"{kv.Value.Stars} {kv.Value.NoteText}")
                    .ToList();
                if (notesForCol.Count > 0)
                    ImGui.TextWrapped(string.Join("\n", notesForCol));
            }
        }

        ImGui.EndTable();
    }

    // ─── Centering ────────────────────────────────────────────────────────────

    private static void CenterCursorForWidth(float w)
    {
        var off = (ImGui.GetColumnWidth() - w) * 0.5f;
        if (off > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + off);
    }

    private static void CenterText(string text, Vector4? color)
    {
        CenterCursorForWidth(ImGui.CalcTextSize(text).X);
        if (color.HasValue) ImGui.TextColored(color.Value, text);
        else ImGui.TextUnformatted(text);
    }

    // ─── Icon rendering ───────────────────────────────────────────────────────

    private static readonly Vector2 IconSize = new(24, 24);

    private static void DrawIconCell(List<uint> abilityIds, List<uint> buddyIds,
                                     string? timingText, int timingAid,
                                     string? noteSuffix, bool compact)
    {
        if (abilityIds.Count == 0 && buddyIds.Count == 0)
        {
            CenterText("-", null);
            return;
        }

        string buddyLabel = BuddyLabel(compact);
        string? displayTiming = string.IsNullOrEmpty(timingText) ? null
            : (compact ? CompactTimingLabel(timingText) : timingText);

        const float IW = 24f, IG = 2f, BG = 4f, PLG = 2f;
        float blW  = buddyIds.Count > 0 ? ImGui.CalcTextSize(buddyLabel).X : 0f;

        // Pre-calculate timing text widths for centering
        // Whole-cell timing (aID==-1 or no specific match) goes at the end
        // Per-icon timing goes before its icon
        bool wholeCell = displayTiming != null && (timingAid == -1 || !AbilityMatchesTiming(abilityIds, buddyIds, timingAid));
        float timingW  = (displayTiming != null && wholeCell) ? ImGui.CalcTextSize(displayTiming).X + 2f : 0f;

        float totalW = 0f;
        if (abilityIds.Count > 0)
            totalW += abilityIds.Count * IW + Math.Max(0, abilityIds.Count - 1) * IG;
        if (buddyIds.Count > 0)
        {
            if (abilityIds.Count > 0) totalW += BG;
            totalW += blW + PLG + buddyIds.Count * IW + Math.Max(0, buddyIds.Count - 1) * IG;
        }
        // Add per-icon timing widths
        if (displayTiming != null && !wholeCell)
        {
            float tw = ImGui.CalcTextSize(displayTiming).X + 2f;
            totalW += tw; // shown once before the matching icon
        }
        totalW += timingW;

        CenterCursorForWidth(totalW);

        bool first = true;

        void RenderIcon(uint id)
        {
            if (!first) ImGui.SameLine(0, IG);
            first = false;
            if (!AbilityExtraInfoData.AbilitiesInfo.TryGetValue(id, out var info)) return;
            if (info.IconId == 0) { ImGui.Text(info.Nickname); return; }
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(info.IconId)).GetWrapOrEmpty();
            ImGui.Image(wrap.Handle, IconSize);
            if (ImGui.IsItemHovered())
            { ImGui.BeginTooltip(); ImGui.Text(info.Nickname); ImGui.EndTooltip(); }
        }

        void MaybeRenderTimingBefore(uint id)
        {
            if (displayTiming == null || wholeCell) return;
            // Show timing before the ability whose ID matches timingAid
            uint matchId = timingAid < 0 ? (uint)Math.Abs(timingAid) : (uint)timingAid;
            if (id == matchId)
            {
                if (!first) ImGui.SameLine(0, 2);
                first = false;
                ImGui.TextDisabled(displayTiming);
            }
        }

        foreach (var id in abilityIds)
        {
            MaybeRenderTimingBefore(id);
            RenderIcon(id);
        }

        if (buddyIds.Count > 0)
        {
            if (!first) ImGui.SameLine(0, BG);
            first = false;
            ImGui.TextDisabled(buddyLabel);
            foreach (var id in buddyIds)
            {
                MaybeRenderTimingBefore(id);
                ImGui.SameLine(0, PLG);
                RenderIcon(id);
            }
        }

        if (wholeCell && !string.IsNullOrEmpty(displayTiming))
        { ImGui.SameLine(0, 2); ImGui.TextDisabled(displayTiming); }

        if (!string.IsNullOrEmpty(noteSuffix))
        { ImGui.SameLine(0, 2); ImGui.TextDisabled(noteSuffix); }
    }

    /// Returns true if the timingAid matches any ability in the cell (for per-icon placement).
    private static bool AbilityMatchesTiming(List<uint> abilityIds, List<uint> buddyIds, int timingAid)
    {
        if (timingAid == -1) return false;
        uint matchId = (uint)Math.Abs(timingAid);
        return abilityIds.Contains(matchId) || buddyIds.Contains(matchId);
    }

    // ─── Notes & timing ──────────────────────────────────────────────────────

    private record NoteKey(string ColName, string NoteText);
    private record NoteGlossaryEntry(string NoteText, string Stars, int StarCount);

    /// Build per-row inline note suffixes ("*", "**") and the glossary for the footer.
    /// Now covers both regular mechanic rows AND tank-buster rows.
    private static (Dictionary<NoteKey, NoteGlossaryEntry> glossary,
                    Dictionary<(int rowIdx, string colName), string> rowMap)
        BuildNotes(List<SheetRow> rows, List<string> visibleColNames)
    {
        var glossary = new Dictionary<NoteKey, NoteGlossaryEntry>();
        var rowMap   = new Dictionary<(int, string), string>();
        var colIdx   = new Dictionary<string, Dictionary<string, int>>();
        foreach (var col in visibleColNames)
            colIdx[col] = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            foreach (var colName in visibleColNames)
            {
                string? note = null;
                if (!row.IsTankBuster && row.Mechanic != null)
                    note = GetNoteFromMits(row.Mechanic.Mits, colName);
                else if (row.IsTankBuster && row.TankBuster != null)
                    note = row.TankBuster.NotesByJob.TryGetValue(colName, out var n) ? n : null;

                if (string.IsNullOrWhiteSpace(note)) continue;
                if (!colIdx.TryGetValue(colName, out var idx)) continue;

                if (!idx.TryGetValue(note, out var starCount))
                {
                    starCount = idx.Count + 1;
                    idx[note] = starCount;
                    var stars = new string('*', starCount);
                    glossary[new NoteKey(colName, note)] =
                        new NoteGlossaryEntry(note, stars, starCount);
                }
                rowMap[(i, colName)] = new string('*', starCount);
            }
        }
        return (glossary, rowMap);
    }

    private static string? GetNoteFromMits(Dictionary<string, List<MitEntry>> mits, string col)
    {
        if (!mits.TryGetValue(col, out var entries)) return null;
        foreach (var e in entries)
            if (!string.IsNullOrWhiteSpace(e.Note)) return e.Note!;
        return null;
    }

    /// Returns the timing info for a cell: (text, actionId).
    /// aID == -1 means whole-cell (show at end). Other aIDs mean "show before that ability".
    /// Returns (null, -1) when there is no timing.
    private static (string? Text, int ActionId) GetTimingInfo(SheetRow row, string colName,
                                                               string? tank1Abbrev, string? tank2Abbrev)
    {
        // Tank-buster rows: look up timing by the matched job role
        if (row.IsTankBuster && row.TankBuster != null)
        {
            if (colName is not ("Tank 1" or "Tank 2")) return (null, -1);
            var targetAbbrev = colName == "Tank 1" ? tank1Abbrev : tank2Abbrev;
            var matchedJob   = FindMatchedJobInDict(row.TankBuster.TimingByJob, colName, targetAbbrev);
            if (matchedJob != null && row.TankBuster.TimingByJob.TryGetValue(matchedJob, out var t))
                return (t.Text, t.ActionId);
            return (null, -1);
        }

        // Regular mechanic rows
        if (row.Mechanic == null) return (null, -1);
        if (!row.Mechanic.Mits.TryGetValue(colName, out var entries)) return (null, -1);
        foreach (var e in entries)
            if (e.Timing != null && !string.IsNullOrWhiteSpace(e.Timing.Text))
                return (e.Timing.Text, e.Timing.ActionId);
        return (null, -1);
    }

    /// Shortens a timing label for compact mode: "(Late)"→"(L)", "(Early)"→"(E)", etc.
    private static string CompactTimingLabel(string label) =>
        label.Replace("(Late)", "(L)").Replace("(Early)", "(E)");

    /// Returns the "Buddy Mit:" label text, shortened in compact mode.
    private static string BuddyLabel(bool compact) => compact ? "Buddy:" : "Buddy Mit:";

    // ─── Column width computation ─────────────────────────────────────────────

    private List<float> ComputeColumnWidths(List<SheetRow> rows, List<int> colIndices,
        uint playerJobId, Configuration config,
        string? tank1Abbrev, string? tank2Abbrev,
        uint myColJobId, uint otherColJobId, bool compact)
    {
        const float IW = 24f, IG = 2f, BG = 4f, PLG = 2f, CellPad = 12f;
        const float MinW = 130f, HeaderW = 122f;
        float blW = ImGui.CalcTextSize(BuddyLabel(compact)).X;

        var widths = new List<float>();
        foreach (var ci in colIndices)
        {
            var colName = AllColumns[ci];
            float maxW = Math.Max(MinW, HeaderW);

            if (config.DisplayMode == AbilityDisplayMode.Icon)
            {
                foreach (var row in rows)
                {
                    var own    = GetAbilityIdsForCell(row, colName, playerJobId, config,
                                                      tank1Abbrev, tank2Abbrev, myColJobId, otherColJobId);
                    var buddy  = GetBuddyIdsForCell(row, colName, tank1Abbrev, tank2Abbrev);
                    float w = 0f;
                    if (own.Count > 0)   w += own.Count * IW + Math.Max(0, own.Count - 1) * IG;
                    if (buddy.Count > 0)
                    {
                        if (own.Count > 0) w += BG;
                        w += blW + PLG + buddy.Count * IW + Math.Max(0, buddy.Count - 1) * IG;
                    }
                    w += CellPad;
                    if (w > maxW) maxW = w;
                }
            }
            else
            {
                maxW = Math.Max(maxW, 160f);
            }
            widths.Add(maxW);
        }
        return widths;
    }

    // ─── Row model ────────────────────────────────────────────────────────────

    private record SheetRow(
        string Timestamp, string Name, string? Nickname, bool IsTankBuster,
        Mechanic? Mechanic, TankBusterEntry? TankBuster);

    /// Tank-buster entry: own IDs per job-name key (buddy mits stored separately)
    private record TankBusterEntry(
        string MechanicName,
        Dictionary<string, List<int>> OwnIds,            // positive + named-sentinel ids
        Dictionary<string, List<uint>> BuddyIds,         // abs(negative) ids per job
        Dictionary<string, string> NotesByJob,           // job-name → note text
        Dictionary<string, (string Text, int ActionId)> TimingByJob  // job-name → (text, aID)
    );

    private List<SheetRow> BuildRows(Phase phase, bool showTankMits,
        uint playerJobId, string? tank1Abbrev, string? tank2Abbrev,
        int comboOverrideIndex = -1)
    {
        var rows = new List<(TimeSpan ts, SheetRow row)>();

        foreach (var mech in phase.Mechanics)
            rows.Add((ParseTimestamp(mech.Timestamp), new SheetRow(mech.Timestamp, mech.Name, mech.Nickname, false, mech, null)));

        if (showTankMits && phase.TankCombos != null)
        {
            List<TankCombo> combos = (comboOverrideIndex >= 0 && comboOverrideIndex < phase.TankCombos.Count)
                ? new List<TankCombo> { phase.TankCombos[comboOverrideIndex] }
                : FindRelevantTankCombos(phase.TankCombos, tank1Abbrev, tank2Abbrev);

            foreach (var combo in combos)
            {
                foreach (var tbMech in combo.TankMits)
                {
                    var ownIds   = new Dictionary<string, List<int>>();
                    var buddyIds = new Dictionary<string, List<uint>>();
                    var notes    = new Dictionary<string, string>();
                    var timings  = new Dictionary<string, (string Text, int ActionId)>();

                    foreach (var (role, entries) in tbMech.Mits)
                    {
                        var own   = new List<int>();
                        var buddy = new List<uint>();
                        string roleNote   = "";
                        string roleTimingText = "";
                        int    roleTimingAid  = -1;

                        foreach (var e in entries)
                        {
                            if (e.ActionIds != null)
                                foreach (var id in e.ActionIds)
                                {
                                    if (IsBuddyMit(id))
                                        buddy.Add((uint)Math.Abs(id));
                                    else if (id == (int)Abilities.NascentFlash ||
                                             id == (int)Abilities.Intervention)
                                        buddy.Add((uint)id); // treat as buddy mit
                                    else
                                        own.Add(id);
                                }
                            if (!string.IsNullOrWhiteSpace(e.Note))
                                roleNote = e.Note!;
                            if (e.Timing != null && !string.IsNullOrWhiteSpace(e.Timing.Text))
                            { roleTimingText = e.Timing.Text; roleTimingAid = e.Timing.ActionId; }
                        }
                        if (own.Count > 0)        ownIds[role]   = own;
                        if (buddy.Count > 0)      buddyIds[role] = buddy;
                        if (roleNote.Length > 0)  notes[role]    = roleNote;
                        if (roleTimingText.Length > 0) timings[role] = (roleTimingText, roleTimingAid);
                    }

                    var ts = ParseTimestamp(tbMech.Timestamp);
                    rows.Add((ts, new SheetRow(tbMech.Timestamp, tbMech.Name, tbMech.Nickname, true, null,
                        new TankBusterEntry(tbMech.Name, ownIds, buddyIds, notes, timings))));
                }
            }
        }

        rows.Sort((a, b) => a.ts.CompareTo(b.ts));
        return rows.Select(r => r.row).ToList();
    }

    // ─── Tank detection ───────────────────────────────────────────────────────

    private static (string? t1, string? t2) DetectTankPairForPhase(Phase phase, int comboOverrideIndex)
    {
        if (comboOverrideIndex >= 0 && phase.TankCombos != null
            && comboOverrideIndex < phase.TankCombos.Count)
        {
            var parts = phase.TankCombos[comboOverrideIndex].Id.Split('-');
            var pair  = parts.Last();
            if (pair.Length == 6) return (pair[..3], pair[3..]);
        }
        return DetectTankPair();
    }

    private static (string? t1, string? t2) DetectTankPair()
    {
        var tanks = new List<(uint entityId, string abbrev)>();
        foreach (var member in Plugin.PartyList)
        {
            var job = member.ClassJob.Value;
            if (job.RowId == 0 || job.Role != 1) continue;
            var abbrev = job.Abbreviation.ToString();
            if (!string.IsNullOrEmpty(abbrev)) tanks.Add((member.EntityId, abbrev));
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

    private static (uint myColJobId, uint otherColJobId) ResolvePartyMitJobIds(
        uint playerJobId, Configuration config, string? t1, string? t2)
    {
        if (!JobRoleMapper.IsTank(playerJobId)) return (playerJobId, 0);
        uint other = 0;
        var local = Plugin.ObjectTable.LocalPlayer;
        foreach (var m in Plugin.PartyList)
        {
            if (local != null && m.EntityId == local.EntityId) continue;
            var job = m.ClassJob.Value;
            if (job.Role != 1) continue;
            other = job.RowId; break;
        }
        return (playerJobId, other);
    }

    private static (string t1, string t2) GetPlayerRelativeTankLabels(
        uint playerJobId, Configuration config, string? t1Abbrev, string? t2Abbrev)
    {
        if (t1Abbrev == null || t2Abbrev == null) return ("Tank 1", "Tank 2");
        if (!JobRoleMapper.IsTank(playerJobId)) return (t1Abbrev, t2Abbrev);

        var playerAbbrev = JobIdToAbbrev(playerJobId);
        var (playerCol, _) = JobRoleMapper.GetColumn(playerJobId, config.TankDefaultSwap, config.MeleeDefaultSwap);

        uint otherJobId = 0;
        var local = Plugin.ObjectTable.LocalPlayer;
        foreach (var m in Plugin.PartyList)
        {
            if (local != null && m.EntityId == local.EntityId) continue;
            if (m.ClassJob.Value.Role != 1) continue;
            otherJobId = m.ClassJob.Value.RowId; break;
        }
        var otherAbbrev = otherJobId != 0 ? JobIdToAbbrev(otherJobId)
                        : (playerCol == "Tank 1" ? t2Abbrev : t1Abbrev);

        return playerCol == "Tank 1"
            ? (playerAbbrev, otherAbbrev)
            : (otherAbbrev, playerAbbrev);
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

    // ─── Cell data ────────────────────────────────────────────────────────────

    private List<uint> GetAbilityIdsForCell(SheetRow row, string colName,
        uint playerJobId, Configuration config,
        string? t1, string? t2, uint myColJobId, uint otherColJobId)
    {
        var result = new List<uint>();
        if (row.IsTankBuster && row.TankBuster != null)
            CollectTankBusterOwnIds(row.TankBuster, colName, t1, t2, result);
        else if (row.Mechanic != null)
            CollectMechanicIds(row.Mechanic, colName, playerJobId, config, myColJobId, otherColJobId, result);
        return result;
    }

    private static List<uint> GetBuddyIdsForCell(SheetRow row, string colName,
        string? t1, string? t2)
    {
        if (!row.IsTankBuster || row.TankBuster == null) return new List<uint>();
        if (colName is not ("Tank 1" or "Tank 2")) return new List<uint>();

        var targetAbbrev = colName == "Tank 1" ? t1 : t2;
        var matchedJob   = FindMatchedJobInDict(row.TankBuster.BuddyIds, colName, targetAbbrev);
        if (matchedJob == null || !row.TankBuster.BuddyIds.TryGetValue(matchedJob, out var ids))
            return new List<uint>();
        return ids.ToList();
    }

    private static void CollectTankBusterOwnIds(TankBusterEntry tb, string colName,
        string? t1, string? t2, List<uint> result)
    {
        if (colName is not ("Tank 1" or "Tank 2")) return;

        var targetAbbrev = colName == "Tank 1" ? t1 : t2;
        var matchedJob   = FindMatchedJobInDict(tb.OwnIds, colName, targetAbbrev);
        if (matchedJob == null || !tb.OwnIds.TryGetValue(matchedJob, out var rawIds)) return;

        uint colJobId = targetAbbrev != null ? AbbrevToJobId(targetAbbrev) : 0;

        foreach (var id in rawIds)
        {
            if (id == 0) // party-mit placeholder
            {
                if (colJobId != 0 && JobRoleMapper.PartyMitsTanks.TryGetValue(colJobId, out var tankMitId))
                    result.Add(tankMitId);
            }
            else if (id == -1) { /* None */ }
            else if (id > 0)
                result.Add((uint)id);
            else // named sentinels: -2 TankLb, -3 KitchenSink
            {
                var uid = (uint)id;
                if (AbilityExtraInfoData.AbilitiesInfo.ContainsKey(uid))
                    result.Add(uid);
            }
        }
    }

    private void CollectMechanicIds(Mechanic mech, string colName,
        uint playerJobId, Configuration config,
        uint myColJobId, uint otherColJobId, List<uint> result)
    {
        var (playerCol, _) = JobRoleMapper.GetColumn(playerJobId, config.TankDefaultSwap, config.MeleeDefaultSwap);
        var ids = GetIdsForColumn(mech.Mits, colName);

        bool isTankCol  = colName is "Tank 1" or "Tank 2";
        bool isMyCol    = colName == playerCol;
        bool isPhysRng  = colName == "Phys Range";

        if (isMyCol && JobRoleMapper.HasExtras(playerJobId))
        {
            var extras = GetIdsForColumn(mech.Mits, "Extras");
            if (extras.Count > 0 && JobRoleMapper.ExtrasMap.TryGetValue(playerJobId, out var xid)
                && extras.Contains((int)xid))
                ids.Add((int)xid);
        }

        uint colJobId = 0;
        if (isTankCol)
        {
            colJobId = isMyCol ? myColJobId : otherColJobId;
            if (!isMyCol && colJobId != 0 && JobRoleMapper.HasExtras(colJobId))
            {
                var extras = GetIdsForColumn(mech.Mits, "Extras");
                if (extras.Count > 0 && JobRoleMapper.ExtrasMap.TryGetValue(colJobId, out var xid)
                    && extras.Contains((int)xid))
                    ids.Add((int)xid);
            }
        }

        foreach (var id in ids)
        {
            if (id == 0)
            {
                if (isTankCol && colJobId != 0 && JobRoleMapper.PartyMitsTanks.TryGetValue(colJobId, out var m))
                    result.Add(m);
                else if (isPhysRng)
                {
                    var pjid = JobRoleMapper.PartyMitsPhysicalRanged.ContainsKey(playerJobId)
                             ? playerJobId : FindPhysRangeJobId();
                    if (pjid != 0 && JobRoleMapper.PartyMitsPhysicalRanged.TryGetValue(pjid, out var rm))
                        result.Add(rm);
                }
            }
            else if (id == -1) { }
            else if (id > 0) result.Add((uint)id);
            else
            {
                var uid = (uint)id;
                if (AbilityExtraInfoData.AbilitiesInfo.ContainsKey(uid)) result.Add(uid);
            }
        }
    }

    // ─── Text cell ────────────────────────────────────────────────────────────

    private string GetCellText(SheetRow row, string colName,
        uint playerJobId, Configuration config,
        string? t1, string? t2, uint myColJobId, uint otherColJobId)
    {
        if (row.IsTankBuster && row.TankBuster != null)
            return GetTankBusterCellText(row.TankBuster, colName, t1, t2, config);
        if (row.Mechanic == null) return "";
        return GetMechanicCellText(row.Mechanic, colName, playerJobId, config, myColJobId, otherColJobId);
    }

    private string GetMechanicCellText(Mechanic mech, string colName,
        uint playerJobId, Configuration config, uint myColJobId, uint otherColJobId)
    {
        var ids = new List<uint>();
        CollectMechanicIds(mech, colName, playerJobId, config, myColJobId, otherColJobId, ids);
        if (ids.Count == 0) return "-";
        var parts = ids.Select(id => FormatAbility(id, config.DisplayMode)).Where(s => s.Length > 0).ToList();
        return parts.Count > 0 ? string.Join("/", parts) : "-";
    }

    private string GetTankBusterCellText(TankBusterEntry tb, string colName,
        string? t1, string? t2, Configuration config)
    {
        if (colName is not ("Tank 1" or "Tank 2")) return "";

        var targetAbbrev = colName == "Tank 1" ? t1 : t2;
        var matchedJob   = FindMatchedJobInDict(tb.OwnIds, colName, targetAbbrev);
        if (matchedJob == null) return "-";

        var parts = new List<string>();

        if (tb.OwnIds.TryGetValue(matchedJob, out var ownRaw))
        {
            uint colJobId = targetAbbrev != null ? AbbrevToJobId(targetAbbrev) : 0;
            foreach (var id in ownRaw)
            {
                if (id == 0)
                { if (colJobId != 0 && JobRoleMapper.PartyMitsTanks.TryGetValue(colJobId, out var m)) parts.Add(FormatAbility(m, config.DisplayMode)); }
                else if (id == -1) { }
                else if (id > 0) { var s = FormatAbility((uint)id, config.DisplayMode); if (s.Length > 0) parts.Add(s); }
                else { var uid = (uint)id; if (AbilityExtraInfoData.AbilitiesInfo.ContainsKey(uid)) parts.Add(FormatAbility(uid, config.DisplayMode)); }
            }
        }

        if (tb.BuddyIds.TryGetValue(matchedJob, out var buddyList) && buddyList.Count > 0)
        {
            var bp = buddyList.Select(id => FormatAbility(id, config.DisplayMode)).Where(s => s.Length > 0).ToList();
            if (bp.Count > 0) parts.Add("Buddy: " + string.Join("/", bp));
        }

        return parts.Count > 0 ? string.Join("/", parts) : "-";
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string? FindMatchedJobInDict<T>(
        Dictionary<string, T> dict, string colName, string? targetAbbrev)
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
    { "WAR" => 21, "PLD" => 19, "DRK" => 32, "GNB" => 37,
      "BRD" => 23, "MCH" => 31, "DNC" => 38, _ => 0 };

    private static string JobIdToAbbrev(uint id) => id switch
    { 21 => "WAR", 19 => "PLD", 32 => "DRK", 37 => "GNB",
      23 => "BRD", 31 => "MCH", 38 => "DNC", _ => "???" };

    private static uint FindPhysRangeJobId()
    {
        var ps = Plugin.PlayerState;
        if (ps.IsLoaded && ps.ClassJob.IsValid &&
            JobRoleMapper.PartyMitsPhysicalRanged.ContainsKey(ps.ClassJob.RowId))
            return ps.ClassJob.RowId;
        foreach (var m in Plugin.PartyList)
        {
            var jid = m.ClassJob.Value.RowId;
            if (JobRoleMapper.PartyMitsPhysicalRanged.ContainsKey(jid)) return jid;
        }
        return 0;
    }

    private static List<int> GetIdsForColumn(Dictionary<string, List<MitEntry>> mits, string col)
    {
        if (!mits.TryGetValue(col, out var entries)) return new List<int>();
        var ids = new List<int>();
        foreach (var e in entries) if (e.ActionIds != null) ids.AddRange(e.ActionIds);
        return ids;
    }

    private static string FormatAbility(uint id, AbilityDisplayMode mode)
    {
        if (id == Abilities.None) return "-";
        if (!AbilityExtraInfoData.AbilitiesInfo.TryGetValue(id, out var info)) return "";
        return mode switch { AbilityDisplayMode.Name => info.Name, _ => info.Nickname };
    }

    private static TimeSpan ParseTimestamp(string ts)
    {
        if (string.IsNullOrWhiteSpace(ts)) return TimeSpan.Zero;
        var p = ts.Split(':');
        if (p.Length == 2 && int.TryParse(p[0], out var m) && int.TryParse(p[1], out var s))
            return new TimeSpan(0, m, s);
        return TimeSpan.Zero;
    }

    private static string JobNameToAbbrev(string j) => j switch
    { "Warrior" => "WAR", "Paladin" => "PLD", "DarkKnight" => "DRK", "Gunbreaker" => "GNB",
      _ => j.ToUpperInvariant() };

    private void ResetColumnsForPlayer()
    {
        columns.Clear();
        var role = GetPlayerRoleColumn();
        var idx  = Array.IndexOf(AllColumns, role);
        columns.Add(idx >= 0 ? idx : 0);
    }

    private void AddDefaultColumn()
    {
        if (columns.Count >= 1 + MaxExtraColumns) return;
        foreach (var col in AllColumns)
        {
            var idx = Array.IndexOf(AllColumns, col);
            if (!columns.Contains(idx)) { columns.Add(idx); return; }
        }
        columns.Add(0);
    }

    private static string GetPlayerRoleColumn()
    {
        var ps = Plugin.PlayerState;
        if (!ps.IsLoaded || !ps.ClassJob.IsValid) return "Extras";
        var config = Plugin.Configuration;
        var jobId  = ps.ClassJob.RowId;
        if (JobRoleMapper.IsTank(jobId) || jobId is 20 or 22 or 30 or 34 or 39 or 41)
        {
            var (col, _) = JobRoleMapper.GetColumn(jobId, config.TankDefaultSwap, config.MeleeDefaultSwap);
            return col;
        }
        return ps.ClassJob.Value.Abbreviation.ToString() switch
        {
            "SCH" => "Scholar", "SGE" => "Sage", "WHM" => "White Mage", "AST" => "Astro",
            "BRD" or "MCH" or "DNC" => "Phys Range",
            "BLM" or "SMN" or "RDM" or "PCT" => "Caster",
            _ => "Extras"
        };
    }

    private static uint GetPlayerJobId()
    {
        var ps = Plugin.PlayerState;
        if (!ps.IsLoaded || !ps.ClassJob.IsValid) return 0;
        return ps.ClassJob.RowId;
    }
}
