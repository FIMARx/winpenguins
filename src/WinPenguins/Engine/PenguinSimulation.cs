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

    public CollisionWorld World { get; }

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
        Step(settings, settings.FrameDelayMs, cursorScreenPos);

    public void Step(AppSettings settings, double deltaMs, Point? cursorScreenPos = null)
    {
        _enableParachutes = settings.EnableParachutes;
        double stepUnits = Math.Clamp(deltaMs / Math.Max(1.0, settings.FrameDelayMs), 0.001, 2.0);
        bool autoPerMonitorDpi = settings.ScaleMode == SpriteScaleMode.Auto;

        for (int i = 0; i < _penguins.Count; i++)
        {
            Penguin penguin = _penguins[i];
            if (!penguin.Active)
            {
                continue;
            }

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
                    FacingDirection dir = p.PreferredDirection ?? RandomDirection();
                    EnsureSubType(p, settings.SantaPercent);
                    p.SetAnimation(
                        p.SubType == PenguinSubType.Santa
                            ? PenguinAnimationId.SantaWalker
                            : PenguinAnimationId.Walker,
                        dir);
                    p.SetVelocity(dir == FacingDirection.Right ? walkSpeed : -walkSpeed, 0);
                    p.PreferredDirection = null;

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

    private static int ScaleSize(int baseSize, double scale) =>
        Math.Max(1, (int)Math.Round(baseSize * scale));

    private static int ScaleStep(int baseStep, double scale) =>
        baseStep == 0 ? 0 : Math.Max(1, (int)Math.Round(baseStep * scale));
}
