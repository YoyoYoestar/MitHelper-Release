using System;
using System.Collections.Generic;
using Dalamud;
using Dalamud.Plugin.Services;
using Action = Lumina.Excel.Sheets.Action;


namespace MitHelper.Data;

public record AbilityExtraInfo(
    uint ActionId,
    uint IconId,
    string Name,
    string Nickname
);

public static class AbilityExtraInfoData
{
    private static IDataManager? _dataManager;
    private static Dictionary<uint, AbilityExtraInfo>? _abilitiesInfo;

    public static Dictionary<uint, AbilityExtraInfo> AbilitiesInfo 
        => _abilitiesInfo ?? throw new InvalidOperationException("Call Initialize() first.");

    public static void Initialize(IDataManager dataManager)
    {
        _dataManager = dataManager;
        _abilitiesInfo = BuildAll();
    }

    private static AbilityExtraInfo Build(uint id, string name, string nickname)
    {
        var row = _dataManager!.GetExcelSheet<Action>()?.GetRow(id);
        return new AbilityExtraInfo(
            id,
            row?.Icon ?? 0,
            row?.Name.ToString() ?? "N/A",
            nickname
        );
    }
    
    private static AbilityExtraInfo BuildManual(uint id, uint iconId, string name, string nickname)
    {
        return new AbilityExtraInfo(id, iconId, name, nickname);
    }
    
    private static Dictionary<uint, AbilityExtraInfo> BuildAll() => new()
    {

        // Tank 
        { Abilities.Reprisal, Build(Abilities.Reprisal, "Reprisal", "Rep") },
        { Abilities.TankLb, BuildManual(Abilities.TankLb, 103, "Tank LB", "Tank LB!") },
        
        { Abilities.ShakeItOff, Build(Abilities.ShakeItOff, "Shake ti Off", "Shake") },
        { Abilities.DivineVeil, Build(Abilities.DivineVeil, "Divine Veil", "Veil") },
        { Abilities.DarkMissionary, Build(Abilities.DarkMissionary, "Dark Missionary", "Missionary") },
        { Abilities.HeartOfLight, Build(Abilities.HeartOfLight, "Heart of Light","HoL") },
        { Abilities.PassageOfArms, Build(Abilities.PassageOfArms, "Passage of Arms", "Passage") },

        // Scholar
        { Abilities.FeyIllumination, Build(Abilities.FeyIllumination, "Fey Illumination", "Fey Illum") },
        { Abilities.Deploy, Build(Abilities.Deploy, "Spread-Lo", "Spread-Lo") },
        { Abilities.Concitation, Build(Abilities.Concitation, "Concitation","Concit") },
        { Abilities.Succor, Build(Abilities.Succor, "Succor", "Succor") },
        { Abilities.Soil, Build(Abilities.Soil, "Sacred Soil", "Soil") },
        { Abilities.Expedience, Build(Abilities.Expedience, "Expedient", "Exped") },
        { Abilities.Seraphism, Build(Abilities.Seraphism, "Seraphism", "Seraphism") },
        { Abilities.SummonSeraph, Build(Abilities.SummonSeraph, "Summon Seraph", "Seraph") },
        { Abilities.Recitation, Build(Abilities.Recitation, "Recitation", "Recit") },
        
        // Sage
        { Abilities.Zoe, Build(Abilities.Zoe, "Zoe", "Zoe") },
        { Abilities.Kerachole, Build(Abilities.Kerachole, "Kerachole", "Kera") },
        { Abilities.Holos, Build(Abilities.Holos, "Holos", "Holos") },
        { Abilities.Panhaima, Build(Abilities.Panhaima, "Panhaima", "Panhaima") },
        { Abilities.Philosophia, Build(Abilities.Philosophia, "Philosophia", "Party-ia") },
        { Abilities.EukrasianPrognosis2, Build(Abilities.EukrasianPrognosis2, "Eukrasian Prognosis", "EukProg2") },
        { Abilities.EukrasianPrognosis1, Build(Abilities.EukrasianPrognosis1, "Eukrasian Prognosis", "EukProg1") },
        
        // White Mage
        { Abilities.PlenaryIndulgence, Build(Abilities.PlenaryIndulgence, "Plenary Indulgence", "Plenary") },
        { Abilities.Temperance, Build(Abilities.Temperance, "Temperance", "Temp") },
        { Abilities.DivineCaress, Build(Abilities.DivineCaress, "Divine Caress", "Caress") },
        { Abilities.LetargyOfTheBell, Build(Abilities.LetargyOfTheBell, "Lethargy Of The Bell", "Bell") },
        { Abilities.Asylum, Build(Abilities.Asylum, "Asylum", "Asylum") },
        
        // Astro
        { Abilities.CollectiveUnconscious, Build(Abilities.CollectiveUnconscious, "Collective Unconscious", "CU") },
        { Abilities.NeutralSect, Build(Abilities.NeutralSect, "Neutral Sect", "Neutral") },
        { Abilities.SunSign, Build(Abilities.SunSign, "Sun Sign", "Sun") },
        { Abilities.Macrocosmos, Build(Abilities.Macrocosmos, "Macro Cosmos", "Macro") },
        
        // Melee
        { Abilities.Feint, Build(Abilities.Feint, "Feint", "Feint") },
        { Abilities.Mantra, Build(Abilities.Mantra, "Mantra", "Mantra") },
        
        // Physical Ranged
        { Abilities.Tactician, Build(Abilities.Tactician, "Tactician", "Tact") },
        { Abilities.ShieldSamba, Build(Abilities.ShieldSamba, "Tactician", "Samba") },
        { Abilities.Troubadour, Build(Abilities.Troubadour, "Troubadour", "Troub") },
        { Abilities.Dismantle, Build(Abilities.Dismantle, "Dismantle", "Dismantle") },
        { Abilities.NaturesMinne, Build(Abilities.NaturesMinne, "Nature's Minne", "Minne") },
        { Abilities.Improvisation, Build(Abilities.Improvisation, "Improvisation", "Improv") },
        
        // Caster
        { Abilities.Addle, Build(Abilities.Addle, "Addle", "Addle") },
        { Abilities.MagickBarrier, Build(Abilities.MagickBarrier, "Magick Barrier", "Barrier") },
        { Abilities.TemperaGrassa, Build(Abilities.TemperaGrassa,  "Tempera Grassa",  "Tempera Grassa") },
        
        
        // Tank Specific
        { Abilities.Rampart, Build(Abilities.Rampart, "Rampart", "Ramp") },

        // WAR
        { Abilities.Holmgang, Build(Abilities.Holmgang, "Holmgang", "Holmg") },
        { Abilities.ThrillOfBattle, Build(Abilities.ThrillOfBattle, "Thrill of Battle", "Thrill") },
        { Abilities.Damnation, Build(Abilities.Damnation, "Damnation", "Damnation") },
        { Abilities.Bloodwhetting, Build(Abilities.Bloodwhetting, "Bloodwhetting", "Bloodwhetting") },
        { Abilities.NascentFlash, Build(Abilities.NascentFlash, "Nascent", "Nascent") },
        { Abilities.Vengeance, Build(Abilities.Vengeance, "Vengeance", "Veng") },
        { Abilities.RawIntuition, Build(Abilities.RawIntuition, "Raw Intuition", "Raw") },

        // PLD
        { Abilities.HallowedGround, Build(Abilities.HallowedGround, "Hallowed Ground", "Hallowed") },
        { Abilities.Guardian, Build(Abilities.Guardian, "Guardian", "Guardian") },
        { Abilities.Bulwark, Build(Abilities.Bulwark, "Bulwark", "Bulwark") },
        { Abilities.HolySheltron, Build(Abilities.HolySheltron, "Holy Sheltron", "Sheltron") },
        { Abilities.Intervention, Build(Abilities.Intervention, "Intervention", "Intervention") },
        { Abilities.Sentinel, Build(Abilities.Sentinel, "Sentinel", "Sentinel") },
        { Abilities.Sheltron, Build(Abilities.Sheltron, "Sheltron", "Sheltron") },

        // DRK
        { Abilities.LivingDead, Build(Abilities.LivingDead, "LD", "LD") },
        { Abilities.ShadowWall, Build(Abilities.ShadowWall, "Shadow Wall", "Shadow Wall") },
        { Abilities.TheBlackestNight, Build(Abilities.TheBlackestNight, "The Blackest Night", "TBN") },
        { Abilities.Oblation, Build(Abilities.Oblation, "Oblation", "Oblation") },
        { Abilities.DarkMind, Build(Abilities.DarkMind, "Dark Mind", "Dark Mind") },
        { Abilities.ShadowVigil, Build(Abilities.ShadowVigil, "Shadow Vigil", "Vigil") },

        // GNB
        { Abilities.Superbolide, Build(Abilities.Superbolide, "Superbolide","Bolide") },
        { Abilities.Aurora, Build(Abilities.Aurora, "Aurora", "Aurora") },
        { Abilities.GreatNebula, Build(Abilities.GreatNebula, "Nebula", "Nebula") },
        { Abilities.Camouflage, Build(Abilities.Camouflage, "Camouflage", "Camo") },
        { Abilities.HeartOfCorundum, Build(Abilities.HeartOfCorundum, "Heart Of Corundum", "HoC") },
        { Abilities.HeartOfStone, Build(Abilities.HeartOfStone, "Heart Of Stone", "HoS") }
    };
    
}

