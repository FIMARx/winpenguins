using System.Drawing;

namespace WinPenguins.Engine;

public enum ToyKind
{
    Fish,
    Trampoline,
    PortalA,
    PortalB
}

public sealed class DesktopToy
{
    public int Id { get; }
    public ToyKind Kind { get; }
    public double X { get; set; }
    public double Y { get; set; }
    public double VelocityY { get; set; }
    public double Scale { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Pulse { get; set; }
    public double AgeSeconds { get; set; }
    public bool Active { get; set; } = true;

    public DesktopToy(int id, ToyKind kind, double x, double y, double scale)
    {
        Id = id;
        Kind = kind;
        X = x;
        Y = y;
        Scale = Math.Clamp(scale, 0.5, 4.0);
        (Width, Height) = GetDimensions(kind, Scale);
    }

    public Rectangle Bounds => new((int)Math.Round(X), (int)Math.Round(Y), Width, Height);

    public static (int W, int H) GetDimensions(ToyKind kind, double scale) => kind switch
    {
        ToyKind.Fish => (Math.Max(12, (int)Math.Round(24 * scale)), Math.Max(8, (int)Math.Round(14 * scale))),
        ToyKind.Trampoline => (Math.Max(20, (int)Math.Round(44 * scale)), Math.Max(8, (int)Math.Round(14 * scale))),
        ToyKind.PortalA or ToyKind.PortalB => (Math.Max(16, (int)Math.Round(30 * scale)), Math.Max(22, (int)Math.Round(42 * scale))),
        _ => (24, 24)
    };
}

public sealed class ToySystem
{
    private const int MaxToys = 16;
    private readonly List<DesktopToy> _toys = [];
    private int _nextId = 1;

    public IReadOnlyList<DesktopToy> Toys => _toys;

    public DesktopToy SpawnToy(ToyKind kind, int x, int y, double scale)
    {
        if (kind is ToyKind.PortalA or ToyKind.PortalB)
        {
            _toys.RemoveAll(t => t.Kind == kind);
        }
        else if (_toys.Count >= MaxToys)
        {
            for (int i = 0; i < _toys.Count; i++)
            {
                if (_toys[i].Kind is not (ToyKind.PortalA or ToyKind.PortalB))
                {
                    _toys.RemoveAt(i);
                    break;
                }
            }
        }

        var toy = new DesktopToy(_nextId++, kind, x, y, scale);
        _toys.Add(toy);
        return toy;
    }

    public void SpawnTwinPortals(CollisionWorld world, Random rng)
    {
        if (world.Monitors.Count >= 2)
        {
            Rectangle m0 = world.Monitors[0];
            Rectangle m1 = world.Monitors[1];
            double s0 = world.MonitorScales[0];
            double s1 = world.MonitorScales[1];
            (int w0, int h0) = DesktopToy.GetDimensions(ToyKind.PortalA, s0);
            (int w1, int h1) = DesktopToy.GetDimensions(ToyKind.PortalB, s1);

            int x0 = m0.Left + Math.Max(40, Math.Min(m0.Width - w0 - 40, (int)(m0.Width * 0.72)));
            int y0 = m0.Top + Math.Max(60, m0.Height / 3);
            int x1 = m1.Left + Math.Max(40, Math.Min(m1.Width - w1 - 40, (int)(m1.Width * 0.28)));
            int y1 = m1.Top + Math.Max(60, m1.Height / 3);

            SpawnToy(ToyKind.PortalA, x0, y0, s0);
            SpawnToy(ToyKind.PortalB, x1, y1, s1);
        }
        else
        {
            Rectangle m = world.Monitors[0];
            double s = world.MonitorScales[0];
            (int w, _) = DesktopToy.GetDimensions(ToyKind.PortalA, s);
            int xA = m.Left + Math.Max(40, (int)(m.Width * 0.2));
            int xB = m.Left + Math.Max(40, Math.Min(m.Width - w - 40, (int)(m.Width * 0.8)));
            int y = m.Top + Math.Max(60, rng.Next(Math.Max(80, m.Height / 2)));
            SpawnToy(ToyKind.PortalA, xA, y, s);
            SpawnToy(ToyKind.PortalB, xB, y, s);
        }
    }

    public void Clear() => _toys.Clear();

    public void Step(CollisionWorld world, double deltaMs)
    {
        double dt = Math.Clamp(deltaMs / 1000.0, 0.0, 0.1);
        for (int i = _toys.Count - 1; i >= 0; i--)
        {
            DesktopToy toy = _toys[i];
            if (!toy.Active)
            {
                _toys.RemoveAt(i);
                continue;
            }

            toy.AgeSeconds += dt;
            if (toy.Pulse > 0)
            {
                toy.Pulse = Math.Max(0, toy.Pulse - dt * 3.5);
            }

            Rectangle cur = toy.Bounds;
            if (!world.IsInsideDesktop(cur))
            {
                Point clamped = world.ClampInsideDesktop(cur);
                toy.X = clamped.X;
                toy.Y = clamped.Y;
                cur = toy.Bounds;
            }

            // Gentle drop until resting on a window top or monitor bottom.
            if (!world.IsBlocked(cur, CollisionDirection.Down))
            {
                toy.VelocityY = Math.Min(520 * toy.Scale, toy.VelocityY + 950 * toy.Scale * dt);
                double dy = toy.VelocityY * dt;
                int stepY = Math.Max(1, (int)Math.Round(dy));
                int maxDrop = world.FindFloorDistanceBelow(cur, stepY);
                if (maxDrop <= stepY)
                {
                    toy.Y += maxDrop;
                    toy.VelocityY = 0;
                }
                else
                {
                    toy.Y += dy;
                }
            }
            else
            {
                toy.VelocityY = 0;
            }
        }
    }

    public DesktopToy? FindNearestFish(int centerX, int centerY, double maxDistance)
    {
        DesktopToy? best = null;
        double bestDistSq = maxDistance * maxDistance;

        for (int i = 0; i < _toys.Count; i++)
        {
            DesktopToy t = _toys[i];
            if (!t.Active || t.Kind != ToyKind.Fish)
            {
                continue;
            }

            double dx = (t.X + t.Width * 0.5) - centerX;
            double dy = (t.Y + t.Height * 0.5) - centerY;
            if (Math.Abs(dy) > 90)
            {
                continue;
            }

            double dSq = dx * dx + dy * dy;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                best = t;
            }
        }

        return best;
    }

    public DesktopToy? GetTwinPortal(ToyKind sourceKind)
    {
        ToyKind target = sourceKind == ToyKind.PortalA ? ToyKind.PortalB : ToyKind.PortalA;
        for (int i = 0; i < _toys.Count; i++)
        {
            if (_toys[i].Active && _toys[i].Kind == target)
            {
                return _toys[i];
            }
        }

        return null;
    }
}
