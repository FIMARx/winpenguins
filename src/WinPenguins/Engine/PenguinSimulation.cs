using System.Drawing;
using WinPenguins.Config;

namespace WinPenguins.Engine;

public sealed class PenguinSimulation
{
    private readonly List<Penguin> _penguins = [];
    private readonly Random _rng;
    private IReadOnlyDictionary<PenguinAnimationId, AnimationSpec> _specs;
    private double _scale;
    private bool _enableParachutes = true;
    private bool _enablePersonalities = true;
    private Point? _lastCursorPos;

    public CollisionWorld World { get; }
    public ToySystem Toys { get; } = new();
    public SnowSystem Snow { get; } = new();

    public IReadOnlyList<Penguin> Penguins => _penguins;
    public double Scale => _scale;
    public int FishEatenCount { get; private set; }
    public int PortalJumpsCount { get; private set; }
    public int TrampolineBouncesCount { get; private set; }

    public event Action<SoundEffectKind>? SoundRequested;

    public PenguinSimulation(
        CollisionWorld world,
        double scale = 1.0,
        Random? rng = null,
        IReadOnlyDictionary<PenguinAnimationId, AnimationSpec>? specs = null)
    {
        World = world;
        _scale = Math.Clamp(scale, 0.5, 4.0);
        _rng = rng ?? Random.Shared;
        _specs = specs ?? AnimationCatalogSpecs.All;
    }

    public int BasePenguinSize => ScaleSize(_specs[PenguinAnimationId.Faller].BaseWidth, _scale);

    public void UpdateSpecs(IReadOnlyDictionary<PenguinAnimationId, AnimationSpec> specs)
    {
        _specs = specs;
        for (int i = 0; i < _penguins.Count; i++)
        {
            _penguins[i].UpdateSpecs(specs);
        }
    }

    public void SetScale(double newScale)
    {
        newScale = Math.Clamp(newScale, 0.5, 4.0);
        if (Math.Abs(newScale - _scale) < 0.001)
        {
            return;
        }

        _scale = newScale;
        for (int i = 0; i < _penguins.Count; i++)
        {
            _penguins[i].SetScale(_scale);
        }
    }

    public void EnsureCount(int targetCount)
    {
        targetCount = Math.Max(0, targetCount);

        while (_penguins.Count < targetCount)
        {
            _penguins.Add(SpawnPenguin());
        }

        if (targetCount < _penguins.Count)
        {
            for (int i = targetCount; i < _penguins.Count; i++)
            {
                _penguins[i].TriggerBomber();
            }
        }
    }

    public void ExplodeAllToBombers()
    {
        foreach (Penguin penguin in _penguins)
        {
            penguin.TriggerBomber();
        }
    }

    public void ScatterAll()
    {
        int count = _penguins.Count;
        _penguins.Clear();
        EnsureCount(count);
    }

    public Penguin? HitTest(int screenX, int screenY)
    {
        for (int i = _penguins.Count - 1; i >= 0; i--)
        {
            if (_penguins[i].HitTest(screenX, screenY))
            {
                return _penguins[i];
            }
        }

        return null;
    }

    public DesktopToy? HitTestToy(int screenX, int screenY) => Toys.HitTest(screenX, screenY);

    public void BeginToyGrab(DesktopToy toy) => Toys.BeginGrab(toy);

    public void MoveToyGrab(DesktopToy toy, int cursorX, int cursorY) =>
        Toys.MoveGrab(toy, World, cursorX, cursorY);

    public void ReleaseToyGrab(DesktopToy toy) => Toys.ReleaseGrab(toy);

    public void RemoveToy(DesktopToy toy) => Toys.Remove(toy);

    public void RerollPersonality(Penguin penguin)
    {
        PenguinPersonality next = PenguinCharacterCatalog.AllocateUnique(_penguins, _rng, excludeId: penguin.Personality.CharacterId);
        penguin.SetPersonality(next, resetMood: true);
        penguin.SpeedMultiplier = Math.Clamp(next.SpeedBias, 0.72, 1.38);
    }

    public void BeginGrab(Penguin penguin)
    {
        penguin.IsPerching = false;
        penguin.SlideSecondsRemaining = 0;
        penguin.BeginGrab();
        ApplyGrabStartPersonality(penguin);
        SoundRequested?.Invoke(SoundEffectKind.Eep);
    }

    public void MoveGrab(Penguin penguin, int cursorX, int cursorY)
    {
        int rawX = cursorX - penguin.Width / 2;
        int rawY = cursorY - penguin.Height / 2;
        Point clamped = World.ClampInsideDesktop(new Rectangle(rawX, rawY, penguin.Width, penguin.Height));
        penguin.MoveGrab(clamped.X, clamped.Y);
        penguin.CornerTrapSeconds = 0;
    }

    public void ReleaseGrab(Penguin penguin, double velocityPxPerSecX, double velocityPxPerSecY, bool wasClick)
    {
        if (wasClick)
        {
            ApplyClickPersonality(penguin);
            SoundRequested?.Invoke(SoundEffectKind.Chirp);
            return;
        }

        // Convert px/sec throw speed to classic 45ms step units, clamped safely.
        double maxSpeed = 18.0 * penguin.Scale;
        double vx = Math.Clamp(velocityPxPerSecX * 0.045, -maxSpeed, maxSpeed);
        double vy = Math.Clamp(velocityPxPerSecY * 0.045, -maxSpeed, maxSpeed);
        if (Math.Abs(vy) < 1.5 * penguin.Scale)
        {
            vy = 3.0 * penguin.Scale;
        }

        penguin.ReleaseGrab(vx, vy);
        ApplyThrowPersonality(penguin, vx, vy);
    }

    public void Step(AppSettings settings, Point? cursorScreenPos = null) =>
        Step(settings, settings.FrameDelayMs, cursorScreenPos, 0f);

    public void Step(AppSettings settings, double deltaMs, Point? cursorScreenPos = null, float audioPeak = 0f)
    {
        _enableParachutes = settings.EnableParachutes;
        _enablePersonalities = settings.EnablePersonalities;
        double stepUnits = Math.Clamp(deltaMs / Math.Max(1.0, settings.FrameDelayMs), 0.001, 2.0);
        double dtSec = Math.Max(0.0, deltaMs / 1000.0);
        bool autoPerMonitorDpi = settings.ScaleMode == SpriteScaleMode.Auto;

        Toys.Step(World, deltaMs);
        Snow.Step(World, _penguins, deltaMs, settings.EnableSnow);

        double cursorStepDist = 0;
        double cursorSpeedPxPerSec = 0;
        if (cursorScreenPos.HasValue && _lastCursorPos.HasValue && dtSec > 0.0005)
        {
            double cdx = cursorScreenPos.Value.X - _lastCursorPos.Value.X;
            double cdy = cursorScreenPos.Value.Y - _lastCursorPos.Value.Y;
            cursorStepDist = Math.Sqrt(cdx * cdx + cdy * cdy);
            cursorSpeedPxPerSec = cursorStepDist / dtSec;
        }
        _lastCursorPos = cursorScreenPos;

        bool anyPerching = _penguins.Any(p => p.Active && p.IsPerching);

        for (int i = 0; i < _penguins.Count; i++)
        {
            Penguin penguin = _penguins[i];
            if (!penguin.Active)
            {
                continue;
            }

            penguin.RelaxVisualTransforms(deltaMs);
            penguin.ShadowDistance = (penguin.StartingUp || penguin.IsTerminalAnimation)
                ? 999
                : World.FindFloorDistanceBelow(penguin.Bounds, 120);

            if (penguin.IsGrabbed)
            {
                if (_enablePersonalities && TryEscapeGrabByPersonality(penguin, dtSec))
                {
                    continue;
                }

                // Keep tumbling animation dangling while grabbed.
                var (_, _, advanceGrabFrame) = penguin.AccumulateSubStep(stepUnits);
                if (advanceGrabFrame)
                {
                    penguin.Advance(World, 0, 0, advanceFrame: true, force: true, out _);
                }

                continue;
            }

            if (penguin.IsPerching)
            {
                if (!settings.CursorPerch || !settings.InteractiveMouse || settings.CursorAvoidance || !cursorScreenPos.HasValue)
                {
                    penguin.IsPerching = false;
                    penguin.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
                    penguin.SetVelocity(0, 2.0 * penguin.Scale);
                }
                else if (cursorSpeedPxPerSec > 780.0)
                {
                    // Quick cursor shake flings the perched penguin playfully.
                    penguin.IsPerching = false;
                    double vx = Math.Clamp((cursorScreenPos.Value.X - (penguin.X + penguin.Width / 2)) * 0.4, -10.0 * penguin.Scale, 10.0 * penguin.Scale);
                    penguin.ReleaseGrab(vx, -5.5 * penguin.Scale);
                    penguin.TriggerSquash(0.84f, 1.22f);
                    penguin.TriggerEmote(PenguinEmote.Alert, 1.2);
                    SoundRequested?.Invoke(SoundEffectKind.Eep);
                }
                else
                {
                    Point target = World.ClampInsideDesktop(new Rectangle(
                        cursorScreenPos.Value.X - penguin.Width / 2,
                        cursorScreenPos.Value.Y - penguin.Height,
                        penguin.Width,
                        penguin.Height));
                    penguin.X = target.X;
                    penguin.Y = target.Y;
                    penguin.ResetFrameToStanding();
                    penguin.ShadowDistance = 999;
                    continue;
                }
            }

            if (autoPerMonitorDpi)
            {
                double monScale = World.GetScaleAtWithHysteresis(
                    penguin.X + penguin.Width / 2,
                    penguin.Y + penguin.Height / 2,
                    penguin.Scale,
                    _scale);
                if (Math.Abs(monScale - penguin.Scale) >= 0.001)
                {
                    penguin.SetScale(monScale);
                    if (!penguin.StartingUp && !World.IsPastBottomOfAnyCoveringMonitor(penguin.Bounds))
                    {
                        Point safe = World.ClampInsideDesktop(penguin.Bounds);
                        penguin.X = safe.X;
                        penguin.Y = safe.Y;
                        ResolveResizeOverlap(penguin);
                    }
                }
            }

            if (!penguin.StartingUp &&
                !penguin.IsTerminalAnimation &&
                !World.IsInsideDesktop(penguin.Bounds) &&
                !World.IsPastBottomOfAnyCoveringMonitor(penguin.Bounds))
            {
                Point safe = World.ClampInsideDesktop(penguin.Bounds);
                penguin.X = safe.X;
                penguin.Y = safe.Y;
            }

            int splatThreshold = settings.NeverSplat
                ? int.MaxValue
                : ScaleStep(settings.SplatDistance, penguin.Scale);

            if (_enablePersonalities &&
                settings.InteractiveMouse &&
                cursorScreenPos.HasValue &&
                !penguin.StartingUp &&
                !penguin.IsTerminalAnimation)
            {
                TryHandleCursorPetting(penguin, cursorScreenPos.Value, cursorStepDist, cursorSpeedPxPerSec, dtSec, settings);
            }

            if (penguin.IsIdle)
            {
                penguin.IdleSecondsRemaining -= deltaMs / 1000.0;
                if (!World.IsBlocked(penguin.Bounds, CollisionDirection.Down) ||
                    World.IsBlocked(penguin.Bounds, CollisionDirection.Here))
                {
                    penguin.IdleSecondsRemaining = 0;
                }
                else
                {
                    if (penguin.IdleSecondsRemaining <= 0)
                    {
                        penguin.IdleSecondsRemaining = 0;
                        // Occasionally glance/turn the other way after an idle pause.
                        if (_rng.Next(4) == 0)
                        {
                            FacingDirection flipped = FlipDirection(penguin.Direction);
                            penguin.SetAnimation(penguin.Animation, flipped);
                            double speed = 3.2 * penguin.Scale * penguin.SpeedMultiplier;
                            penguin.SetVelocity(flipped == FacingDirection.Right ? speed : -speed, 0);
                        }
                    }
                    continue;
                }
            }

            (int stepX, int stepY, bool advanceFrame) = penguin.AccumulateSubStep(stepUnits);

            if (penguin.IsTerminalAnimation)
            {
                penguin.Advance(World, stepX, stepY, advanceFrame, force: true, out bool explodedTerminal);
                if (explodedTerminal)
                {
                    SoundRequested?.Invoke(SoundEffectKind.Explosion);
                }

                continue;
            }

            bool startingAndBlocked = false;
            if (World.IsBlocked(penguin.Bounds, CollisionDirection.Here))
            {
                if (!penguin.StartingUp)
                {
                    if (penguin.TriggerExplosion())
                    {
                        SoundRequested?.Invoke(SoundEffectKind.Explosion);
                    }

                    continue;
                }

                startingAndBlocked = true;
            }
            else
            {
                penguin.StartingUp = false;
            }

            if (settings.CursorAvoidance && cursorScreenPos.HasValue)
            {
                ApplyCursorAvoidance(penguin, cursorScreenPos.Value);
            }
            else
            {
                if (_enablePersonalities &&
                    settings.InteractiveMouse &&
                    cursorScreenPos.HasValue &&
                    !penguin.StartingUp)
                {
                    ApplyPersonalityCursorProximity(penguin, cursorScreenPos.Value, cursorSpeedPxPerSec);
                }

                bool canPersonalityPerch = !_enablePersonalities ||
                    (penguin.Personality.Temperament != PenguinTemperament.Grumpy &&
                     !penguin.HoldsGrudge &&
                     (penguin.Personality.Temperament != PenguinTemperament.Shy || penguin.Mood >= 10f));

                if (settings.CursorPerch &&
                    settings.InteractiveMouse &&
                    canPersonalityPerch &&
                    !anyPerching &&
                    !penguin.StartingUp &&
                    cursorScreenPos.HasValue &&
                    cursorSpeedPxPerSec < 85.0 &&
                    penguin.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker)
                {
                    int cx = penguin.X + penguin.Width / 2;
                    int footY = penguin.Bounds.Bottom;
                    if (Math.Abs(cursorScreenPos.Value.X - cx) <= penguin.Width &&
                        Math.Abs(cursorScreenPos.Value.Y - footY) <= penguin.Height)
                    {
                        double dwellThreshold = penguin.SubType == PenguinSubType.Sleepy
                            ? 0.65
                            : (_enablePersonalities && penguin.Personality.Temperament == PenguinTemperament.Cuddly
                                ? 0.60
                                : (_enablePersonalities && penguin.Personality.Temperament == PenguinTemperament.Chill ? 0.85 : 1.25));
                        penguin.PerchDwellSeconds += dtSec;
                        if (penguin.PerchDwellSeconds >= dwellThreshold)
                        {
                            penguin.PerchDwellSeconds = 0;
                            penguin.IsPerching = true;
                            penguin.IdleSecondsRemaining = 0;
                            penguin.SlideSecondsRemaining = 0;
                            penguin.TriggerSquash(0.84f, 1.20f);
                            if (penguin.SubType == PenguinSubType.Sleepy)
                            {
                                penguin.TriggerEmote(PenguinEmote.Sleep, 2.2);
                            }
                            else if (_enablePersonalities && penguin.Personality.Temperament == PenguinTemperament.Cuddly)
                            {
                                penguin.AdjustMood(6f);
                                penguin.TriggerEmote(PenguinEmote.Heart, 1.6);
                            }
                            anyPerching = true;
                            SoundRequested?.Invoke(SoundEffectKind.Chirp);
                            continue;
                        }
                    }
                    else
                    {
                        penguin.PerchDwellSeconds = 0;
                    }
                }
            }

            bool groovySoul = _enablePersonalities &&
                (penguin.Personality.Quirk == PenguinQuirk.Groovy ||
                 penguin.Personality.Temperament == PenguinTemperament.Dramatic);
            float musicThreshold = penguin.SubType == PenguinSubType.DJ
                ? 0.16f
                : (groovySoul ? 0.19f : 0.28f);
            if (settings.EnableMusicDancing &&
                audioPeak > musicThreshold &&
                !penguin.StartingUp &&
                penguin.BeatCooldown <= 0 &&
                penguin.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker)
            {
                bool eagerDancer = penguin.SubType == PenguinSubType.DJ || groovySoul;
                penguin.BeatCooldown = eagerDancer ? 0.22 : 0.28;
                penguin.TriggerEmote(PenguinEmote.Music, 1.1);
                if (audioPeak > (eagerDancer ? 0.45f : 0.70f) && _rng.Next(eagerDancer ? 2 : 3) == 0 && !penguin.IsIdle)
                {
                    penguin.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
                    penguin.SetVelocity(penguin.VelocityX * 0.4, (eagerDancer ? -5.0 : -4.2) * penguin.Scale);
                    penguin.TriggerSquash(0.84f, 1.22f);
                }
                else
                {
                    penguin.TriggerSquash(1.16f, 0.84f);
                }
            }

            // Chef penguins periodically serve free Fish snacks onto surfaces!
            if (penguin.SubType == PenguinSubType.Chef &&
                !penguin.StartingUp &&
                penguin.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker &&
                !penguin.IsIdle &&
                penguin.ActionCooldown <= 0 &&
                Toys.Toys.Count < 8 &&
                World.IsBlocked(penguin.Bounds, CollisionDirection.Down))
            {
                TryServeChefFish(penguin);
            }

            if (penguin.SlideSecondsRemaining > 0)
            {
                penguin.SlideSecondsRemaining = Math.Max(0, penguin.SlideSecondsRemaining - dtSec);
                if (penguin.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker)
                {
                    double targetWalk = 3.2 * penguin.Scale * penguin.SpeedMultiplier;
                    if (penguin.SlideSecondsRemaining <= 0)
                    {
                        penguin.SetVelocity(penguin.Direction == FacingDirection.Right ? targetWalk : -targetWalk, 0);
                    }
                }
                else
                {
                    penguin.SlideSecondsRemaining = 0;
                }
            }
            if (penguin.HuffySecondsRemaining > 0)
            {
                penguin.HuffySecondsRemaining = Math.Max(0, penguin.HuffySecondsRemaining - dtSec);
                if (_enablePersonalities &&
                    penguin.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker &&
                    !penguin.IsSliding &&
                    !penguin.IsSeekingFish)
                {
                    double baseWalk = 3.2 * penguin.Scale * penguin.SpeedMultiplier;
                    double targetSpeed = penguin.HuffySecondsRemaining > 0 ? baseWalk * 1.35 : baseWalk;
                    penguin.VelocityX = penguin.Direction == FacingDirection.Right ? targetSpeed : -targetSpeed;
                }
            }

            ApplyToyInteractions(penguin, settings);

            MoveOutcome outcome = penguin.Advance(
                World,
                stepX,
                stepY,
                advanceFrame,
                startingAndBlocked,
                out bool explodedByBounds);

            if (explodedByBounds)
            {
                SoundRequested?.Invoke(SoundEffectKind.Explosion);
                continue;
            }

            bool attemptedMove = stepX != 0 || stepY != 0;
            UpdateBehavior(
                penguin,
                outcome,
                attemptedMove,
                startingAndBlocked,
                stepUnits,
                settings,
                splatThreshold);
        }

        ResolvePenguinInteractions();

        for (int i = _penguins.Count - 1; i >= 0; i--)
        {
            if (!_penguins[i].Active)
            {
                _penguins.RemoveAt(i);
            }
        }

        EnsureCount(settings.PenguinCount);
    }

    private void UpdateBehavior(
        Penguin p,
        MoveOutcome outcome,
        bool attemptedMove,
        bool startingAndBlocked,
        double stepUnits,
        AppSettings settings,
        int splatThreshold)
    {
        double s = p.Scale;
        double walkSpeed = 3.2 * s * p.SpeedMultiplier;
        double fallSpeed = 3.0 * s;
        double tumbleSpeed = 6.0 * s;
        double climbSpeed = (p.SubType == PenguinSubType.Jetpack ? 5.2 : 3.5) * s * p.SpeedMultiplier;
        double floatSpeed = 3.0 * s;
        double parachuteSpeed = 1.6 * s;

        switch (p.Animation)
        {
            case PenguinAnimationId.Faller:
                // Apply slight gravity when tossed upwards.
                if (p.VelocityY < fallSpeed)
                {
                    p.VelocityY = Math.Min(fallSpeed, p.VelocityY + 0.35 * s * stepUnits);
                }

                if (startingAndBlocked || !attemptedMove || outcome == MoveOutcome.Moved)
                {
                    break;
                }

                if (World.IsBlocked(p.Bounds, CollisionDirection.Down))
                {
                    FacingDirection dir = p.PreferredDirection ?? RandomDirection();
                    EnsureSubType(p, settings);
                    p.SetAnimation(
                        p.SubType == PenguinSubType.Santa
                            ? PenguinAnimationId.SantaWalker
                            : PenguinAnimationId.Walker,
                        dir);
                    p.SetVelocity(dir == FacingDirection.Right ? walkSpeed : -walkSpeed, 0);
                    p.PreferredDirection = null;
                    p.TriggerSquash(1.22f, 0.80f);
                }
                else
                {
                    bool rightBlocked = World.IsBlocked(p.Bounds, CollisionDirection.Right);
                    bool leftBlocked = World.IsBlocked(p.Bounds, CollisionDirection.Left);
                    bool ceilingBlocked = World.IsBlocked(p.Bounds, CollisionDirection.Up);

                    // Do not climb up a corner chute if pinned between walls or already touching the top edge.
                    if ((rightBlocked && leftBlocked) || ceilingBlocked)
                    {
                        double escapeVx = rightBlocked && leftBlocked
                            ? 0
                            : rightBlocked ? -s : s;
                        p.SetVelocity(escapeVx, Math.Max(fallSpeed, p.VelocityY));
                        p.PreferredDirection = escapeVx < 0 ? FacingDirection.Left : FacingDirection.Right;
                        break;
                    }

                    FacingDirection wallSide = rightBlocked
                        ? FacingDirection.Right
                        : leftBlocked
                            ? FacingDirection.Left
                            : (p.VelocityX > 0 ? FacingDirection.Right : FacingDirection.Left);

                    EnsureSubType(p, settings);
                    p.SetAnimation(
                        p.SubType == PenguinSubType.Santa
                            ? PenguinAnimationId.SantaClimber
                            : PenguinAnimationId.Climber,
                        wallSide);
                    p.SetVelocity(0, -climbSpeed);
                }
                break;

            case PenguinAnimationId.Tumbler:
                if (p.SubType == PenguinSubType.Jetpack &&
                    !startingAndBlocked &&
                    p.VelocityY > 2.2 * s &&
                    p.ActionCooldown <= 0 &&
                    (p.Y - p.TumbleStartY) > 100 * s &&
                    _rng.NextDouble() < 0.05 * stepUnits)
                {
                    p.ActionCooldown = 4.2;
                    p.JetBoostSeconds = 0.55;
                    p.TumbleStartY = p.Y;
                    p.SetVelocity(p.VelocityX, -5.8 * s);
                    p.TriggerEmote(PenguinEmote.Alert, 0.9);
                    SoundRequested?.Invoke(SoundEffectKind.Eep);
                }

                if (p.VelocityY < tumbleSpeed)
                {
                    p.VelocityY = Math.Min(tumbleSpeed, p.VelocityY + 0.45 * s * stepUnits);
                }

                if (settings.EnableParachutes &&
                    p.VelocityY > 0 &&
                    (p.Y - p.TumbleStartY) > (180 * s) &&
                    _rng.NextDouble() < 0.04 * stepUnits)
                {
                    p.IsParachuting = true;
                    p.SetAnimation(PenguinAnimationId.Floater, FacingDirection.Left);
                    double sway = (_rng.Next(3) - 1) * 0.6 * s;
                    p.SetVelocity(sway, parachuteSpeed);
                    SoundRequested?.Invoke(SoundEffectKind.Eep);
                    break;
                }

                if (!attemptedMove || outcome == MoveOutcome.Moved)
                {
                    break;
                }

                if (World.IsBlocked(p.Bounds, CollisionDirection.Down))
                {
                    double impactVx = p.VelocityX;
                    bool fastSlide = Math.Abs(impactVx) > 4.2 * s;
                    FacingDirection dir = fastSlide
                        ? (impactVx > 0 ? FacingDirection.Right : FacingDirection.Left)
                        : (p.PreferredDirection ?? RandomDirection());

                    EnsureSubType(p, settings);
                    p.SetAnimation(
                        p.SubType == PenguinSubType.Santa
                            ? PenguinAnimationId.SantaWalker
                            : PenguinAnimationId.Walker,
                        dir);

                    if (fastSlide)
                    {
                        double slideSpeed = Math.Clamp(Math.Abs(impactVx) * 0.75, walkSpeed * 1.4, 6.4 * s);
                        p.SetVelocity(dir == FacingDirection.Right ? slideSpeed : -slideSpeed, 0);
                        p.SlideSecondsRemaining = 0.65;
                    }
                    else
                    {
                        p.SetVelocity(dir == FacingDirection.Right ? walkSpeed : -walkSpeed, 0);
                    }

                    p.PreferredDirection = null;
                    p.TriggerSquash(1.24f, 0.78f);

                    if (p.SubType != PenguinSubType.Jetpack && p.Y - p.TumbleStartY > splatThreshold)
                    {
                        if (p.TriggerExplosion())
                        {
                            SoundRequested?.Invoke(SoundEffectKind.Explosion);
                        }
                    }
                }
                else
                {
                    // Bounced off a side wall or ceiling while tumbling; avoid negligible horizontal bounce creep.
                    double nextVx = -p.VelocityX * 0.5;
                    if (Math.Abs(nextVx) < 0.25 * s)
                    {
                        nextVx = 0;
                    }
                    p.SetVelocity(nextVx, Math.Max(fallSpeed, p.VelocityY));
                }
                break;

            case PenguinAnimationId.Walker:
            case PenguinAnimationId.SantaWalker:
                if (!World.IsBlocked(p.Bounds, CollisionDirection.Down))
                {
                    p.PreferredDirection = p.Direction;
                    p.SetAnimation(PenguinAnimationId.Tumbler, FacingDirection.Left);
                    p.SetVelocity(0, tumbleSpeed);
                    p.TumbleStartY = p.Y;
                }
                else if (attemptedMove && outcome != MoveOutcome.Moved)
                {
                    CollisionDirection moveDir = p.Direction == FacingDirection.Right
                        ? CollisionDirection.Right
                        : CollisionDirection.Left;

                    if (TryCrossMonitorSeamStep(p, moveDir, walkSpeed, climbSpeed, settings))
                    {
                        break;
                    }

                    int headroomProbe = Math.Max(4, (int)Math.Ceiling(8 * s));
                    bool noHeadroomAbove = World.IsBlocked(p.Bounds, CollisionDirection.Up) ||
                        !World.IsInsideDesktop(new Rectangle(p.X, p.Y - headroomProbe, p.Width, p.Height));

                    bool canClimbTowardMonitor = !noHeadroomAbove &&
                        World.HasAdjacentMonitorReachableUpward(p.Bounds, moveDir);

                    int choice;
                    if (noHeadroomAbove)
                    {
                        choice = 4;
                    }
                    else if (canClimbTowardMonitor && _rng.Next(10) < 6)
                    {
                        choice = 2; // Strongly bias climbing staggered monitor borders to migrate across monitors.
                    }
                    else if (_enablePersonalities && p.Personality.Quirk == PenguinQuirk.Climber && _rng.Next(10) < 6)
                    {
                        choice = 2;
                    }
                    else if (_enablePersonalities && p.Personality.Quirk == PenguinQuirk.Explorer && _rng.Next(10) < 4)
                    {
                        choice = 0;
                    }
                    else
                    {
                        choice = _rng.Next(8);
                    }

                    switch (choice)
                    {
                        case 0:
                        case 1:
                            p.IsParachuting = false;
                            p.SetAnimation(PenguinAnimationId.Floater, FacingDirection.Left);
                            int awaySign = p.VelocityX >= 0 ? -1 : 1;
                            double drift = _rng.Next(1, 5) * s * awaySign;
                            p.SetVelocity(drift, -floatSpeed);
                            p.PreferredDirection = awaySign < 0 ? FacingDirection.Left : FacingDirection.Right;
                            break;

                        case 2:
                        case 3:
                            EnsureSubType(p, settings);
                            p.SetAnimation(
                                p.SubType == PenguinSubType.Santa
                                    ? PenguinAnimationId.SantaClimber
                                    : PenguinAnimationId.Climber,
                                p.Direction);
                            p.SetVelocity(0, -climbSpeed);
                            if (canClimbTowardMonitor)
                            {
                                p.PreferredDirection = p.Direction;
                            }
                            if (_rng.Next(5) == 0)
                            {
                                SoundRequested?.Invoke(SoundEffectKind.Eep);
                            }
                            break;

                        default:
                            EnsureSubType(p, settings);
                            FacingDirection flipped = FlipDirection(p.Direction);
                            p.SetAnimation(
                                p.SubType == PenguinSubType.Santa
                                    ? PenguinAnimationId.SantaWalker
                                    : PenguinAnimationId.Walker,
                                flipped);
                            p.SetVelocity(flipped == FacingDirection.Right ? walkSpeed : -walkSpeed, 0);
                            break;
                    }
                }
                else if (settings.EnableIdlePauses &&
                    _rng.NextDouble() < GetIdlePauseChance(p) * stepUnits)
                {
                    bool cozyNapper = p.SubType == PenguinSubType.Sleepy ||
                        (_enablePersonalities &&
                         (p.Personality.Temperament == PenguinTemperament.Chill || p.Personality.Quirk == PenguinQuirk.Napper));
                    bool daydreamer = _enablePersonalities && p.Personality.Quirk == PenguinQuirk.Daydreamer;
                    p.IdleSecondsRemaining = cozyNapper
                        ? (2.0 + _rng.NextDouble() * 2.6)
                        : (0.45 + _rng.NextDouble() * 0.9);
                    p.ResetFrameToStanding();
                    if (cozyNapper)
                    {
                        p.TriggerEmote(PenguinEmote.Sleep, p.IdleSecondsRemaining);
                    }
                    else if (daydreamer && _rng.Next(2) == 0)
                    {
                        p.TriggerEmote(PenguinEmote.Curious, Math.Min(1.2, p.IdleSecondsRemaining));
                    }
                    else if (_rng.Next(8) == 0)
                    {
                        SoundRequested?.Invoke(SoundEffectKind.Chirp);
                    }
                }
                else if (settings.EnableIdlePauses &&
                    !p.IsSliding &&
                    _rng.NextDouble() < GetSlideChance(p) * stepUnits)
                {
                    bool isDj = p.SubType == PenguinSubType.DJ;
                    bool lovesSliding = _enablePersonalities &&
                        (p.Personality.Quirk == PenguinQuirk.Slider ||
                         p.Personality.Temperament is PenguinTemperament.ThrillSeeker or PenguinTemperament.Mischievous);
                    p.SlideSecondsRemaining = (isDj || lovesSliding ? 0.90 : 0.55) + _rng.NextDouble() * 0.5;
                    double slideSpeed = (isDj || lovesSliding ? 6.0 : 5.4) * s * p.SpeedMultiplier;
                    p.SetVelocity(p.Direction == FacingDirection.Right ? slideSpeed : -slideSpeed, 0);
                    if (isDj)
                    {
                        p.TriggerEmote(PenguinEmote.Music, p.SlideSecondsRemaining);
                    }
                    else if (lovesSliding)
                    {
                        p.TriggerEmote(PenguinEmote.Sparkle, p.SlideSecondsRemaining);
                    }
                }
                break;

            case PenguinAnimationId.Climber:
            case PenguinAnimationId.SantaClimber:
                CollisionDirection climbWall = p.Direction == FacingDirection.Left
                    ? CollisionDirection.Left
                    : CollisionDirection.Right;

                if (!World.IsBlocked(p.Bounds, climbWall))
                {
                    EnsureSubType(p, settings);
                    p.SetAnimation(
                        p.SubType == PenguinSubType.Santa
                            ? PenguinAnimationId.SantaWalker
                            : PenguinAnimationId.Walker,
                        p.Direction);
                    p.SetVelocity(p.Direction == FacingDirection.Right ? walkSpeed : -walkSpeed, 0);

                    int nudge = p.Direction == FacingDirection.Right ? 1 : -1;
                    Rectangle nudged = new(p.X + nudge, p.Y, p.Width, p.Height);
                    if (World.IsPositionValid(nudged, ignoreWindows: false))
                    {
                        p.X += nudge;
                    }

                    p.PreferredDirection = p.Direction;
                }
                else if (attemptedMove && outcome != MoveOutcome.Moved)
                {
                    FacingDirection away = FlipDirection(p.Direction);
                    CollisionDirection awayDir = away == FacingDirection.Left
                        ? CollisionDirection.Left
                        : CollisionDirection.Right;
                    bool chuteBlocked = World.IsBlocked(p.Bounds, awayDir);

                    p.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
                    double bounceX = chuteBlocked ? 0 : (away == FacingDirection.Left ? -s : s);
                    p.SetVelocity(bounceX, fallSpeed);
                    p.PreferredDirection = away;
                }
                break;

            case PenguinAnimationId.Floater:
                if (attemptedMove && outcome != MoveOutcome.Moved)
                {
                    if (p.IsParachuting && World.IsBlocked(p.Bounds, CollisionDirection.Down))
                    {
                        p.IsParachuting = false;
                        FacingDirection dir = p.PreferredDirection ?? RandomDirection();
                        EnsureSubType(p, settings);
                        p.SetAnimation(
                            p.SubType == PenguinSubType.Santa
                                ? PenguinAnimationId.SantaWalker
                                : PenguinAnimationId.Walker,
                            dir);
                        p.SetVelocity(dir == FacingDirection.Right ? walkSpeed : -walkSpeed, 0);
                        p.PreferredDirection = null;
                        p.TriggerSquash(1.16f, 0.86f);
                    }
                    else if (!p.IsParachuting && World.IsBlocked(p.Bounds, CollisionDirection.Up))
                    {
                        bool rightBlocked = World.IsBlocked(p.Bounds, CollisionDirection.Right);
                        bool leftBlocked = World.IsBlocked(p.Bounds, CollisionDirection.Left);
                        double fallVx = rightBlocked && leftBlocked
                            ? 0
                            : rightBlocked
                                ? -s
                                : leftBlocked
                                    ? s
                                    : (p.VelocityX > 0 ? s : -s);

                        p.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
                        p.SetVelocity(fallVx, fallSpeed);
                        p.PreferredDirection = fallVx > 0 ? FacingDirection.Right : FacingDirection.Left;
                    }
                    else
                    {
                        p.SetVelocity(-p.VelocityX, p.IsParachuting ? parachuteSpeed : -floatSpeed);
                    }
                }
                break;
        }

        // Anti-corner-trap watchdog: if pinned in a ceiling corner bouncing repeatedly, escape cleanly.
        if (!startingAndBlocked &&
            !p.IsTerminalAnimation &&
            World.IsBlocked(p.Bounds, CollisionDirection.Up) &&
            (World.IsBlocked(p.Bounds, CollisionDirection.Left) || World.IsBlocked(p.Bounds, CollisionDirection.Right)))
        {
            p.CornerTrapSeconds += (settings.FrameDelayMs * stepUnits) / 1000.0;
            if (p.CornerTrapSeconds >= 1.0)
            {
                p.CornerTrapSeconds = 0;
                bool rightBlocked = World.IsBlocked(p.Bounds, CollisionDirection.Right);
                bool leftBlocked = World.IsBlocked(p.Bounds, CollisionDirection.Left);
                FacingDirection escapeDir = rightBlocked && !leftBlocked
                    ? FacingDirection.Left
                    : FacingDirection.Right;

                if (World.IsBlocked(p.Bounds, CollisionDirection.Down))
                {
                    EnsureSubType(p, settings);
                    p.SetAnimation(
                        p.SubType == PenguinSubType.Santa ? PenguinAnimationId.SantaWalker : PenguinAnimationId.Walker,
                        escapeDir);
                    p.SetVelocity(escapeDir == FacingDirection.Right ? walkSpeed : -walkSpeed, 0);
                }
                else
                {
                    p.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
                    p.SetVelocity(rightBlocked && leftBlocked ? 0 : (escapeDir == FacingDirection.Right ? s : -s), fallSpeed);
                    p.PreferredDirection = escapeDir;
                }
            }
        }
        else
        {
            p.CornerTrapSeconds = 0;
        }
    }

    private void ApplyCursorAvoidance(Penguin p, Point cursor)
    {
        if (p.SubType == PenguinSubType.King ||
            p.Animation is not (PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker))
        {
            return;
        }

        int cx = p.X + p.Width / 2;
        int cy = p.Y + p.Height / 2;
        int dx = cx - cursor.X;
        int dy = cy - cursor.Y;
        int radius = ScaleSize(48, p.Scale);

        if ((dx * dx) + (dy * dy) > radius * radius)
        {
            return;
        }

        double walkSpeed = 3.2 * p.Scale * p.SpeedMultiplier;
        if (dx < 0 && p.Direction == FacingDirection.Right)
        {
            p.SetAnimation(p.Animation, FacingDirection.Left);
            p.SetVelocity(-walkSpeed, 0);
        }
        else if (dx > 0 && p.Direction == FacingDirection.Left)
        {
            p.SetAnimation(p.Animation, FacingDirection.Right);
            p.SetVelocity(walkSpeed, 0);
        }
    }

    private Penguin SpawnPenguin()
    {
        Rectangle mon = World.PickSpawnMonitor(_rng, out double monitorScale);
        double effectiveScale = monitorScale > 0 ? monitorScale : _scale;
        int size = ScaleSize(_specs[PenguinAnimationId.Faller].BaseWidth, effectiveScale);
        int maxSpan = Math.Max(1, mon.Width - size);
        int spawnX = mon.Left + _rng.Next(maxSpan);
        int spawnY = mon.Top;
        PenguinPersonality identity = PenguinCharacterCatalog.AllocateUnique(_penguins, _rng);
        double speedMult = _enablePersonalities
            ? Math.Clamp(identity.SpeedBias * (0.95 + (_rng.NextDouble() * 0.10)), 0.72, 1.38)
            : 0.85 + (_rng.NextDouble() * 0.33);

        Penguin spawned;
        if (_enableParachutes && _rng.Next(5) == 0)
        {
            double sway = (_rng.Next(3) - 1) * 0.6 * effectiveScale;
            spawned = new Penguin(spawnX, spawnY, sway, 1.6 * effectiveScale, effectiveScale, speedMult, _specs)
            {
                IsParachuting = true
            };
            spawned.SetAnimation(PenguinAnimationId.Floater, FacingDirection.Left);
        }
        else
        {
            double driftX = (_rng.Next(3) - 1) * effectiveScale;
            double fallY = 3.0 * effectiveScale;
            spawned = new Penguin(spawnX, spawnY, driftX, fallY, effectiveScale, speedMult, _specs);
        }

        spawned.SetPersonality(identity, resetMood: true);
        return spawned;
    }

    private void EnsureSubType(Penguin p, AppSettings settings)
    {
        if (p.SubType != PenguinSubType.Unspecified)
        {
            return;
        }

        if (_rng.Next(100) < settings.SantaPercent)
        {
            p.SetSubType(PenguinSubType.Santa, World);
            return;
        }

        IReadOnlyList<PenguinSubType> crew = settings.GetEnabledSpecialCrew();
        if (crew.Count > 0 && _rng.Next(100) < settings.SpecialCrewPercent)
        {
            PenguinSubType chosen = crew[_rng.Next(crew.Count)];
            p.ActionCooldown = 8.0 + _rng.NextDouble() * 10.0;
            p.SetSubType(chosen, World);
            return;
        }

        p.SetSubType(PenguinSubType.Normal, World);
    }

    private FacingDirection RandomDirection() =>
        _rng.Next(2) == 0 ? FacingDirection.Left : FacingDirection.Right;

    private static FacingDirection FlipDirection(FacingDirection dir) =>
        dir == FacingDirection.Left ? FacingDirection.Right : FacingDirection.Left;

    private bool TryCrossMonitorSeamStep(
        Penguin p,
        CollisionDirection moveDir,
        double walkSpeed,
        double climbSpeed,
        AppSettings settings)
    {
        if (!World.IsNearMonitorBoundary(p.Bounds, moveDir, margin: 8))
        {
            return false;
        }

        int dx = moveDir == CollisionDirection.Right ? 2 : -2;
        int maxStep = Math.Max(32, (int)Math.Ceiling(1.5 * p.Height));
        int smoothHopLimit = Math.Max(6, p.Height / 4);

        for (int dy = 1; dy <= maxStep; dy++)
        {
            var lifted = new Rectangle(p.X, p.Y - dy, p.Width, p.Height);
            if (!World.IsInsideDesktop(lifted) || World.IntersectsAnyWindow(lifted))
            {
                break;
            }

            var across = new Rectangle(p.X + dx, p.Y - dy, p.Width, p.Height);
            if (!World.IsPositionValid(across, ignoreWindows: false))
            {
                continue;
            }

            if (dy <= smoothHopLimit)
            {
                p.X += dx;
                p.Y -= dy;
                p.SetVelocity(moveDir == CollisionDirection.Right ? walkSpeed : -walkSpeed, 0);
                return true;
            }

            EnsureSubType(p, settings);
            p.SetAnimation(
                p.SubType == PenguinSubType.Santa ? PenguinAnimationId.SantaClimber : PenguinAnimationId.Climber,
                p.Direction);
            p.SetVelocity(0, -climbSpeed);
            p.PreferredDirection = p.Direction;
            return true;
        }

        return false;
    }

    private void ResolveResizeOverlap(Penguin p)
    {
        if (!World.IsBlocked(p.Bounds, CollisionDirection.Here))
        {
            return;
        }

        int maxLift = Math.Max(24, p.Height);
        for (int dy = 1; dy <= maxLift; dy++)
        {
            var lifted = new Rectangle(p.X, p.Y - dy, p.Width, p.Height);
            if (World.IsPositionValid(lifted, ignoreWindows: false))
            {
                p.Y -= dy;
                return;
            }
        }

        for (int offset = 1; offset <= 12; offset++)
        {
            foreach (int dx in new[] { -offset, offset })
            {
                var shifted = new Rectangle(p.X + dx, p.Y, p.Width, p.Height);
                if (World.IsPositionValid(shifted, ignoreWindows: false))
                {
                    p.X += dx;
                    return;
                }
            }
        }
    }

    private void ApplyToyInteractions(Penguin p, AppSettings settings)
    {
        if (p.IsTerminalAnimation)
        {
            return;
        }

        double s = p.Scale;
        double walkSpeed = 3.2 * s * p.SpeedMultiplier;
        if (Toys.Toys.Count == 0)
        {
            if (p.IsSeekingFish)
            {
                p.IsSeekingFish = false;
                if (p.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker && !p.IsSliding)
                {
                    p.VelocityX = p.Direction == FacingDirection.Right ? walkSpeed : -walkSpeed;
                }
            }
            return;
        }

        Rectangle pBounds = p.Bounds;
        int cx = p.X + p.Width / 2;
        int cy = p.Y + p.Height / 2;

        // Seek nearby fish treats on the same reachable floor without pinning against walls.
        bool isFoodie = _enablePersonalities && p.Personality.Quirk == PenguinQuirk.Foodie;
        double fishSeekRadius = isFoodie ? 500 * s : 340 * s;
        int maxFishFootDelta = Math.Max(18, (int)(20 * s));
        DesktopToy? nearestFish = p.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker && !p.IsIdle
            ? Toys.FindNearestFish(cx, cy, fishSeekRadius, pBounds.Bottom, maxFishFootDelta)
            : null;

        bool canApproachFish = false;
        if (nearestFish != null)
        {
            int fishCx = (int)Math.Round(nearestFish.X + nearestFish.Width * 0.5);
            FacingDirection want = fishCx > cx ? FacingDirection.Right : FacingDirection.Left;
            CollisionDirection wantDir = want == FacingDirection.Right
                ? CollisionDirection.Right
                : CollisionDirection.Left;
            bool blockedByWall = Math.Abs(fishCx - cx) > 4 &&
                (World.IsBlocked(pBounds, wantDir) || World.HasHorizontalBarrierBetween(pBounds, fishCx));

            if (!blockedByWall)
            {
                canApproachFish = true;
                p.IsSeekingFish = true;
                if (Math.Abs(fishCx - cx) > 4)
                {
                    if (p.Direction != want)
                    {
                        p.SetAnimation(p.Animation, want);
                    }

                    double mult = p.SubType == PenguinSubType.Baby ? 1.55 : (isFoodie ? 1.50 : 1.35);
                    double sprint = p.IsSliding ? Math.Abs(p.VelocityX) : walkSpeed * mult;
                    p.VelocityX = want == FacingDirection.Right ? sprint : -sprint;
                    p.VelocityY = 0;
                }
            }
        }

        if (!canApproachFish && p.IsSeekingFish)
        {
            p.IsSeekingFish = false;
            if (!p.IsSliding && p.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker)
            {
                p.VelocityX = p.Direction == FacingDirection.Right ? walkSpeed : -walkSpeed;
            }
        }

        for (int i = 0; i < Toys.Toys.Count; i++)
        {
            DesktopToy toy = Toys.Toys[i];
            if (!toy.Active || toy.IsGrabbed)
            {
                continue;
            }

            bool nearFish = toy.Kind == ToyKind.Fish &&
                Math.Abs((toy.X + toy.Width * 0.5) - cx) <= Math.Max(14, toy.Width * 0.7) &&
                Math.Abs(toy.Bounds.Bottom - pBounds.Bottom) <= Math.Max(18, (int)(20 * s));

            if (!nearFish && !pBounds.IntersectsWith(toy.Bounds))
            {
                continue;
            }

            switch (toy.Kind)
            {
                case ToyKind.Fish:
                    toy.Active = false;
                    FishEatenCount++;
                    p.IdleSecondsRemaining = 0;
                    p.HuffySecondsRemaining = 0;
                    p.AdjustMood(isFoodie ? 40f : 30f);
                    p.VelocityX = p.Direction == FacingDirection.Right ? walkSpeed : -walkSpeed;
                    p.TriggerSquash(0.84f, 1.24f);
                    p.TriggerEmote(PenguinEmote.Heart, 1.8);
                    SoundRequested?.Invoke(SoundEffectKind.Chirp);
                    return;

                case ToyKind.Trampoline:
                    if (p.BounceCooldown <= 0 && !p.StartingUp)
                    {
                        TrampolineBouncesCount++;
                        p.IsParachuting = false;
                        p.IdleSecondsRemaining = 0;
                        p.SlideSecondsRemaining = 0;
                        p.BounceCooldown = 0.45;
                        toy.Pulse = 1.0;
                        bool lovesBounce = _enablePersonalities &&
                            (p.Personality.Quirk == PenguinQuirk.Acrobat ||
                             p.Personality.Temperament == PenguinTemperament.ThrillSeeker);
                        double vx = p.Direction == FacingDirection.Right ? 2.4 * s : -2.4 * s;
                        double jumpVy = p.SubType == PenguinSubType.Jetpack
                            ? -13.5 * s
                            : (lovesBounce ? -12.8 * s : -11.5 * s);
                        if (p.SubType == PenguinSubType.Jetpack)
                        {
                            p.JetBoostSeconds = 0.45;
                        }
                        p.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
                        p.SetVelocity(vx, jumpVy);
                        p.TumbleStartY = p.Y;
                        p.TriggerSquash(0.78f, 1.28f);
                        p.AdjustMood(lovesBounce ? 10f : 4f);
                        p.TriggerEmote(lovesBounce ? PenguinEmote.Sparkle : PenguinEmote.Alert, 1.1);
                        SoundRequested?.Invoke(SoundEffectKind.Eep);
                        return;
                    }
                    break;

                case ToyKind.PortalA:
                case ToyKind.PortalB:
                    if (p.PortalCooldown <= 0 && !p.StartingUp)
                    {
                        DesktopToy? dest = Toys.GetTwinPortal(toy.Kind);
                        if (dest != null)
                        {
                            PortalJumpsCount++;
                            double destScale = World.GetScaleAt(
                                (int)Math.Round(dest.X + dest.Width * 0.5),
                                (int)Math.Round(dest.Y + dest.Height * 0.5),
                                settings.ScaleMode == SpriteScaleMode.Auto ? _scale : p.Scale);
                            if (settings.ScaleMode == SpriteScaleMode.Auto)
                            {
                                p.SetScale(destScale);
                            }

                            Rectangle target = new(
                                (int)Math.Round(dest.X + (dest.Width - p.Width) * 0.5),
                                (int)Math.Round(dest.Y + (dest.Height - p.Height) * 0.5),
                                p.Width,
                                p.Height);
                            Point safe = World.ClampInsideDesktop(target);
                            p.X = safe.X;
                            p.Y = safe.Y;
                            p.PortalCooldown = 1.5;
                            toy.Pulse = 1.0;
                            dest.Pulse = 1.0;
                            p.TriggerSquash(0.76f, 1.30f);
                            p.TriggerEmote(PenguinEmote.Alert, 1.1);
                            SoundRequested?.Invoke(SoundEffectKind.Eep);
                            return;
                        }
                    }
                    break;
            }
        }
    }

    private void TryServeChefFish(Penguin penguin)
    {
        (int fishW, int fishH) = DesktopToy.GetDimensions(ToyKind.Fish, penguin.Scale);
        int rawFishY = penguin.Bounds.Bottom - fishH;
        int frontX = penguin.Direction == FacingDirection.Right ? penguin.Bounds.Right + 10 : penguin.X - fishW - 10;
        int behindX = penguin.Direction == FacingDirection.Right ? penguin.X - fishW - 10 : penguin.Bounds.Right + 10;
        int centerX = penguin.X + ((penguin.Width - fishW) / 2);

        Span<int> candidates = [frontX, behindX, centerX];
        Point? spawnPoint = null;
        for (int i = 0; i < candidates.Length; i++)
        {
            Point clamped = World.ClampInsideDesktop(new Rectangle(candidates[i], rawFishY, fishW, fishH));
            if (World.IsPositionValid(new Rectangle(clamped.X, clamped.Y, fishW, fishH), ignoreWindows: false))
            {
                spawnPoint = clamped;
                break;
            }
        }

        if (!spawnPoint.HasValue)
        {
            Point fallback = World.ClampInsideDesktop(new Rectangle(frontX, rawFishY, fishW, fishH));
            if (World.TrySurfaceFromOverlappingWindows(new Rectangle(fallback.X, fallback.Y, fishW, fishH), out Point surfaced) &&
                World.IsPositionValid(new Rectangle(surfaced.X, surfaced.Y, fishW, fishH), ignoreWindows: false))
            {
                spawnPoint = surfaced;
            }
        }

        if (!spawnPoint.HasValue)
        {
            penguin.ActionCooldown = 2.0;
            return;
        }

        penguin.ActionCooldown = 14.0 + (_rng.NextDouble() * 8.0);
        Toys.SpawnToy(ToyKind.Fish, spawnPoint.Value.X, spawnPoint.Value.Y, penguin.Scale);
        penguin.TriggerEmote(PenguinEmote.Heart, 1.4);
        SoundRequested?.Invoke(SoundEffectKind.Chirp);
    }

    private void ResolvePenguinInteractions()
    {
        for (int i = 0; i < _penguins.Count; i++)
        {
            Penguin a = _penguins[i];
            if (!a.Active || a.StartingUp || a.IsGrabbed || a.IsPerching || a.IsTerminalAnimation)
            {
                continue;
            }

            Rectangle ra = a.Bounds;
            for (int j = i + 1; j < _penguins.Count; j++)
            {
                Penguin b = _penguins[j];
                if (!b.Active || b.StartingUp || b.IsGrabbed || b.IsPerching || b.IsTerminalAnimation)
                {
                    continue;
                }

                Rectangle rb = b.Bounds;
                if (!ra.IntersectsWith(rb))
                {
                    // Baby penguins gently flock behind nearby adult walkers on the same ledge.
                    if (a.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker &&
                        b.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker &&
                        Math.Abs(ra.Bottom - rb.Bottom) <= 8 &&
                        Math.Abs(ra.X - rb.X) <= 160 * Math.Max(a.Scale, b.Scale) &&
                        _rng.Next(25) == 0)
                    {
                        if (a.SubType == PenguinSubType.Baby && b.SubType != PenguinSubType.Baby && !a.IsIdle)
                        {
                            FacingDirection follow = rb.X > ra.X ? FacingDirection.Right : FacingDirection.Left;
                            if (a.Direction != follow)
                            {
                                a.SetAnimation(a.Animation, follow);
                                double spd = 3.2 * a.Scale * a.SpeedMultiplier;
                                a.VelocityX = follow == FacingDirection.Right ? spd : -spd;
                            }
                        }
                        else if (b.SubType == PenguinSubType.Baby && a.SubType != PenguinSubType.Baby && !b.IsIdle)
                        {
                            FacingDirection follow = ra.X > rb.X ? FacingDirection.Right : FacingDirection.Left;
                            if (b.Direction != follow)
                            {
                                b.SetAnimation(b.Animation, follow);
                                double spd = 3.2 * b.Scale * b.SpeedMultiplier;
                                b.VelocityX = follow == FacingDirection.Right ? spd : -spd;
                            }
                        }
                    }
                    continue;
                }

                // Check bouncy trampoline landing onto another penguin or parachute umbrella.
                if (TryBouncePair(a, ra, b, rb) || TryBouncePair(b, rb, a, ra))
                {
                    continue;
                }

                bool involvesKing = a.SubType == PenguinSubType.King || b.SubType == PenguinSubType.King;

                // Opposite-facing walkers briefly greet with a cheerful hop/chirp (or bow when meeting a King!).
                if (a.GreetingCooldown <= 0 &&
                    b.GreetingCooldown <= 0 &&
                    a.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker &&
                    b.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker &&
                    (involvesKing || a.Direction != b.Direction) &&
                    Math.Abs((ra.X + ra.Width / 2) - (rb.X + rb.Width / 2)) < Math.Min(ra.Width, rb.Width) * 0.6)
                {
                    bool aGrumpy = _enablePersonalities && !involvesKing &&
                        (a.Personality.Quirk == PenguinQuirk.Loner || a.Personality.Temperament == PenguinTemperament.Grumpy || a.HoldsGrudge);
                    bool bGrumpy = _enablePersonalities && !involvesKing &&
                        (b.Personality.Quirk == PenguinQuirk.Loner || b.Personality.Temperament == PenguinTemperament.Grumpy || b.HoldsGrudge);
                    bool extraSocial = _enablePersonalities && !aGrumpy && !bGrumpy &&
                        (a.Personality.Quirk == PenguinQuirk.Social || b.Personality.Quirk == PenguinQuirk.Social ||
                         a.Personality.Temperament == PenguinTemperament.Cuddly || b.Personality.Temperament == PenguinTemperament.Cuddly);

                    double cooldown = extraSocial ? 2.6 : 4.5;
                    a.GreetingCooldown = cooldown;
                    b.GreetingCooldown = cooldown;
                    a.TriggerSquash(0.88f, 1.15f);
                    b.TriggerSquash(0.88f, 1.15f);

                    if (aGrumpy || bGrumpy)
                    {
                        a.TriggerEmote(aGrumpy ? PenguinEmote.Angry : PenguinEmote.Alert, 1.2);
                        b.TriggerEmote(bGrumpy ? PenguinEmote.Angry : PenguinEmote.Alert, 1.2);
                    }
                    else
                    {
                        a.TriggerEmote(PenguinEmote.Heart, 1.5);
                        b.TriggerEmote(PenguinEmote.Heart, 1.5);
                        if (extraSocial)
                        {
                            a.AdjustMood(5f);
                            b.AdjustMood(5f);
                        }
                    }

                    if (involvesKing)
                    {
                        if (a.SubType != PenguinSubType.King) a.IdleSecondsRemaining = Math.Max(a.IdleSecondsRemaining, 0.35);
                        if (b.SubType != PenguinSubType.King) b.IdleSecondsRemaining = Math.Max(b.IdleSecondsRemaining, 0.35);
                    }
                    if (_rng.Next(2) == 0)
                    {
                        SoundRequested?.Invoke(SoundEffectKind.Chirp);
                    }
                }
            }
        }
    }

    private bool TryBouncePair(Penguin top, Rectangle rt, Penguin bottom, Rectangle rb)
    {
        if (top.BounceCooldown > 0 || bottom.BounceCooldown > 0)
        {
            return false;
        }

        if (top.Animation is not (PenguinAnimationId.Faller or PenguinAnimationId.Tumbler) ||
            top.VelocityY <= 1.2 * top.Scale)
        {
            return false;
        }

        bool aboveTopHalf = rt.Bottom >= rb.Top && rt.Bottom <= rb.Top + rb.Height * 0.65 && rt.Top < rb.Top;
        if (!aboveTopHalf)
        {
            return false;
        }

        top.BounceCooldown = 0.45;
        bottom.BounceCooldown = 0.45;
        top.TumbleStartY = top.Y;
        double deflectX = rt.X + rt.Width / 2 < rb.X + rb.Width / 2 ? -1.8 * top.Scale : 1.8 * top.Scale;
        top.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
        top.SetVelocity(deflectX, -5.2 * top.Scale);
        top.TriggerSquash(0.84f, 1.22f);
        bottom.TriggerSquash(1.24f, 0.78f);
        SoundRequested?.Invoke(SoundEffectKind.Chirp);
        return true;
    }

    private double GetIdlePauseChance(Penguin p)
    {
        if (p.SubType == PenguinSubType.Sleepy)
        {
            return 0.011;
        }

        if (_enablePersonalities)
        {
            if (p.Personality.Temperament == PenguinTemperament.Chill || p.Personality.Quirk == PenguinQuirk.Napper)
            {
                return 0.0095;
            }

            if (p.Personality.Quirk == PenguinQuirk.Daydreamer)
            {
                return 0.0075;
            }
        }

        return 0.0045;
    }

    private double GetSlideChance(Penguin p)
    {
        if (p.SubType == PenguinSubType.DJ)
        {
            return 0.0026;
        }

        if (_enablePersonalities &&
            (p.Personality.Quirk == PenguinQuirk.Slider ||
             p.Personality.Temperament is PenguinTemperament.ThrillSeeker or PenguinTemperament.Mischievous))
        {
            return 0.0024;
        }

        return 0.0010;
    }

    private void ApplyGrabStartPersonality(Penguin penguin)
    {
        if (!_enablePersonalities)
        {
            penguin.TriggerEmote(PenguinEmote.Alert, 0.9);
            return;
        }

        if (penguin.Personality.Temperament == PenguinTemperament.Grumpy || penguin.HoldsGrudge)
        {
            penguin.AdjustMood(-14f);
            penguin.TriggerEmote(PenguinEmote.Angry, 1.4);
            penguin.TriggerSquash(1.22f, 0.80f);
            return;
        }

        switch (penguin.Personality.Temperament)
        {
            case PenguinTemperament.Cuddly:
                penguin.AdjustMood(4f);
                penguin.TriggerEmote(PenguinEmote.Heart, 1.2);
                penguin.TriggerSquash(0.90f, 1.12f);
                break;

            case PenguinTemperament.ThrillSeeker:
                penguin.AdjustMood(6f);
                penguin.TriggerEmote(PenguinEmote.Sparkle, 1.2);
                penguin.TriggerSquash(0.84f, 1.20f);
                break;

            case PenguinTemperament.Mischievous:
                penguin.AdjustMood(3f);
                penguin.TriggerEmote(PenguinEmote.Sparkle, 1.0);
                break;

            case PenguinTemperament.Chill:
                penguin.AdjustMood(-1f);
                penguin.TriggerSquash(0.95f, 1.05f);
                break;

            case PenguinTemperament.Shy:
                penguin.AdjustMood(-10f);
                penguin.TriggerEmote(PenguinEmote.Alert, 1.3);
                break;

            case PenguinTemperament.Curious:
                penguin.TriggerEmote(PenguinEmote.Curious, 1.1);
                break;

            case PenguinTemperament.Dramatic:
                penguin.TriggerEmote(PenguinEmote.Alert, 1.5);
                penguin.TriggerSquash(0.76f, 1.32f);
                break;

            default:
                penguin.TriggerEmote(PenguinEmote.Alert, 0.9);
                break;
        }
    }

    private bool TryEscapeGrabByPersonality(Penguin penguin, double dtSec)
    {
        bool resistsGrab = penguin.Personality.Temperament == PenguinTemperament.Grumpy || penguin.HoldsGrudge;
        if (!resistsGrab)
        {
            return false;
        }

        penguin.AdjustMood((float)(-10.0 * dtSec));
        if (penguin.Emote != PenguinEmote.Angry)
        {
            penguin.TriggerEmote(PenguinEmote.Angry, 0.9);
        }

        double escapeAfter = penguin.Personality.Temperament == PenguinTemperament.Grumpy ? 1.05 : 1.45;
        if (penguin.GrabHeldSeconds < escapeAfter)
        {
            return false;
        }

        double kickVx = (_rng.Next(2) == 0 ? -1 : 1) * 4.5 * penguin.Scale;
        penguin.ReleaseGrab(kickVx, -4.4 * penguin.Scale);
        penguin.HuffySecondsRemaining = 2.0;
        penguin.TriggerSquash(0.80f, 1.26f);
        penguin.TriggerEmote(PenguinEmote.Angry, 1.8);
        SoundRequested?.Invoke(SoundEffectKind.Eep);
        return true;
    }

    private void ApplyClickPersonality(Penguin penguin)
    {
        double s = penguin.Scale;
        double hopVy;
        if (penguin.SubType == PenguinSubType.Baby)
        {
            hopVy = -6.2 * s;
        }
        else if (!_enablePersonalities)
        {
            hopVy = -4.5 * s;
        }
        else if (penguin.Personality.Temperament == PenguinTemperament.Dramatic ||
                 penguin.Personality.Quirk == PenguinQuirk.Acrobat)
        {
            hopVy = -5.6 * s;
        }
        else if (penguin.Personality.Temperament == PenguinTemperament.Cuddly)
        {
            hopVy = -5.0 * s;
        }
        else if (penguin.Personality.Temperament == PenguinTemperament.Chill)
        {
            hopVy = -3.1 * s;
        }
        else if (penguin.Personality.Temperament == PenguinTemperament.Grumpy || penguin.HoldsGrudge)
        {
            hopVy = -3.6 * s;
        }
        else
        {
            hopVy = -4.5 * s;
        }

        double hopVx = _enablePersonalities &&
            (penguin.Personality.Temperament is PenguinTemperament.Grumpy or PenguinTemperament.Shy || penguin.HoldsGrudge)
            ? (_rng.Next(2) == 0 ? -2.2 : 2.2) * s
            : 0;

        penguin.ReleaseGrab(hopVx, hopVy);

        if (!_enablePersonalities)
        {
            penguin.TriggerSquash(0.82f, 1.24f);
            return;
        }

        if (penguin.Personality.Temperament == PenguinTemperament.Grumpy || penguin.HoldsGrudge)
        {
            penguin.AdjustMood(-10f);
            penguin.HuffySecondsRemaining = 1.6;
            penguin.TriggerSquash(1.22f, 0.80f);
            penguin.TriggerEmote(PenguinEmote.Angry, 1.5);
            return;
        }

        switch (penguin.Personality.Temperament)
        {
            case PenguinTemperament.Cuddly:
                penguin.AdjustMood(8f);
                penguin.TriggerSquash(0.82f, 1.24f);
                penguin.TriggerEmote(PenguinEmote.Heart, 1.5);
                break;

            case PenguinTemperament.Shy:
                penguin.AdjustMood(-6f);
                penguin.TriggerSquash(0.84f, 1.22f);
                penguin.TriggerEmote(PenguinEmote.Alert, 1.2);
                break;

            case PenguinTemperament.ThrillSeeker:
            case PenguinTemperament.Dramatic:
            case PenguinTemperament.Mischievous:
                penguin.AdjustMood(6f);
                penguin.TriggerSquash(0.78f, 1.28f);
                penguin.TriggerEmote(PenguinEmote.Sparkle, 1.4);
                break;

            case PenguinTemperament.Curious:
                penguin.AdjustMood(4f);
                penguin.TriggerSquash(0.84f, 1.20f);
                penguin.TriggerEmote(PenguinEmote.Curious, 1.2);
                break;

            case PenguinTemperament.Chill:
                penguin.AdjustMood(2f);
                penguin.TriggerSquash(0.92f, 1.08f);
                penguin.TriggerEmote(PenguinEmote.Sleep, 1.0);
                break;

            default:
                penguin.TriggerSquash(0.82f, 1.24f);
                break;
        }
    }

    private void ApplyThrowPersonality(Penguin penguin, double vx, double vy)
    {
        if (!_enablePersonalities)
        {
            return;
        }

        double throwSpeed = Math.Sqrt((vx * vx) + (vy * vy)) / Math.Max(0.5, penguin.Scale);
        if (throwSpeed >= 8.0)
        {
            if (penguin.Personality.Temperament is PenguinTemperament.ThrillSeeker or PenguinTemperament.Dramatic or PenguinTemperament.Mischievous ||
                penguin.Personality.Quirk == PenguinQuirk.Acrobat)
            {
                penguin.AdjustMood(14f);
                penguin.TriggerEmote(PenguinEmote.Sparkle, 1.8);
                SoundRequested?.Invoke(SoundEffectKind.Chirp);
            }
            else if (penguin.Personality.Temperament is PenguinTemperament.Grumpy or PenguinTemperament.Shy)
            {
                penguin.AdjustMood(-22f);
                penguin.HuffySecondsRemaining = 2.5;
                penguin.TriggerEmote(
                    penguin.Personality.Temperament == PenguinTemperament.Grumpy ? PenguinEmote.Angry : PenguinEmote.Alert,
                    1.9);
            }
            else
            {
                penguin.AdjustMood(-8f);
                penguin.TriggerEmote(PenguinEmote.Alert, 1.2);
            }
        }
        else if (throwSpeed < 5.0)
        {
            if (penguin.Personality.Temperament is PenguinTemperament.Cuddly or PenguinTemperament.Shy)
            {
                penguin.AdjustMood(5f);
                penguin.TriggerEmote(PenguinEmote.Heart, 1.3);
            }
            else if (penguin.Personality.Temperament == PenguinTemperament.Grumpy || penguin.HoldsGrudge)
            {
                penguin.AdjustMood(-6f);
                penguin.HuffySecondsRemaining = 1.6;
                penguin.TriggerEmote(PenguinEmote.Angry, 1.4);
            }
        }
    }

    private void TryHandleCursorPetting(
        Penguin penguin,
        Point cursor,
        double cursorStepDist,
        double cursorSpeedPxPerSec,
        double dtSec,
        AppSettings settings)
    {
        if (penguin.Animation is not (PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker))
        {
            return;
        }

        Rectangle touchBox = Rectangle.Inflate(penguin.Bounds, 6, 6);
        if (!touchBox.Contains(cursor))
        {
            if (penguin.PetAccumulatedDistance > 0)
            {
                penguin.PetAccumulatedDistance = Math.Max(0, penguin.PetAccumulatedDistance - (40.0 * dtSec));
            }
            return;
        }

        if (cursorSpeedPxPerSec is > 18.0 and < 950.0)
        {
            penguin.PetAccumulatedDistance += cursorStepDist;
        }

        if (penguin.PetCooldown > 0 || penguin.PetAccumulatedDistance < 20.0 * penguin.Scale)
        {
            return;
        }

        penguin.PetAccumulatedDistance = 0;
        penguin.PetCooldown = 1.35;

        bool rejectsTouch = (penguin.Personality.Temperament == PenguinTemperament.Grumpy && penguin.Mood < 35f) ||
            penguin.HoldsGrudge;
        bool startledShy = penguin.Personality.Temperament == PenguinTemperament.Shy && cursorSpeedPxPerSec > 240.0;

        if (rejectsTouch || startledShy)
        {
            penguin.IdleSecondsRemaining = 0;
            penguin.AdjustMood(rejectsTouch ? -10f : -7f);
            penguin.TriggerEmote(rejectsTouch ? PenguinEmote.Angry : PenguinEmote.Alert, 1.6);
            penguin.TriggerSquash(rejectsTouch ? 1.22f : 0.84f, rejectsTouch ? 0.80f : 1.22f);
            penguin.HuffySecondsRemaining = rejectsTouch ? 1.8 : 1.1;

            int cx = penguin.X + (penguin.Width / 2);
            FacingDirection away = cx < cursor.X ? FacingDirection.Left : FacingDirection.Right;
            CollisionDirection awayDir = away == FacingDirection.Right ? CollisionDirection.Right : CollisionDirection.Left;
            if (!World.IsBlocked(penguin.Bounds, awayDir))
            {
                double walkSpeed = 3.2 * penguin.Scale * penguin.SpeedMultiplier;
                penguin.SetAnimation(penguin.Animation, away);
                penguin.SetVelocity(away == FacingDirection.Right ? walkSpeed * 1.35 : -walkSpeed * 1.35, 0);
            }

            SoundRequested?.Invoke(SoundEffectKind.Eep);
            return;
        }

        bool cuddly = penguin.Personality.Temperament == PenguinTemperament.Cuddly;
        penguin.AdjustMood(cuddly ? 14f : 10f);
        penguin.TriggerEmote(cuddly || penguin.Mood >= 25f ? PenguinEmote.Heart : PenguinEmote.Sparkle, 1.6);
        penguin.TriggerSquash(1.18f, 0.84f);
        if (settings.EnableIdlePauses)
        {
            penguin.IdleSecondsRemaining = Math.Max(penguin.IdleSecondsRemaining, cuddly ? 0.55 : 0.25);
            penguin.ResetFrameToStanding();
        }
        SoundRequested?.Invoke(SoundEffectKind.Chirp);
    }

    private void ApplyPersonalityCursorProximity(Penguin p, Point cursor, double cursorSpeedPxPerSec)
    {
        if (p.SubType == PenguinSubType.King ||
            p.IsIdle ||
            p.IsSeekingFish ||
            p.Animation is not (PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker))
        {
            return;
        }

        int cx = p.X + (p.Width / 2);
        int cy = p.Y + (p.Height / 2);
        int dx = cx - cursor.X;
        int dy = cy - cursor.Y;
        double s = p.Scale;
        double walkSpeed = 3.2 * s * p.SpeedMultiplier;

        bool wantsEscape = (p.Personality.Temperament == PenguinTemperament.Shy && cursorSpeedPxPerSec > 260.0) || p.HoldsGrudge;
        if (wantsEscape)
        {
            int escapeRadius = ScaleSize(72, s);
            if ((dx * dx) + (dy * dy) <= escapeRadius * escapeRadius)
            {
                FacingDirection away = dx < 0 ? FacingDirection.Left : FacingDirection.Right;
                CollisionDirection awayDir = away == FacingDirection.Right ? CollisionDirection.Right : CollisionDirection.Left;
                if (!World.IsBlocked(p.Bounds, awayDir))
                {
                    if (p.Direction != away)
                    {
                        p.SetAnimation(p.Animation, away);
                    }
                    p.SetVelocity(away == FacingDirection.Right ? walkSpeed * 1.35 : -walkSpeed * 1.35, 0);
                    p.HuffySecondsRemaining = Math.Max(p.HuffySecondsRemaining, 0.9);
                }

                if (p.PersonalityReactCooldown <= 0)
                {
                    p.PersonalityReactCooldown = 2.0;
                    p.TriggerEmote(p.HoldsGrudge ? PenguinEmote.Angry : PenguinEmote.Alert, 1.3);
                }
            }
            return;
        }

        if (p.Personality.Temperament is PenguinTemperament.Curious or PenguinTemperament.Cuddly &&
            cursorSpeedPxPerSec < 280.0 &&
            Math.Abs(dx) > ScaleSize(22, s) &&
            Math.Abs(dx) <= ScaleSize(120, s) &&
            Math.Abs(dy) <= ScaleSize(55, s))
        {
            FacingDirection toward = cursor.X > cx ? FacingDirection.Right : FacingDirection.Left;
            CollisionDirection towardDir = toward == FacingDirection.Right ? CollisionDirection.Right : CollisionDirection.Left;
            if (!World.IsBlocked(p.Bounds, towardDir) && !World.HasHorizontalBarrierBetween(p.Bounds, cursor.X))
            {
                if (p.Direction != toward)
                {
                    p.SetAnimation(p.Animation, toward);
                    p.SetVelocity(toward == FacingDirection.Right ? walkSpeed : -walkSpeed, 0);
                }

                if (p.PersonalityReactCooldown <= 0)
                {
                    p.PersonalityReactCooldown = 3.2;
                    p.TriggerEmote(
                        p.Personality.Temperament == PenguinTemperament.Curious ? PenguinEmote.Curious : PenguinEmote.Heart,
                        1.3);
                }
            }
        }
    }

    private static int ScaleSize(int baseSize, double scale) =>
        Math.Max(1, (int)Math.Round(baseSize * scale));

    private static int ScaleStep(int baseStep, double scale) =>
        baseStep == 0 ? 0 : Math.Max(1, (int)Math.Round(baseStep * scale));
}
