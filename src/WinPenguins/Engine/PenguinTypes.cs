namespace WinPenguins.Engine;

public enum PenguinAnimationId
{
    Walker,
    Faller,
    Tumbler,
    Floater,
    Climber,
    Bomber,
    Explosion,
    SantaWalker,
    SantaClimber
}

public enum PenguinSubType
{
    Unspecified,
    Normal,
    Santa
}

public enum FacingDirection
{
    Left = 0,
    Right = 1
}

public enum CollisionDirection
{
    Here,
    Left,
    Right,
    Up,
    Down
}

public enum MoveOutcome
{
    Moved,
    PartialMove,
    Blocked
}

public enum SoundEffectKind
{
    Explosion,
    Chirp,
    Eep
}

public sealed record AnimationSpec(
    PenguinAnimationId Id,
    string ResourcePrefix,
    int FrameCount,
    int DirectionCount,
    int BaseWidth,
    int BaseHeight,
    bool Cycles);

public static class AnimationCatalogSpecs
{
    public static readonly IReadOnlyDictionary<PenguinAnimationId, AnimationSpec> All =
        new Dictionary<PenguinAnimationId, AnimationSpec>
        {
            [PenguinAnimationId.Walker] = new(PenguinAnimationId.Walker, "Walker", 8, 2, 32, 32, true),
            [PenguinAnimationId.Faller] = new(PenguinAnimationId.Faller, "Faller", 8, 1, 32, 32, true),
            [PenguinAnimationId.Tumbler] = new(PenguinAnimationId.Tumbler, "Tumbler", 8, 1, 32, 32, true),
            [PenguinAnimationId.Floater] = new(PenguinAnimationId.Floater, "Floater", 8, 1, 32, 32, true),
            [PenguinAnimationId.Climber] = new(PenguinAnimationId.Climber, "Climber", 8, 2, 32, 32, true),
            [PenguinAnimationId.Bomber] = new(PenguinAnimationId.Bomber, "Bomber", 17, 1, 32, 32, false),
            [PenguinAnimationId.Explosion] = new(PenguinAnimationId.Explosion, "explosion", 3, 1, 64, 64, false),
            [PenguinAnimationId.SantaWalker] = new(PenguinAnimationId.SantaWalker, "SantaWalker", 8, 2, 32, 32, true),
            [PenguinAnimationId.SantaClimber] = new(PenguinAnimationId.SantaClimber, "SantaClimber", 8, 2, 32, 32, true),
        };

    public static AnimationSpec Get(PenguinAnimationId id) => All[id];
}
