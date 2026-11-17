using System.Drawing;

namespace WinPenguins.Engine;

public sealed class Penguin
{
    private double _accumX;
    private double _accumY;
    private double _frameAccum;
    private IReadOnlyDictionary<PenguinAnimationId, AnimationSpec> _specs;

    public int X { get; set; }
    public int Y { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }

    public double Scale { get; private set; }
    public double SpeedMultiplier { get; set; }
    public double IdleSecondsRemaining { get; set; }
    public bool IsParachuting { get; set; }
    public bool IsGrabbed { get; private set; }
    public bool IsPerching { get; set; }
    public double CornerTrapSeconds { get; set; }
    public double BounceCooldown { get; set; }
    public double GreetingCooldown { get; set; }
    public double PortalCooldown { get; set; }
    public double BeatCooldown { get; set; }
    public double SlideSecondsRemaining { get; set; }
    public double PerchDwellSeconds { get; set; }
    public bool IsSliding => SlideSecondsRemaining > 0;

    // 240Hz smooth visual transform state (does not mutate collision Bounds).
    public float VisualRotationDeg { get; set; }
    public float VisualScaleX { get; set; } = 1.0f;
    public float VisualScaleY { get; set; } = 1.0f;
    public int ShadowDistance { get; set; } = 999;

    public double VisualX => X + _accumX;
    public double VisualY => Y + _accumY;

    public void TriggerSquash(float scaleX, float scaleY)
    {
        VisualScaleX = Math.Clamp(scaleX, 0.72f, 1.35f);
        VisualScaleY = Math.Clamp(scaleY, 0.72f, 1.35f);
    }

    public void RelaxVisualTransforms(double deltaMs)
    {
        double dt = Math.Max(0.0, deltaMs / 1000.0);
        float blend = (float)(1.0 - Math.Exp(-14.0 * dt));
        VisualScaleX += (1.0f - VisualScaleX) * blend;
        VisualScaleY += (1.0f - VisualScaleY) * blend;

        if (Animation == PenguinAnimationId.Tumbler && !IsGrabbed)
        {
            float spinRate = Math.Clamp((float)Math.Abs(VelocityX) * 42f, 220f, 720f);
            float dirSign = VelocityX >= 0 ? 1f : -1f;
            VisualRotationDeg = (VisualRotationDeg + dirSign * spinRate * (float)dt) % 360f;
        }
        else if (IsSliding)
        {
            float targetLean = Direction == FacingDirection.Right ? 68f : -68f;
            VisualRotationDeg += (targetLean - VisualRotationDeg) * Math.Clamp(blend * 1.4f, 0f, 1f);
        }
        else if (Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker && !IsIdle)
        {
            float targetTilt = Math.Clamp((float)(VelocityX / Math.Max(1.0, Scale)) * 1.2f, -5.5f, 5.5f);
            VisualRotationDeg += (targetTilt - VisualRotationDeg) * blend;
        }
        else if (Animation == PenguinAnimationId.Floater)
        {
            float targetSway = Math.Clamp((float)(VelocityX / Math.Max(1.0, Scale)) * 5.0f, -14f, 14f);
            VisualRotationDeg += (targetSway - VisualRotationDeg) * blend;
        }
        else
        {
            VisualRotationDeg += (0f - VisualRotationDeg) * blend;
        }

        if (BounceCooldown > 0) BounceCooldown = Math.Max(0, BounceCooldown - dt);
        if (GreetingCooldown > 0) GreetingCooldown = Math.Max(0, GreetingCooldown - dt);
        if (PortalCooldown > 0) PortalCooldown = Math.Max(0, PortalCooldown - dt);
        if (BeatCooldown > 0) BeatCooldown = Math.Max(0, BeatCooldown - dt);
    }

    public FacingDirection? PreferredDirection { get; set; }
    public int TumbleStartY { get; set; }

    public PenguinAnimationId Animation { get; private set; }
    public int FrameIndex { get; private set; }
    public FacingDirection Direction { get; private set; }
    public PenguinSubType SubType { get; set; } = PenguinSubType.Unspecified;

    public bool Active { get; private set; } = true;
    public bool StartingUp { get; set; } = true;

    public Penguin(
        int x,
        int y,
        double velocityX,
        double velocityY,
        double scale,
        double speedMultiplier = 1.0,
        IReadOnlyDictionary<PenguinAnimationId, AnimationSpec>? specs = null)
    {
        _specs = specs ?? AnimationCatalogSpecs.All;
        Scale = Math.Clamp(scale, 0.5, 4.0);
        SpeedMultiplier = Math.Clamp(speedMultiplier, 0.7, 1.4);
        X = x;
        Y = y;
        VelocityX = velocityX;
        VelocityY = velocityY;
        SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
    }

    public int Width => ScaleDimension(_specs[Animation].BaseWidth);
    public int Height => ScaleDimension(_specs[Animation].BaseHeight);

    public Rectangle Bounds => new(X, Y, Width, Height);

    public bool IsIdle => IdleSecondsRemaining > 0;

    public bool IsTerminalAnimation =>
        Animation is PenguinAnimationId.Bomber or PenguinAnimationId.Explosion;

    public void UpdateSpecs(IReadOnlyDictionary<PenguinAnimationId, AnimationSpec> specs)
    {
        _specs = specs;
        FrameIndex = Math.Clamp(FrameIndex, 0, Math.Max(0, _specs[Animation].FrameCount - 1));
    }

    public void SetScale(double newScale)
    {
        newScale = Math.Clamp(newScale, 0.5, 4.0);
        if (Math.Abs(newScale - Scale) < 0.001)
        {
            return;
        }

        int oldW = Width;
        int oldH = Height;
        double ratio = newScale / Scale;
        Scale = newScale;
        int newW = Width;
        int newH = Height;

        // Keep feet anchored at the bottom-center while resizing across mixed-DPI monitors.
        X -= (newW - oldW) / 2;
        Y -= newH - oldH;
        VelocityX *= ratio;
        VelocityY *= ratio;
    }

    public bool HitTest(int screenX, int screenY) =>
        Active && !IsTerminalAnimation && Bounds.Contains(screenX, screenY);

    public void BeginGrab()
    {
        IsGrabbed = true;
        IsParachuting = false;
        IdleSecondsRemaining = 0;
        StartingUp = false;
        _accumX = 0;
        _accumY = 0;
        SetAnimation(PenguinAnimationId.Tumbler, FacingDirection.Left);
        SetVelocity(0, 0);
    }

    public void MoveGrab(int newX, int newY)
    {
        X = newX;
        Y = newY;
        _accumX = 0;
        _accumY = 0;
    }

    public void ReleaseGrab(double velocityX, double velocityY)
    {
        IsGrabbed = false;
        IsParachuting = false;
        IdleSecondsRemaining = 0;
        StartingUp = false;

        if (Math.Abs(velocityX) > 6.5 * Scale || velocityY < -1.5 * Scale)
        {
            SetAnimation(PenguinAnimationId.Tumbler, FacingDirection.Left);
            TumbleStartY = Y;
        }
        else
        {
            SetAnimation(PenguinAnimationId.Faller, FacingDirection.Left);
        }

        SetVelocity(velocityX, velocityY);
    }

    public void SetAnimation(PenguinAnimationId animation, FacingDirection direction)
    {
        if (Animation != animation)
        {
            FrameIndex = 0;
            _frameAccum = 0;
        }

        Animation = animation;
        Direction = _specs[animation].DirectionCount > 1
            ? direction
            : FacingDirection.Left;
    }

    public void SetVelocity(double vx, double vy)
    {
        VelocityX = vx;
        VelocityY = vy;
        _accumX = 0;
        _accumY = 0;
    }

    public void ResetFrameToStanding()
    {
        FrameIndex = 0;
        _frameAccum = 0;
    }

    public void TriggerBomber()
    {
        if (Animation == PenguinAnimationId.Bomber || Animation == PenguinAnimationId.Explosion)
        {
            return;
        }

        IsGrabbed = false;
        IsParachuting = false;
        IdleSecondsRemaining = 0;
        Animation = PenguinAnimationId.Bomber;
        Direction = FacingDirection.Left;
        FrameIndex = 0;
        VelocityX = 0;
        VelocityY = 0;
        _accumX = 0;
        _accumY = 0;
        _frameAccum = 0;
        Active = true;
    }

    public bool TriggerExplosion()
    {
        if (Animation == PenguinAnimationId.Explosion)
        {
            return false;
        }

        IsGrabbed = false;
        IsParachuting = false;
        IdleSecondsRemaining = 0;

        int oldW = Width;
        int oldH = Height;

        Animation = PenguinAnimationId.Explosion;
        Direction = FacingDirection.Left;
        FrameIndex = 0;
        VelocityX = 0;
        VelocityY = 0;
        _accumX = 0;
        _accumY = 0;
        _frameAccum = 0;
        Active = true;

        X -= (Width - oldW) / 2;
        Y -= (Height - oldH) / 2;
        return true;
    }

    public (int StepX, int StepY, bool AdvanceFrameNow) AccumulateSubStep(double stepUnits)
    {
        if (IsIdle)
        {
            return (0, 0, false);
        }

        _accumX += VelocityX * stepUnits;
        _accumY += VelocityY * stepUnits;

        int stepX = (int)Math.Truncate(_accumX);
        int stepY = (int)Math.Truncate(_accumY);
        _accumX -= stepX;
        _accumY -= stepY;

        _frameAccum += stepUnits * (Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker
            ? SpeedMultiplier
            : 1.0);
        bool advanceFrame = _frameAccum >= 1.0;
        if (advanceFrame)
        {
            _frameAccum -= Math.Truncate(_frameAccum);
        }

        return (stepX, stepY, advanceFrame);
    }

    public MoveOutcome Advance(CollisionWorld world, bool force, out bool explodedByBounds) =>
        Advance(world, (int)Math.Round(VelocityX), (int)Math.Round(VelocityY), advanceFrame: true, force, out explodedByBounds);

    public MoveOutcome Advance(
        CollisionWorld world,
        int stepX,
        int stepY,
        bool advanceFrame,
        bool force,
        out bool explodedByBounds)
    {
        explodedByBounds = false;

        int newX = X + stepX;
        int newY = Y + stepY;
        int w = Width;
        int h = Height;
        MoveOutcome outcome = MoveOutcome.Moved;
        bool moveAhead = true;

        if (stepX == 0 && stepY == 0)
        {
            if (advanceFrame && (IsTerminalAnimation || VelocityX != 0 || VelocityY != 0))
            {
                AdvanceFrame();
            }

            return MoveOutcome.Moved;
        }

        Rectangle target = new(newX, newY, w, h);

        bool blocked = !force && !world.IsPositionValid(target, ignoreWindows: false);

        if (blocked)
        {
            moveAhead = false;
            outcome = MoveOutcome.Blocked;
            _accumX = 0;
            _accumY = 0;

            if (Math.Abs(stepY) < Math.Abs(stepX))
            {
                int step = newX > X ? -1 : 1;
                for (int tryX = newX + step; tryX != X; tryX += step)
                {
                    int tryY = Y + ((tryX - X) * stepY) / stepX;
                    Rectangle candidate = new(tryX, tryY, w, h);
                    if (world.IsPositionValid(candidate, ignoreWindows: false))
                    {
                        newX = tryX;
                        newY = tryY;
                        outcome = MoveOutcome.PartialMove;
                        moveAhead = true;
                        break;
                    }
                }
            }
            else
            {
                int step = newY > Y ? -1 : 1;
                for (int tryY = newY + step; tryY != Y; tryY += step)
                {
                    int tryX = X + ((tryY - Y) * stepX) / stepY;
                    Rectangle candidate = new(tryX, tryY, w, h);
                    if (world.IsPositionValid(candidate, ignoreWindows: false))
                    {
                        newX = tryX;
                        newY = tryY;
                        outcome = MoveOutcome.PartialMove;
                        moveAhead = true;
                        break;
                    }
                }
            }
        }

        if (moveAhead)
        {
            if (force && !world.IsInsideDesktop(new Rectangle(newX, Math.Max(world.VirtualBounds.Top, newY), w, h)))
            {
                newX = world.ClampHorizontalToDesktop(newX, Math.Max(world.VirtualBounds.Top, newY), w, h);
                if (newY < world.VirtualBounds.Top)
                {
                    newY = world.VirtualBounds.Top;
                }
            }

            X = newX;
            Y = newY;
        }

        if (advanceFrame && (moveAhead || IsTerminalAnimation))
        {
            AdvanceFrame();
        }

        if (!IsTerminalAnimation && world.IsPastBottomOfAnyCoveringMonitor(Bounds))
        {
            explodedByBounds = TriggerExplosion();
        }

        return outcome;
    }

    private void AdvanceFrame()
    {
        AnimationSpec spec = _specs[Animation];
        FrameIndex++;
        if (FrameIndex >= spec.FrameCount)
        {
            if (spec.Cycles)
            {
                FrameIndex = 0;
            }
            else
            {
                Active = false;
                FrameIndex = spec.FrameCount - 1;
            }
        }
    }

    private int ScaleDimension(int baseSize) => Math.Max(1, (int)Math.Round(baseSize * Scale));
}
