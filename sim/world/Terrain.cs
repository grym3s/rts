using System.Text.Json;

namespace Rts.Sim.World;

/// <summary>T25: what a map cell is made of. Map rows use `.` land, `~` water, `#` cliff, `=` bridge
/// (content/CONTEXT.md, maps/). A cell with no terrain set is Land, so maps that only carry
/// `blocked` rects behave exactly as before.</summary>
public enum Terrain : byte { Land = 0, Water = 1, Cliff = 2, Bridge = 3 }

/// <summary>T25: how a unit moves. Land on land and bridges; Naval on water and under bridges;
/// Hover (amphibious) on both; Air anywhere, over buildings too.</summary>
public enum MoveDomain : byte { Land = 0, Naval = 1, Hover = 2, Air = 3 }

/// <summary>T25: which domains a weapon can engage at all (`weapon.targets` in unit files).
/// Separate from the damage multiplier. Ground = land units, hover units and buildings.</summary>
[Flags]
public enum TargetMask : byte { None = 0, Ground = 1, Air = 2, Sea = 4, All = Ground | Air | Sea }

public static class Domains
{
    /// <summary>Can a unit of this domain stand on this terrain (ignores buildings and blocks)?</summary>
    public static bool Allows(MoveDomain d, Terrain t) => d switch
    {
        MoveDomain.Land => t is Terrain.Land or Terrain.Bridge,
        MoveDomain.Naval => t is Terrain.Water or Terrain.Bridge,
        MoveDomain.Hover => t is Terrain.Land or Terrain.Water or Terrain.Bridge,
        _ => true, // air
    };

    /// <summary>What a weapon needs in its mask to engage a unit of this domain. Hover counts
    /// as both ground and sea (it can be over either).</summary>
    public static bool CanEngage(TargetMask weapon, MoveDomain target) => target switch
    {
        MoveDomain.Air => (weapon & TargetMask.Air) != 0,
        MoveDomain.Naval => (weapon & TargetMask.Sea) != 0,
        MoveDomain.Hover => (weapon & (TargetMask.Ground | TargetMask.Sea)) != 0,
        _ => (weapon & TargetMask.Ground) != 0,
    };

    public static MoveDomain Parse(string s) => s switch
    {
        "land" => MoveDomain.Land,
        "naval" => MoveDomain.Naval,
        "hover" => MoveDomain.Hover,
        "air" => MoveDomain.Air,
        _ => throw new InvalidDataException($"unknown domain '{s}'"),
    };

    public static string Name(MoveDomain d) => d switch
    {
        MoveDomain.Naval => "naval",
        MoveDomain.Hover => "hover",
        MoveDomain.Air => "air",
        _ => "land",
    };

    /// <summary>Domain from armor when a unit file has no explicit `domain`: air armor flies,
    /// naval armors sail, everything else walks.</summary>
    public static MoveDomain FromArmor(string armor) => armor switch
    {
        "air" => MoveDomain.Air,
        "light-naval" or "heavy-naval" or "submerged" => MoveDomain.Naval,
        _ => MoveDomain.Land,
    };

    public static TargetMask ParseTargets(JsonElement arr)
    {
        var m = TargetMask.None;
        foreach (var e in arr.EnumerateArray())
            m |= e.GetString() switch
            {
                "ground" => TargetMask.Ground,
                "air" => TargetMask.Air,
                "sea" => TargetMask.Sea,
                var s => throw new InvalidDataException($"unknown weapon target '{s}'"),
            };
        return m;
    }

    public static Terrain TileOf(char c) => c switch
    {
        '.' => Terrain.Land,
        '~' => Terrain.Water,
        '#' => Terrain.Cliff,
        '=' => Terrain.Bridge,
        _ => throw new InvalidDataException($"unknown terrain tile '{c}'"),
    };
}
