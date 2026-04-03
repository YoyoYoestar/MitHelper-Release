using System.Collections.Generic;

namespace MitHelper.Data;

/// Maps a player's ClassJobId to their default sheet column and job name
/// (used for tank_combos key matching).
public static class JobRoleMapper
{
    // BaseRole is the column key used in the JSON mits dictionaries.
    // For tanks and melees the actual column is adjusted by TankDefault/MeleeDefault config.
    private static readonly Dictionary<uint, (string BaseRole, string JobName)> Map = new()
    {
        // Tanks
        { 19, ("Tank", "Paladin")     },
        { 21, ("Tank", "Warrior")     },
        { 32, ("Tank", "DarkKnight")  },
        { 37, ("Tank", "Gunbreaker")  },

        // Healers
        { 28, ("Scholar",    "Scholar")      },
        { 40, ("Sage",       "Sage")         },
        { 24, ("White Mage", "White Mage")   },
        { 33, ("Astro",      "Astrologian")  },

        // Melee
        { 20, ("Melee", "Monk")       },
        { 22, ("Melee", "Dragoon")    },
        { 30, ("Melee", "Ninja")      },
        { 34, ("Melee", "Samurai")    },
        { 39, ("Melee", "Reaper")     },
        { 41, ("Melee", "Viper")      },

        // Physical Ranged
        { 23, ("Phys Range", "Bard")        },
        { 31, ("Phys Range", "Machinist")   },
        { 38, ("Phys Range", "Dancer")      },

        // Caster
        { 25, ("Caster", "Black Mage")   },
        { 27, ("Caster", "Summoner")     },
        { 35, ("Caster", "Red Mage")     },
        { 42, ("Caster", "Pictomancer")  },
    };
    
    public static readonly string[] AllColumns =
    {
        "Tank 1", "Tank 2", "Scholar", "Sage", "White Mage",
        "Astro", "Melee 1", "Melee 2", "Phys Range", "Caster"
    };

    public static Dictionary<uint, uint> PartyMitsTanks = new() //Job ID,Mit ID
    {
        { 21, Abilities.ShakeItOff },
        { 19,Abilities.DivineVeil },
        { 32, Abilities.DarkMissionary },
        { 37, Abilities.HeartOfLight}
    };
    public static Dictionary<uint, uint> PartyMitsPhysicalRanged = new() //Job ID,Mit ID
    {
        { 23, Abilities.Troubadour },
        { 31, Abilities.Tactician },
        { 38, Abilities.ShieldSamba }
    };
    public static Dictionary<uint, uint> ExtrasMap = new() //Job ID, Extras ID
    {
        {19, Abilities.PassageOfArms},
        {23, Abilities.NaturesMinne},
        {31, Abilities.Dismantle},
        {38, Abilities.Improvisation},
        {35, Abilities.MagickBarrier},
        {42, Abilities.TemperaGrassa},
        {20, Abilities.Mantra},
    };
    
    public static (string Column, string JobName) GetColumn(
        uint classJobId,
        bool tankDefault,
        bool meleeDefault)
    {
        if (!Map.TryGetValue(classJobId, out var entry))
            return ("Tank 1", "Unknown");

        var column = entry.BaseRole switch
        {
            "Tank"  => tankDefault  == false ? "Tank 1"  : "Tank 2",
            "Melee" => meleeDefault == false ? "Melee 1" : "Melee 2",
            _       => entry.BaseRole
        };

        return (column, entry.JobName);
    }
    
    /// Returns true if this job has abilities that may appear in the Extras column
    public static bool HasExtras(uint classJobId) => classJobId switch
    {
        19 => true, //PLD - Passage of Arms
        23 => true, //BRD - Minne
        31 => true, //MCH - Dismantle
        38 => true, //DNC - Improv
        35 => true, //RDM - Magick Barrier
        42  => true, //PCT - Tempera Grassa
        20 => true, //Monk - Mantra
        _  => false
    };

    ///Returns true if this is a tank job.
    public static bool IsTank(uint classJobId) => classJobId is 19 or 21 or 32 or 37;
}
