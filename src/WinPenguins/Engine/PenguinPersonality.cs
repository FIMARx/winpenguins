namespace WinPenguins.Engine;

public enum PenguinTemperament : byte
{
    Cuddly = 0,
    Grumpy = 1,
    Shy = 2,
    ThrillSeeker = 3,
    Chill = 4,
    Curious = 5,
    Dramatic = 6,
    Mischievous = 7
}

public enum PenguinQuirk : byte
{
    Foodie = 0,
    Climber = 1,
    Slider = 2,
    Social = 3,
    Loner = 4,
    Groovy = 5,
    Acrobat = 6,
    Napper = 7,
    Explorer = 8,
    Daydreamer = 9
}

public sealed record PenguinPersonality(
    int CharacterId,
    string Name,
    PenguinTemperament Temperament,
    PenguinQuirk Quirk,
    double SpeedBias)
{
    public float BaselineMood => Temperament switch
    {
        PenguinTemperament.Cuddly => 20f,
        PenguinTemperament.Grumpy => -15f,
        PenguinTemperament.Shy => -5f,
        PenguinTemperament.ThrillSeeker => 12f,
        PenguinTemperament.Chill => 10f,
        PenguinTemperament.Curious => 8f,
        PenguinTemperament.Dramatic => 5f,
        PenguinTemperament.Mischievous => 10f,
        _ => 0f
    };

    public PenguinPersonality WithTemperament(PenguinTemperament temperament) =>
        this with
        {
            Temperament = temperament,
            SpeedBias = PenguinCharacterCatalog.ComputeSpeedBias(CharacterId, temperament, Quirk)
        };

    public string FormatMenuHeader(float mood) =>
        $"{Name} · {GetTemperamentLabel(Temperament)} & {GetQuirkLabel(Quirk)} · {DescribeMood(mood)}";

    public static string GetTemperamentLabel(PenguinTemperament temperament) => temperament switch
    {
        PenguinTemperament.Cuddly => "Cuddly",
        PenguinTemperament.Grumpy => "Grumpy",
        PenguinTemperament.Shy => "Shy",
        PenguinTemperament.ThrillSeeker => "Thrill-Seeker",
        PenguinTemperament.Chill => "Chill",
        PenguinTemperament.Curious => "Curious",
        PenguinTemperament.Dramatic => "Dramatic",
        PenguinTemperament.Mischievous => "Mischievous",
        _ => "Friendly"
    };

    public static string GetQuirkLabel(PenguinQuirk quirk) => quirk switch
    {
        PenguinQuirk.Foodie => "Foodie",
        PenguinQuirk.Climber => "Climber",
        PenguinQuirk.Slider => "Slider",
        PenguinQuirk.Social => "Social",
        PenguinQuirk.Loner => "Loner",
        PenguinQuirk.Groovy => "Groovy",
        PenguinQuirk.Acrobat => "Acrobat",
        PenguinQuirk.Napper => "Napper",
        PenguinQuirk.Explorer => "Explorer",
        PenguinQuirk.Daydreamer => "Daydreamer",
        _ => "Explorer"
    };

    public static string DescribeMood(float mood) => mood switch
    {
        >= 45f => "Delighted",
        >= 15f => "Happy",
        > -15f => "Calm",
        > -45f => "Annoyed",
        _ => "Grumpy!"
    };
}

public static class PenguinCharacterCatalog
{
    public const int RosterSize = 100;

    private static readonly string[] s_names =
    [
        "Pingu", "Mochi", "Waddles", "Biscuit", "Noodle", "Captain Frost", "Pebbles", "Pip", "Chilly", "Dumpling",
        "Sprout", "Ziggy", "Snowball", "Barnaby", "Marshmallow", "Pickles", "Tux", "Waffle", "Flipper", "Olive",
        "Miso", "Nimbus", "Clover", "Yoshi", "Maple", "Gizmo", "Puffin", "Kiwi", "Comet", "Bruno",
        "Pepper", "Lumi", "Bramble", "Crumb", "Fizz", "Otto", "Echo", "Sundae", "Cosmo", "Suki",
        "Frosty", "Puddles", "Beans", "Luna", "Tofu", "Jasper", "Popcorn", "Flurry", "Fig", "Boomer",
        "Nori", "Cinder", "Snickers", "Pippin", "Blizzard", "Cocoa", "Rocco", "Sprinkles", "Tater", "Mitten",
        "Skittles", "Glacier", "Peanut", "Mango", "Koda", "Socks", "Twix", "Icicle", "Bongo", "Hazel",
        "Chips", "Yeti", "Berry", "Rolo", "Juno", "Pretzel", "Drift", "Sunny", "Boba", "Bandit",
        "Gnocchi", "Scout", "Sparky", "Chai", "Pogo", "Fudge", "Zephyr", "Nugget", "Dash", "Velvet",
        "Smudge", "Zigzag", "Crumpet", "Tinsel", "Munchkin", "Storm", "Pipsqueak", "Kipper", "Tundra", "Pluto"
    ];

    public static IReadOnlyList<string> Names => s_names;

    public static PenguinPersonality Default { get; } = CreateForSlot(0);

    public static PenguinPersonality CreateForSlot(int characterId)
    {
        int slot = ((characterId % RosterSize) + RosterSize) % RosterSize;
        int temperamentIndex = slot % 8;
        int quirkIndex = ((temperamentIndex * 3) + (slot / 8)) % 10;
        var temperament = (PenguinTemperament)temperamentIndex;
        var quirk = (PenguinQuirk)quirkIndex;
        double speedBias = ComputeSpeedBias(slot, temperament, quirk);
        return new PenguinPersonality(slot, s_names[slot], temperament, quirk, speedBias);
    }

    public static PenguinPersonality AllocateUnique(IReadOnlyList<Penguin> existing, Random rng, int? excludeId = null)
    {
        Span<bool> used = stackalloc bool[RosterSize];
        for (int i = 0; i < existing.Count; i++)
        {
            Penguin p = existing[i];
            if (!p.Active)
            {
                continue;
            }

            int id = p.Personality.CharacterId;
            if ((uint)id < RosterSize)
            {
                used[id] = true;
            }
        }

        if (excludeId.HasValue && (uint)excludeId.Value < RosterSize)
        {
            used[excludeId.Value] = true;
        }

        Span<int> available = stackalloc int[RosterSize];
        int availableCount = 0;
        for (int i = 0; i < RosterSize; i++)
        {
            if (!used[i])
            {
                available[availableCount++] = i;
            }
        }

        int chosenSlot = availableCount > 0
            ? available[rng.Next(availableCount)]
            : rng.Next(RosterSize);

        return CreateForSlot(chosenSlot);
    }

    internal static double ComputeSpeedBias(int slot, PenguinTemperament temperament, PenguinQuirk quirk)
    {
        double baseBias = temperament switch
        {
            PenguinTemperament.Chill => 0.87,
            PenguinTemperament.Shy => 0.96,
            PenguinTemperament.Cuddly => 0.98,
            PenguinTemperament.Curious => 1.01,
            PenguinTemperament.Grumpy => 1.03,
            PenguinTemperament.Dramatic => 1.05,
            PenguinTemperament.ThrillSeeker => 1.09,
            PenguinTemperament.Mischievous => 1.11,
            _ => 1.0
        };

        double quirkDelta = quirk switch
        {
            PenguinQuirk.Napper => -0.06,
            PenguinQuirk.Daydreamer => -0.03,
            PenguinQuirk.Explorer => 0.07,
            PenguinQuirk.Slider => 0.05,
            PenguinQuirk.Acrobat => 0.04,
            _ => 0.0
        };

        // Subtle deterministic per-slot stride offset in [-0.04, +0.04].
        double slotJitter = (((slot * 17) % 9) - 4) * 0.01;
        return Math.Clamp(baseBias + quirkDelta + slotJitter, 0.78, 1.24);
    }
}
