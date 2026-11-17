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
    private Point? _lastCursorPos;

    public CollisionWorld World { get; }
    public ToySystem Toys { get; } = new();
    public SnowSystem Snow { get; } = new();

    public IReadOnlyList<Penguin> Penguins => _penguins;
    public double Scale => _scale;

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

    public void BeginGrab(Penguin penguin)
    {
        penguin.IsPerching = false;
        penguin.SlideSecondsRemaining = 0;
        penguin.BeginGrab();
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
            // Cute hop + quack on single click.
            penguin.ReleaseGrab(0, -4.5 * penguin.Scale);
            penguin.TriggerSquash(0.82f, 1.24f);
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
    }

    public void Step(AppSettings settings, Point? cursorScreenPos = null) =>
        Step(settings, settings.FrameDelayMs, cursorScreenPos, 0f);

    public void Step(AppSettings settings, double deltaMs, Point? cursorScreenPos = null, float audioPeak = 0f)
    {
        _enableParachutes = settings.EnableParachutes;
        double stepUnits = Math.Clamp(deltaMs / Math.Max(1.0, settings.FrameDelayMs), 0.001, 2.0);
        double dtSec = Math.Max(0.0, deltaMs / 1000.0);
        bool autoPerMonitorDpi = settings.ScaleMode == SpriteScaleMode.Auto;

        Toys.Step(World, deltaMs);
        Snow.Step(World, _penguins, deltaMs, settings.EnableSnow);

        double cursorSpeedPxPerSec = 0;
        if (cursorScreenPos.HasValue && _lastCursorPos.HasValue && dtSec > 0.0005)
        {
            double cdx = cursorScreenPos.Value.X - _lastCursorPos.Value.X;
            double cdy = cursorScreenPos.Value.Y - _lastCursorPos.Value.Y;
            cursorSpeedPxPerSec = Math.Sqrt(cdx * cdx + cdy * cdy) / dtSec;
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
            else if (settings.CursorPerch &&
                settings.InteractiveMouse &&
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
                    penguin.PerchDwellSeconds += dtSec;
                    if (penguin.PerchDwellSeconds >= 1.25)
                    {
                        penguin.PerchDwellSeconds = 0;
                        penguin.IsPerching = true;
                        penguin.IdleSecondsRemaining = 0;
                        penguin.SlideSecondsRemaining = 0;
                        penguin.TriggerSquash(0.84f, 1.20f);
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

            if (settings.EnableMusicDancing &&
                audioPeak > 0.28f &&
                !penguin.StartingUp &&
                penguin.BeatCooldown <= 0 &&
                penguin.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker)
            {
                penguin.BeatCooldown = 0.28;
                if (audioPeak > 0.70f && _rng.Next(3) == 0 && !penguin.IsIdle)
                {
                    penguin.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
                    penguin.SetVelocity(penguin.VelocityX * 0.4, -4.2 * penguin.Scale);
                    penguin.TriggerSquash(0.84f, 1.22f);
                }
                else
                {
                    penguin.TriggerSquash(1.14f, 0.86f);
                }
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
        double climbSpeed = 3.5 * s * p.SpeedMultiplier;
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
                    EnsureSubType(p, settings.SantaPercent);
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

                    EnsureSubType(p, settings.SantaPercent);
                    p.SetAnimation(
                        p.SubType == PenguinSubType.Santa
                            ? PenguinAnimationId.SantaClimber
                            : PenguinAnimationId.Climber,
                        wallSide);
                    p.SetVelocity(0, -climbSpeed);
                }
                break;

            case PenguinAnimationId.Tumbler:
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

                    EnsureSubType(p, settings.SantaPercent);
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

                    if (p.Y - p.TumbleStartY > splatThreshold)
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
                            EnsureSubType(p, settings.SantaPercent);
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
                            EnsureSubType(p, settings.SantaPercent);
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
                else if (settings.EnableIdlePauses && _rng.NextDouble() < 0.0045 * stepUnits)
                {
                    p.IdleSecondsRemaining = 0.45 + _rng.NextDouble() * 0.9;
                    p.ResetFrameToStanding();
                    if (_rng.Next(8) == 0)
                    {
                        SoundRequested?.Invoke(SoundEffectKind.Chirp);
                    }
                }
                else if (settings.EnableIdlePauses && !p.IsSliding && _rng.NextDouble() < 0.0010 * stepUnits)
                {
                    p.SlideSecondsRemaining = 0.55 + _rng.NextDouble() * 0.45;
                    double slideSpeed = 5.4 * s * p.SpeedMultiplier;
                    p.SetVelocity(p.Direction == FacingDirection.Right ? slideSpeed : -slideSpeed, 0);
                }
                break;

            case PenguinAnimationId.Climber:
            case PenguinAnimationId.SantaClimber:
                CollisionDirection climbWall = p.Direction == FacingDirection.Left
                    ? CollisionDirection.Left
                    : CollisionDirection.Right;

                if (!World.IsBlocked(p.Bounds, climbWall))
                {
                    EnsureSubType(p, settings.SantaPercent);
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
                        EnsureSubType(p, settings.SantaPercent);
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
                    EnsureSubType(p, settings.SantaPercent);
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
        if (p.Animation is not (PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker))
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
        double speedMult = 0.85 + (_rng.NextDouble() * 0.33);

        if (_enableParachutes && _rng.Next(5) == 0)
        {
            double sway = (_rng.Next(3) - 1) * 0.6 * effectiveScale;
            var para = new Penguin(spawnX, spawnY, sway, 1.6 * effectiveScale, effectiveScale, speedMult, _specs)
            {
                IsParachuting = true
            };
            para.SetAnimation(PenguinAnimationId.Floater, FacingDirection.Left);
            return para;
        }

        double driftX = (_rng.Next(3) - 1) * effectiveScale;
        double fallY = 3.0 * effectiveScale;
        return new Penguin(spawnX, spawnY, driftX, fallY, effectiveScale, speedMult, _specs);
    }

    private void EnsureSubType(Penguin p, int santaPercent)
    {
        if (p.SubType == PenguinSubType.Unspecified)
        {
            p.SubType = _rng.Next(100) < santaPercent
                ? PenguinSubType.Santa
                : PenguinSubType.Normal;
        }
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

            EnsureSubType(p, settings.SantaPercent);
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
        if (Toys.Toys.Count == 0 || p.IsTerminalAnimation)
        {
            return;
        }

        double s = p.Scale;
        double walkSpeed = 3.2 * s * p.SpeedMultiplier;
        Rectangle pBounds = p.Bounds;
        int cx = p.X + p.Width / 2;
        int cy = p.Y + p.Height / 2;

        // Seek nearby fish treats on flat surfaces.
        if (p.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker && !p.IsIdle)
        {
            DesktopToy? fish = Toys.FindNearestFish(cx, cy, 340 * s);
            if (fish != null)
            {
                int fishCx = (int)Math.Round(fish.X + fish.Width * 0.5);
                if (Math.Abs(fishCx - cx) > 6)
                {
                    FacingDirection want = fishCx > cx ? FacingDirection.Right : FacingDirection.Left;
                    if (p.Direction != want)
                    {
                        p.SetAnimation(p.Animation, want);
                    }
                    double sprint = p.IsSliding ? Math.Abs(p.VelocityX) : walkSpeed * 1.35;
                    p.SetVelocity(want == FacingDirection.Right ? sprint : -sprint, 0);
                }
            }
        }

        for (int i = 0; i < Toys.Toys.Count; i++)
        {
            DesktopToy toy = Toys.Toys[i];
            if (!toy.Active || !pBounds.IntersectsWith(toy.Bounds))
            {
                continue;
            }

            switch (toy.Kind)
            {
                case ToyKind.Fish:
                    toy.Active = false;
                    p.IdleSecondsRemaining = 0;
                    p.TriggerSquash(0.84f, 1.24f);
                    SoundRequested?.Invoke(SoundEffectKind.Chirp);
                    return;

                case ToyKind.Trampoline:
                    if (p.BounceCooldown <= 0 && !p.StartingUp)
                    {
                        p.IsParachuting = false;
                        p.IdleSecondsRemaining = 0;
                        p.SlideSecondsRemaining = 0;
                        p.BounceCooldown = 0.45;
                        toy.Pulse = 1.0;
                        double vx = p.Direction == FacingDirection.Right ? 2.4 * s : -2.4 * s;
                        p.SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
                        p.SetVelocity(vx, -11.5 * s);
                        p.TumbleStartY = p.Y;
                        p.TriggerSquash(0.78f, 1.28f);
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
                            SoundRequested?.Invoke(SoundEffectKind.Eep);
                            return;
                        }
                    }
                    break;
            }
        }
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
                    continue;
                }

                // Check bouncy trampoline landing onto another penguin or parachute umbrella.
                if (TryBouncePair(a, ra, b, rb) || TryBouncePair(b, rb, a, ra))
                {
                    continue;
                }

                // Opposite-facing walkers briefly greet with a cheerful hop/chirp.
                if (a.GreetingCooldown <= 0 &&
                    b.GreetingCooldown <= 0 &&
                    a.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker &&
                    b.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker &&
                    a.Direction != b.Direction &&
                    Math.Abs((ra.X + ra.Width / 2) - (rb.X + rb.Width / 2)) < Math.Min(ra.Width, rb.Width) * 0.55)
                {
                    a.GreetingCooldown = 4.5;
                    b.GreetingCooldown = 4.5;
                    a.TriggerSquash(0.88f, 1.15f);
                    b.TriggerSquash(0.88f, 1.15f);
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

    private static int ScaleSize(int baseSize, double scale) =>
        Math.Max(1, (int)Math.Round(baseSize * scale));

    private static int ScaleStep(int baseStep, double scale) =>
        baseStep == 0 ? 0 : Math.Max(1, (int)Math.Round(baseStep * scale));
}
