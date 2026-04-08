using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MitHelper.Data;

namespace MitHelper.Windows;

public class EditorWindow : Window, IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly MitSheetLoader         loader;
    private readonly IPluginLog              log;
    private readonly ITextureProvider        tex;

    private EditorSheet sheet      = new();
    private int phaseIdx           = -1;
    private int mechIdx            = -1;
    private bool editingTb         = false;
    private int comboIdx           = 0;

    private string saveStatus      = "";
    private float  _saveStatusTimer = 0f;

    // Ability picker
    private string  abilitySearch  = "";
    private string? pickerCol      = null;
    private bool    openPicker     = false;
    private bool    showAllAbilities = false;

    // Sheet loader
    private List<(string Path, string Name)> loadList = new();
    private bool showLoad = false;

    private static readonly Vector2 IconSz = new(22, 22);

    public EditorWindow(IDalamudPluginInterface pi, MitSheetLoader loader,
                        IPluginLog log, ITextureProvider tex)
        : base("MitHelper Editor##MHEditor",
               ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.pi = pi; this.loader = loader; this.log = log; this.tex = tex;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(960, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawToolbar();
        ImGui.Separator();

        float avail   = ImGui.GetContentRegionAvail().X;
        float leftW   = 215f;
        float centerW = 280f;
        float rightW  = avail - leftW - centerW - 16f;

        using (var l = ImRaii.Child("##L", new Vector2(leftW,   0), false)) { if (l.Success) DrawLeft();   }
        ImGui.SameLine(0, 4); VerticalSeparator(); ImGui.SameLine(0, 4);
        using (var c = ImRaii.Child("##C", new Vector2(centerW, 0), false)) { if (c.Success) DrawCenter(); }
        ImGui.SameLine(0, 4); VerticalSeparator(); ImGui.SameLine(0, 4);
        using (var r = ImRaii.Child("##R", new Vector2(rightW,  0), false)) { if (r.Success) DrawRight();  }

        DrawAbilityPicker();

        // Toast-style save status
        if (_saveStatusTimer > 0)
        {
            _saveStatusTimer -= ImGui.GetIO().DeltaTime;
            var vp = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(vp.WorkPos + new Vector2(20, vp.WorkSize.Y - 44));
            ImGui.SetNextWindowBgAlpha(0.82f);
            ImGui.Begin("##savestatus",
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove);
            ImGui.TextUnformatted(saveStatus);
            ImGui.End();
        }
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("New Sheet"))
        { sheet = new EditorSheet(); phaseIdx = -1; mechIdx = -1; }
        ImGui.SameLine();

        if (ImGui.Button("Open Sheet…"))
        { loadList = SheetSerializer.ListSheets(loader); showLoad = true; }

        if (showLoad) { ImGui.OpenPopup("##loadpop"); showLoad = false; }
        if (ImGui.BeginPopup("##loadpop"))
        {
            ImGui.Text("Available sheets:");
            ImGui.Separator();
            if (loadList.Count == 0) ImGui.TextDisabled("None found.");
            foreach (var (path, name) in loadList)
            {
                if (!ImGui.Selectable(name)) continue;
                var loaded = SheetSerializer.LoadFromFile(path, log);
                if (loaded != null)
                {
                    sheet    = loaded;
                    phaseIdx = sheet.Phases.Count > 0 ? 0 : -1;
                    mechIdx  = -1; editingTb = false;
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Save"))
        {
            try
            {
                SheetSerializer.SaveToPluginFolder(sheet, pi, log);
                ShowStatus($"✓ Saved {sheet.SafeFileName}");
            }
            catch (Exception ex) { ShowStatus($"✗ {ex.Message}"); }
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"  {sheet.Name}  |  Duty {sheet.Duty}");
    }

    private void DrawLeft()
    {
        SectionLabel("Sheet");
        ImGui.SetNextItemWidth(-1); ImGui.InputText("##sn",   ref sheet.Name,        64);
        ImGui.SetNextItemWidth(-1); ImGui.InputText("##sd",   ref sheet.Description, 128);
        ImGui.Text("Duty ID:"); ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        var di = (int)sheet.Duty;
        if (ImGui.InputInt("##duty", ref di, 0)) sheet.Duty = (uint)Math.Max(0, di);

        ImGui.Spacing();
        SectionLabel("Phases");

        for (int i = 0; i < sheet.Phases.Count; i++)
        {
            var ph  = sheet.Phases[i];
            bool sel = i == phaseIdx;
            if (ImGui.Selectable($"{ph.Name}##ph{i}", sel))
            { phaseIdx = i; mechIdx = -1; editingTb = false; }
            ImGui.SameLine(ImGui.GetContentRegionMax().X - 36);
            ImGui.BeginDisabled(i == 0);
            if (SmallBtn($"↑##phu{i}"))
            { Swap(sheet.Phases, i-1, i); phaseIdx = i-1; }
            ImGui.EndDisabled();
            ImGui.SameLine(0, 2);
            ImGui.BeginDisabled(i == sheet.Phases.Count - 1);
            if (SmallBtn($"↓##phd{i}"))
            { Swap(sheet.Phases, i, i+1); phaseIdx = i+1; }
            ImGui.EndDisabled();
        }

        ImGui.Spacing();
        if (ImGui.Button("+ Add Phase", new Vector2(-1, 0)))
        {
            int n = sheet.Phases.Count + 1;
            sheet.Phases.Add(new EditorPhase { Id = $"P{n}", Name = $"Phase {n}" });
            phaseIdx = sheet.Phases.Count - 1; mechIdx = -1;
        }

        if (phaseIdx >= 0 && phaseIdx < sheet.Phases.Count)
        {
            if (ImGui.Button("Remove Phase", new Vector2(-1, 0)))
            {
                sheet.Phases.RemoveAt(phaseIdx);
                phaseIdx = Math.Max(0, Math.Min(phaseIdx, sheet.Phases.Count - 1));
                if (sheet.Phases.Count == 0) phaseIdx = -1;
                mechIdx = -1;
            }
            ImGui.Spacing(); ImGui.Separator();
            SectionLabel("Phase Settings");
            var ph = sheet.Phases[phaseIdx];
            ImGui.Text("Name:"); ImGui.SameLine(); ImGui.SetNextItemWidth(-1); ImGui.InputText("##phn",  ref ph.Name, 64);
            ImGui.Text("ID:");   ImGui.SameLine(); ImGui.SetNextItemWidth(-1); ImGui.InputText("##phid", ref ph.Id,   32);
            ImGui.Checkbox("Has Tank Busters##htc", ref ph.HasTankCombos);
        }
    }
    

    private void DrawCenter()
    {
        if (phaseIdx < 0 || phaseIdx >= sheet.Phases.Count)
        { ImGui.TextDisabled("← Select a phase."); return; }
        var phase = sheet.Phases[phaseIdx];

        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs.Success) return;
        
        using (var t = ImRaii.TabItem("Party Mechs"))
        {
            if (t.Success)
            {
                for (int i = 0; i < phase.Mechanics.Count; i++)
                {
                    var m   = phase.Mechanics[i];
                    bool sel = !editingTb && i == mechIdx;
                    if (ImGui.Selectable($"[{m.Timestamp}] {m.Name}##m{i}", sel))
                    { mechIdx = i; editingTb = false; }
                    ImGui.SameLine(ImGui.GetContentRegionMax().X - 36);
                    ImGui.BeginDisabled(i == 0);
                    if (SmallBtn($"↑##mu{i}")) { Swap(phase.Mechanics, i-1, i); if (!editingTb) mechIdx = i-1; }
                    ImGui.EndDisabled();
                    ImGui.SameLine(0,2);
                    ImGui.BeginDisabled(i == phase.Mechanics.Count - 1);
                    if (SmallBtn($"↓##md{i}")) { Swap(phase.Mechanics, i, i+1); if (!editingTb) mechIdx = i+1; }
                    ImGui.EndDisabled();
                }
                ImGui.Spacing();
                if (ImGui.Button("+ Add Mechanic", new Vector2(-1,0)))
                {
                    phase.Mechanics.Add(new EditorMechanic { Id = $"{phase.Id}-M{phase.Mechanics.Count+1:D2}" });
                    mechIdx = phase.Mechanics.Count - 1; editingTb = false;
                }
                if (!editingTb && mechIdx >= 0 && mechIdx < phase.Mechanics.Count)
                    if (ImGui.Button("Remove##rmm", new Vector2(-1,0)))
                    { phase.Mechanics.RemoveAt(mechIdx); mechIdx = Clamp(mechIdx-1, phase.Mechanics.Count); }
            }
        }
        
        if (!phase.HasTankCombos) return;

        using (var t2 = ImRaii.TabItem("Tank Busters"))
        {
            if (!t2.Success) return;

            // Combo picker
            if (phase.TankCombos.Count > 0)
            {
                var names = phase.TankCombos.Select(c => c.ComboLabel).ToArray();
                comboIdx = Math.Clamp(comboIdx, 0, phase.TankCombos.Count - 1);
                ImGui.SetNextItemWidth(-1);
                ImGui.Combo("##csel", ref comboIdx, names, names.Length);
            }

            if (ImGui.Button("+ Add Combo", new Vector2(-1,0)))
            {
                phase.TankCombos.Add(new EditorTankCombo { Id = $"{phase.Id}-WARGNB" });
                comboIdx = phase.TankCombos.Count - 1;
            }

            if (phase.TankCombos.Count == 0) return;
            comboIdx = Math.Clamp(comboIdx, 0, phase.TankCombos.Count - 1);
            var combo = phase.TankCombos[comboIdx];

            // Remove combo
            if (ImGui.Button("Remove Combo", new Vector2(-1,0)))
            { phase.TankCombos.RemoveAt(comboIdx); comboIdx = Clamp(comboIdx-1, phase.TankCombos.Count); return; }

            ImGui.Spacing(); ImGui.Separator();

            // Job pickers
            var abbrevs = EditorTankCombo.TankAbbrevs;
            ImGui.Text("Tank 1:"); ImGui.SameLine(); ImGui.SetNextItemWidth(65);
            var j1 = Math.Max(0, Array.IndexOf(abbrevs, combo.Job1));
            if (ImGui.Combo("##j1", ref j1, abbrevs, abbrevs.Length))
            { combo.Job1 = abbrevs[j1]; combo.Id = $"{phase.Id}-{combo.Job1}{combo.Job2}"; }
            ImGui.SameLine();
            ImGui.Text("vs"); ImGui.SameLine(); ImGui.SetNextItemWidth(65);
            var j2 = Math.Max(0, Array.IndexOf(abbrevs, combo.Job2));
            if (ImGui.Combo("##j2", ref j2, abbrevs, abbrevs.Length))
            { combo.Job2 = abbrevs[j2]; combo.Id = $"{phase.Id}-{combo.Job1}{combo.Job2}"; }

            ImGui.Spacing();
            SectionLabel("Tank Busters");

            for (int i = 0; i < combo.TankMits.Count; i++)
            {
                var tb  = combo.TankMits[i];
                bool sel = editingTb && i == mechIdx;
                if (ImGui.Selectable($"[{tb.Timestamp}] {tb.Name}##tb{i}", sel))
                { mechIdx = i; editingTb = true; }
                ImGui.SameLine(ImGui.GetContentRegionMax().X - 36);
                ImGui.BeginDisabled(i == 0);
                if (SmallBtn($"↑##tbu{i}")) { Swap(combo.TankMits, i-1, i); if (editingTb) mechIdx = i-1; }
                ImGui.EndDisabled();
                ImGui.SameLine(0,2);
                ImGui.BeginDisabled(i == combo.TankMits.Count - 1);
                if (SmallBtn($"↓##tbd{i}")) { Swap(combo.TankMits, i, i+1); if (editingTb) mechIdx = i+1; }
                ImGui.EndDisabled();
            }

            ImGui.Spacing();
            if (ImGui.Button("+ Add Tank Buster", new Vector2(-1,0)))
            {
                var job1 = EditorTankCombo.AbbrevToJobName(combo.Job1);
                var job2 = EditorTankCombo.AbbrevToJobName(combo.Job2);
                combo.TankMits.Add(new EditorTankMechanic
                {
                    Id    = $"{combo.Id}-TB{combo.TankMits.Count+1:D2}",
                    Cells = new Dictionary<string, EditorMitCell>
                          { { job1, new EditorMitCell() }, { job2, new EditorMitCell() } },
                });
                mechIdx = combo.TankMits.Count - 1; editingTb = true;
            }
            if (editingTb && mechIdx >= 0 && mechIdx < combo.TankMits.Count)
                if (ImGui.Button("Remove Buster##rmtb", new Vector2(-1,0)))
                { combo.TankMits.RemoveAt(mechIdx); mechIdx = Clamp(mechIdx-1, combo.TankMits.Count); }
        }
    }

    private void DrawRight()
    {
        if (phaseIdx < 0 || phaseIdx >= sheet.Phases.Count)
        { ImGui.TextDisabled("← Select a phase."); return; }
        var phase = sheet.Phases[phaseIdx];

        if (!editingTb)
        {
            if (mechIdx < 0 || mechIdx >= phase.Mechanics.Count)
            { ImGui.TextDisabled("← Select a mechanic."); return; }
            var m = phase.Mechanics[mechIdx];
            DrawMechHeader(ref m.Name, ref m.Nickname, ref m.Timestamp, m.Id);
            ImGui.Separator();
            using var scr = ImRaii.Child("##RC", Vector2.Zero, false, ImGuiWindowFlags.HorizontalScrollbar);
            if (!scr.Success) return;
            foreach (var col in PartyColumns())
            {
                if (!m.Cells.ContainsKey(col)) m.Cells[col] = new EditorMitCell();
                DrawCellRow(col, m.Cells[col], false, m.Id);
            }
        }
        else
        {
            if (comboIdx >= phase.TankCombos.Count) { ImGui.TextDisabled("Select a combo."); return; }
            var combo = phase.TankCombos[comboIdx];
            if (mechIdx < 0 || mechIdx >= combo.TankMits.Count)
            { ImGui.TextDisabled("← Select a tank buster."); return; }
            var tb   = combo.TankMits[mechIdx];
            var job1 = EditorTankCombo.AbbrevToJobName(combo.Job1);
            var job2 = EditorTankCombo.AbbrevToJobName(combo.Job2);
            DrawMechHeader(ref tb.Name, ref tb.Nickname, ref tb.Timestamp, tb.Id);
            ImGui.Separator();
            using var scr = ImRaii.Child("##RCT", Vector2.Zero, false);
            if (!scr.Success) return;
            foreach (var job in new[] { job1, job2 })
            {
                if (!tb.Cells.ContainsKey(job)) tb.Cells[job] = new EditorMitCell();
                DrawCellRow(job, tb.Cells[job], true, tb.Id);
            }
        }
    }

    private static void DrawMechHeader(
        ref string name, ref string nickname, ref string timestamp, string id)
    {
        SectionLabel("Mechanic");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200); ImGui.InputText("##mn",    ref name,      80);
        ImGui.SameLine();
        ImGui.TextUnformatted("Nickname:"); ImGui.SameLine();
        ImGui.SetNextItemWidth(110); ImGui.InputText("##mnick", ref nickname,  32);
        ImGui.SameLine();
        ImGui.TextUnformatted("Time:"); ImGui.SameLine();
        ImGui.SetNextItemWidth(65);  ImGui.InputText("##mts",   ref timestamp, 10);
        ImGui.TextDisabled($"  id: {id}");
    }

    private void DrawCellRow(string col, EditorMitCell cell, bool isTankRow, string mechId)
    {
        ImGui.PushID($"{col}_{mechId}");
        var hColor = isTankRow
            ? new Vector4(1f, 0.55f, 0.15f, 1f)
            : new Vector4(0.45f, 0.85f, 1f, 1f);

        ImGui.TextColored(hColor, $"{col,-14}");
        ImGui.SameLine(0, 6);

        // Render each ability chip inline
        int removeAt = -1;
        for (int ai = 0; ai < cell.Abilities.Count; ai++)
        {
            var entry = cell.Abilities[ai];
            ImGui.PushID(ai);

            if (entry.ActionId == 0)
            {
                // Party-mit placeholder chip
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.1f, 0.45f, 0.1f, 1f));
                if (ImGui.Button("Party\nMit", new Vector2(36, IconSz.Y))) removeAt = ai;
                ImGui.PopStyleColor();
            }
            else
            {
                uint absId  = (uint)entry.AbsId;
                bool hasInf = AbilityExtraInfoData.AbilitiesInfo.TryGetValue(absId, out var info);

                if (hasInf && info!.IconId != 0)
                {
                    var wrap = tex.GetFromGameIcon(new GameIconLookup(info.IconId)).GetWrapOrEmpty();
                    // Buddy 
                    var tint = entry.IsBuddy ? new Vector4(1f, 0.6f, 1f, 1f) : Vector4.One;
                    ImGui.Image(wrap.Handle, IconSz, Vector2.Zero, Vector2.One, tint);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text(hasInf ? info!.Nickname : $"ID {absId}");
                        if (entry.IsBuddy) ImGui.TextColored(new Vector4(1f,0.6f,1f,1f), "(Buddy Mit)");
                        ImGui.Text("Right-click for options");
                        ImGui.EndTooltip();
                    }
                }
                else
                {
                    var chip = hasInf ? info!.Nickname : $"#{absId}";
                    var col2 = entry.IsBuddy ? new Vector4(0.6f,0.2f,0.8f,1f) : new Vector4(0.25f,0.25f,0.3f,1f);
                    ImGui.PushStyleColor(ImGuiCol.Button, col2);
                    ImGui.Button(chip, new Vector2(0, IconSz.Y));
                    ImGui.PopStyleColor();
                }

                // Right-click menu
                if (ImGui.BeginPopupContextItem($"##ctx{ai}"))
                {
                    ImGui.TextDisabled(hasInf ? info!.Nickname : $"ID {absId}");
                    ImGui.Separator();
                    var toggleLabel = entry.IsBuddy ? "Make Own Mit" : "Make Buddy Mit";
                    if (ImGui.MenuItem(toggleLabel)) entry.IsBuddy = !entry.IsBuddy;
                    ImGui.Separator();
                    if (ImGui.MenuItem("Remove")) removeAt = ai;
                    ImGui.EndPopup();
                }
            }

            ImGui.SameLine(0, 3);
            ImGui.PopID();
        }

        // Remove queued
        if (removeAt >= 0) cell.Abilities.RemoveAt(removeAt);

        // Add button
        if (ImGui.Button($"+##add", new Vector2(IconSz.Y, IconSz.Y)))
        { abilitySearch = ""; pickerCol = col; openPicker = true; }
        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.Text("Add ability"); ImGui.EndTooltip(); }

        // Party-mit toggle
        if (!isTankRow && col is "Tank 1" or "Tank 2" or "Phys Range")
        {
            ImGui.SameLine(0, 4);
            bool hasPm = cell.Abilities.Any(a => a.ActionId == 0);
            ImGui.PushStyleColor(ImGuiCol.Button,
                hasPm ? new Vector4(0.1f,0.5f,0.1f,1f) : new Vector4(0.28f,0.28f,0.28f,1f));
            if (ImGui.Button("PM##pm", new Vector2(0, IconSz.Y)))
            {
                if (hasPm) cell.Abilities.RemoveAll(a => a.ActionId == 0);
                else        cell.Abilities.Insert(0, new EditorAbilityEntry { ActionId = 0 });
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.Text("Toggle Party Mit placeholder"); ImGui.EndTooltip(); }
        }

        // Timing + Note pushed to right side
        float rStart = ImGui.GetContentRegionMax().X - 370;
        if (rStart > ImGui.GetCursorPosX() + 10) ImGui.SameLine(rStart);
        else ImGui.SameLine(0, 12);

        ImGui.TextDisabled("Timing:"); ImGui.SameLine(0,4);
        ImGui.SetNextItemWidth(90); ImGui.InputText($"##t", ref cell.TimingText, 40);
        ImGui.SameLine(0, 8);
        ImGui.TextDisabled("Note:"); ImGui.SameLine(0,4);
        ImGui.SetNextItemWidth(240); ImGui.InputText($"##n", ref cell.Note, 200);

        ImGui.Separator();
        ImGui.PopID();
    }
    

    private void DrawAbilityPicker()
    {
        if (openPicker) { ImGui.OpenPopup("##picker"); openPicker = false; }
        ImGui.SetNextWindowSize(new Vector2(370, 460), ImGuiCond.Always);
        if (!ImGui.BeginPopup("##picker")) return;

        ImGui.Text($"Add ability  →  {pickerCol}");
        ImGui.Separator();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##ps", ref abilitySearch, 64);

        // Show All toggle — off by default so list is filtered to the column's relevant abilities
        ImGui.Checkbox("Show all abilities", ref showAllAbilities);
        ImGui.SameLine();
        ImGui.TextDisabled("Click = own   Right-click = buddy");
        ImGui.Spacing();

        var allowed = showAllAbilities ? null : GetAllowedIds(pickerCol);

        using (var lst = ImRaii.Child("##plist", new Vector2(0, -46), false))
        {
            if (lst.Success)
            {
                var term = abilitySearch.ToLowerInvariant();
                foreach (var (id, info) in AbilityExtraInfoData.AbilitiesInfo
                    .OrderBy(kv => kv.Value.Nickname, StringComparer.OrdinalIgnoreCase))
                {
                    // Column filter
                    if (allowed != null && !allowed.Contains(id)) continue;

                    // Search filter
                    if (!string.IsNullOrEmpty(term) &&
                        !info.Nickname.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                        !info.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (info.IconId != 0)
                    {
                        var wrap = tex.GetFromGameIcon(new GameIconLookup(info.IconId)).GetWrapOrEmpty();
                        ImGui.Image(wrap.Handle, new Vector2(18, 18));
                        ImGui.SameLine(0, 4);
                    }

                    bool clicked = ImGui.Selectable($"{info.Nickname}   {info.Name}##{id}");
                    if (ImGui.BeginPopupContextItem($"##pctx{id}"))
                    {
                        if (ImGui.MenuItem("Add as Buddy Mit"))
                        { AddToCell((int)id, true); ImGui.CloseCurrentPopup(); ImGui.CloseCurrentPopup(); }
                        ImGui.EndPopup();
                    }
                    if (clicked) { AddToCell((int)id, false); ImGui.CloseCurrentPopup(); }
                }
            }
        }

        ImGui.Separator();
        if (ImGui.Button("Party Mit"))    { AddToCell(0, false);                         ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("None"))         { AddToCell(-1, false);                        ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Tank LB"))      { AddToCell(unchecked((int)(uint)-2), false);  ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Kitchen Sink")) { AddToCell(unchecked((int)(uint)-3), false);  ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Everything"))   { AddToCell(unchecked((int)(uint)-99), false); ImGui.CloseCurrentPopup(); }
        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.Text("Use every available personal mit"); ImGui.EndTooltip(); }

        ImGui.EndPopup();
    }

    /// Returns the set of ability IDs relevant for a given column/job.
    /// Returns null if the column is unrecognised (show everything).
    private static HashSet<uint>? GetAllowedIds(string? col) => col switch
    {
        // ── Shared tank mits (always relevant for any tank column) ────────────
        "Tank 1" or "Tank 2" => new HashSet<uint>
        {
            Abilities.Reprisal, Abilities.Rampart,
            // WAR
            Abilities.Holmgang, Abilities.ThrillOfBattle, Abilities.Damnation,
            Abilities.Bloodwhetting, Abilities.NascentFlash, Abilities.Vengeance, Abilities.RawIntuition,
            Abilities.ShakeItOff,
            // PLD
            Abilities.HallowedGround, Abilities.Guardian, Abilities.Bulwark,
            Abilities.HolySheltron, Abilities.Sheltron, Abilities.Intervention,
            Abilities.Sentinel, Abilities.DivineVeil, Abilities.PassageOfArms,
            // DRK
            Abilities.LivingDead, Abilities.ShadowWall, Abilities.TheBlackestNight,
            Abilities.Oblation, Abilities.DarkMind, Abilities.ShadowVigil, Abilities.DarkMissionary,
            // GNB
            Abilities.Superbolide, Abilities.Aurora, Abilities.GreatNebula, Abilities.Nebula,
            Abilities.Camouflage, Abilities.HeartOfCorundum, Abilities.HeartOfStone, Abilities.HeartOfLight,
        },

        // ── Individual tank job columns (tank busters) ────────────────────────
        "Warrior" => new HashSet<uint>
        {
            Abilities.Reprisal, Abilities.Rampart,
            Abilities.Holmgang, Abilities.ThrillOfBattle, Abilities.Damnation,
            Abilities.Bloodwhetting, Abilities.NascentFlash, Abilities.Vengeance,
            Abilities.RawIntuition, Abilities.ShakeItOff,
        },
        "Paladin" => new HashSet<uint>
        {
            Abilities.Reprisal, Abilities.Rampart,
            Abilities.HallowedGround, Abilities.Guardian, Abilities.Bulwark,
            Abilities.HolySheltron, Abilities.Sheltron, Abilities.Intervention,
            Abilities.Sentinel, Abilities.DivineVeil, Abilities.PassageOfArms,
        },
        "DarkKnight" => new HashSet<uint>
        {
            Abilities.Reprisal, Abilities.Rampart,
            Abilities.LivingDead, Abilities.ShadowWall, Abilities.TheBlackestNight,
            Abilities.Oblation, Abilities.DarkMind, Abilities.ShadowVigil, Abilities.DarkMissionary,
        },
        "Gunbreaker" => new HashSet<uint>
        {
            Abilities.Reprisal, Abilities.Rampart,
            Abilities.Superbolide, Abilities.Aurora, Abilities.GreatNebula, Abilities.Nebula,
            Abilities.Camouflage, Abilities.HeartOfCorundum, Abilities.HeartOfStone, Abilities.HeartOfLight,
        },

        // ── Healers ───────────────────────────────────────────────────────────
        "Scholar" => new HashSet<uint>
        {
            Abilities.FeyIllumination, Abilities.Deploy, Abilities.Concitation,
            Abilities.Succor, Abilities.Soil, Abilities.Expedience,
            Abilities.Seraphism, Abilities.SummonSeraph, Abilities.Recitation,
        },
        "Sage" => new HashSet<uint>
        {
            Abilities.Zoe, Abilities.Kerachole, Abilities.Holos, Abilities.Panhaima,
            Abilities.Philosophia, Abilities.EukrasianPrognosis2, Abilities.EukrasianPrognosis1,
        },
        "White Mage" => new HashSet<uint>
        {
            Abilities.PlenaryIndulgence, Abilities.Temperance, Abilities.DivineCaress,
            Abilities.LetargyOfTheBell, Abilities.Asylum,
        },
        "Astro" => new HashSet<uint>
        {
            Abilities.CollectiveUnconscious, Abilities.NeutralSect,
            Abilities.SunSign, Abilities.Macrocosmos,
        },

        // ── Melee ─────────────────────────────────────────────────────────────
        "Melee 1" or "Melee 2" => new HashSet<uint>
        {
            Abilities.Feint, Abilities.Mantra,
        },

        // ── Physical Ranged ───────────────────────────────────────────────────
        "Phys Range" => new HashSet<uint>
        {
            Abilities.Tactician, Abilities.ShieldSamba, Abilities.Troubadour,
            Abilities.Dismantle, Abilities.NaturesMinne, Abilities.Improvisation,
        },

        // ── Caster ────────────────────────────────────────────────────────────
        "Caster" => new HashSet<uint>
        {
            Abilities.Addle, Abilities.MagickBarrier, Abilities.TemperaGrassa,
        },

        // ── Extras — anything that appears outside its own role column ────────
        "Extras" => new HashSet<uint>
        {
            Abilities.PassageOfArms, Abilities.Mantra,
            Abilities.Dismantle, Abilities.NaturesMinne, Abilities.Improvisation,
            Abilities.MagickBarrier, Abilities.TemperaGrassa,
        },

        _ => null, // unknown column — show everything
    };

    private void AddToCell(int actionId, bool buddy)
    {
        if (pickerCol == null || phaseIdx < 0 || phaseIdx >= sheet.Phases.Count) return;
        var phase = sheet.Phases[phaseIdx];
        EditorMitCell? cell = null;

        if (!editingTb)
        {
            if (mechIdx >= 0 && mechIdx < phase.Mechanics.Count)
                phase.Mechanics[mechIdx].Cells.TryGetValue(pickerCol, out cell);
        }
        else
        {
            if (comboIdx < phase.TankCombos.Count && mechIdx >= 0)
            {
                var combo = phase.TankCombos[comboIdx];
                if (mechIdx < combo.TankMits.Count)
                    combo.TankMits[mechIdx].Cells.TryGetValue(pickerCol, out cell);
            }
        }

        cell?.Abilities.Add(new EditorAbilityEntry { ActionId = Math.Abs(actionId), IsBuddy = buddy });
    }
    

    private static IEnumerable<string> PartyColumns()
    {
        foreach (var c in JobRoleMapper.AllColumns) yield return c;
        yield return "Extras";
    }

    private static void Swap<T>(List<T> list, int a, int b)
    { (list[a], list[b]) = (list[b], list[a]); }

    private static int Clamp(int val, int count)
        => count == 0 ? -1 : Math.Max(0, Math.Min(val, count - 1));

    private static void SectionLabel(string text)
        => ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.3f, 1f), text);

    private static bool SmallBtn(string label)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 1));
        bool r = ImGui.SmallButton(label);
        ImGui.PopStyleVar();
        return r;
    }

    private void ShowStatus(string msg) { saveStatus = msg; _saveStatusTimer = 3.5f; }

    /// Draws a 1px vertical line the full height of the current window content region.
    private static void VerticalSeparator()
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos      = ImGui.GetCursorScreenPos();
        float height = ImGui.GetContentRegionAvail().Y;
        uint  color  = ImGui.GetColorU32(ImGuiCol.Separator);
        drawList.AddLine(pos, pos + new Vector2(0, height), color, 1f);
        // Advance cursor by 1px so SameLine offsets work correctly
        ImGui.Dummy(new Vector2(1, 0));
    }
}
