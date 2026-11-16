using System.Drawing;

namespace WinPenguins.Engine;

public sealed class CollisionWorld
{
    public IReadOnlyList<Rectangle> Monitors { get; private set; }
    public IReadOnlyList<double> MonitorScales { get; private set; }
    public IReadOnlyList<Rectangle> Windows { get; private set; }
    public Rectangle VirtualBounds { get; private set; }

    public CollisionWorld(
        IReadOnlyList<Rectangle> monitors,
        IReadOnlyList<Rectangle>? windows = null,
        IReadOnlyList<double>? monitorScales = null)
    {
        Monitors = monitors.Count > 0 ? monitors : [new Rectangle(0, 0, 1920, 1080)];
        MonitorScales = NormalizeScales(Monitors.Count, monitorScales);
        Windows = windows ?? Array.Empty<Rectangle>();
        VirtualBounds = ComputeUnion(Monitors);
    }

    public void Update(
        IReadOnlyList<Rectangle> monitors,
        IReadOnlyList<Rectangle> windows,
        IReadOnlyList<double>? monitorScales = null)
    {
        if (monitors.Count > 0)
        {
            Monitors = monitors;
            MonitorScales = NormalizeScales(Monitors.Count, monitorScales);
            VirtualBounds = ComputeUnion(Monitors);
        }

        Windows = windows;
    }

    public double GetScaleAt(int x, int y, double fallbackScale)
    {
        int idx = FindMonitorIndexAt(x, y);
        return idx >= 0 ? MonitorScales[idx] : fallbackScale;
    }

    public double GetScaleAtWithHysteresis(
        int x,
        int y,
        double currentScale,
        double fallbackScale,
        int hysteresisPx = 6)
    {
        int idx = FindMonitorIndexAt(x, y);
        if (idx < 0)
        {
            return fallbackScale;
        }

        double targetScale = MonitorScales[idx];
        if (Monitors.Count <= 1 || Math.Abs(targetScale - currentScale) < 0.001)
        {
            return targetScale;
        }

        Rectangle m = Monitors[idx];
        bool nearEdge = (x - m.Left) < hysteresisPx
            || (m.Right - x) <= hysteresisPx
            || (y - m.Top) < hysteresisPx
            || (m.Bottom - y) <= hysteresisPx;

        if (nearEdge)
        {
            for (int i = 0; i < Monitors.Count; i++)
            {
                if (i == idx || Math.Abs(MonitorScales[i] - currentScale) >= 0.001)
                {
                    continue;
                }

                Rectangle inflated = Rectangle.Inflate(Monitors[i], hysteresisPx, hysteresisPx);
                if (inflated.Contains(x, y))
                {
                    return currentScale;
                }
            }
        }

        return targetScale;
    }

    public int FindMonitorIndexAt(int x, int y)
    {
        for (int i = 0; i < Monitors.Count; i++)
        {
            if (Monitors[i].Contains(x, y))
            {
                return i;
            }
        }

        return -1;
    }

    public bool IsNearMonitorBoundary(Rectangle bounds, CollisionDirection dir, int margin = 6)
    {
        if (Monitors.Count <= 1 || dir is not (CollisionDirection.Left or CollisionDirection.Right))
        {
            return false;
        }

        int cx = bounds.Left + bounds.Width / 2;
        int cy = bounds.Top + bounds.Height / 2;
        int idx = FindMonitorIndexAt(cx, cy);
        if (idx < 0)
        {
            return false;
        }

        Rectangle m = Monitors[idx];
        if (dir == CollisionDirection.Right)
        {
            return (m.Right - bounds.Right) <= margin && HasMonitorHorizontallyAdjacent(idx, toRight: true);
        }

        return (bounds.Left - m.Left) <= margin && HasMonitorHorizontallyAdjacent(idx, toRight: false);
    }

    public bool HasAdjacentMonitorReachableUpward(Rectangle bounds, CollisionDirection dir)
    {
        if (Monitors.Count <= 1 || dir is not (CollisionDirection.Left or CollisionDirection.Right))
        {
            return false;
        }

        int idx = FindMonitorIndexAt(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        if (idx < 0)
        {
            return false;
        }

        Rectangle cur = Monitors[idx];
        for (int i = 0; i < Monitors.Count; i++)
        {
            if (i == idx)
            {
                continue;
            }

            Rectangle other = Monitors[i];
            bool touchesBorder = dir == CollisionDirection.Right
                ? Math.Abs(other.Left - cur.Right) <= 4
                : Math.Abs(other.Right - cur.Left) <= 4;

            if (touchesBorder && other.Top < bounds.Top)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasMonitorHorizontallyAdjacent(int idx, bool toRight)
    {
        Rectangle cur = Monitors[idx];
        for (int i = 0; i < Monitors.Count; i++)
        {
            if (i == idx)
            {
                continue;
            }

            Rectangle other = Monitors[i];
            bool touches = toRight
                ? Math.Abs(other.Left - cur.Right) <= 4
                : Math.Abs(other.Right - cur.Left) <= 4;

            if (touches && other.Bottom > cur.Top && other.Top < cur.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    public bool ContainsPoint(int x, int y)
    {
        for (int i = 0; i < Monitors.Count; i++)
        {
            if (Monitors[i].Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsInsideDesktop(Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        return ContainsPoint(rect.Left, rect.Top)
            && ContainsPoint(rect.Right - 1, rect.Top)
            && ContainsPoint(rect.Left, rect.Bottom - 1)
            && ContainsPoint(rect.Right - 1, rect.Bottom - 1);
    }

    public bool IntersectsAnyWindow(Rectangle rect)
    {
        for (int i = 0; i < Windows.Count; i++)
        {
            if (Windows[i].IntersectsWith(rect))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsBlocked(Rectangle currentRect, CollisionDirection direction)
    {
        Rectangle testRect = direction switch
        {
            CollisionDirection.Left => new Rectangle(currentRect.X - 1, currentRect.Y, currentRect.Width, currentRect.Height),
            CollisionDirection.Right => new Rectangle(currentRect.X + 1, currentRect.Y, currentRect.Width, currentRect.Height),
            CollisionDirection.Up => new Rectangle(currentRect.X, currentRect.Y - 1, currentRect.Width, currentRect.Height),
            CollisionDirection.Down => new Rectangle(currentRect.X, currentRect.Y + 1, currentRect.Width, currentRect.Height),
            _ => currentRect
        };

        if (direction != CollisionDirection.Here && !IsInsideDesktop(testRect))
        {
            return true;
        }

        return IntersectsAnyWindow(testRect);
    }

    public bool IsPastBottomOfAnyCoveringMonitor(Rectangle rect)
    {
        int centerX = rect.Left + rect.Width / 2;
        int bestBottom = int.MinValue;

        for (int i = 0; i < Monitors.Count; i++)
        {
            Rectangle m = Monitors[i];
            if (centerX >= m.Left && centerX < m.Right)
            {
                if (m.Bottom > bestBottom)
                {
                    bestBottom = m.Bottom;
                }
            }
        }

        if (bestBottom == int.MinValue)
        {
            bestBottom = VirtualBounds.Bottom;
        }

        return rect.Bottom > bestBottom;
    }

    public Rectangle PickSpawnMonitor(Random rng, out double monitorScale)
    {
        if (Monitors.Count == 1)
        {
            monitorScale = MonitorScales[0];
            return Monitors[0];
        }

        long totalWidth = 0;
        for (int i = 0; i < Monitors.Count; i++)
        {
            totalWidth += Math.Max(1, Monitors[i].Width);
        }

        long pick = (long)(rng.NextDouble() * totalWidth);
        long acc = 0;
        for (int i = 0; i < Monitors.Count; i++)
        {
            acc += Math.Max(1, Monitors[i].Width);
            if (pick < acc)
            {
                monitorScale = MonitorScales[i];
                return Monitors[i];
            }
        }

        monitorScale = MonitorScales[0];
        return Monitors[0];
    }

    public bool IsPositionValid(Rectangle rect, bool ignoreWindows)
    {
        if (!IsInsideDesktop(rect))
        {
            return false;
        }

        return ignoreWindows || !IntersectsAnyWindow(rect);
    }

    public Point ClampInsideDesktop(Rectangle rect)
    {
        if (IsInsideDesktop(rect))
        {
            return rect.Location;
        }

        int cx = rect.Left + rect.Width / 2;
        int cy = rect.Top + rect.Height / 2;
        Rectangle target = Monitors[0];
        long bestDistSq = long.MaxValue;

        for (int i = 0; i < Monitors.Count; i++)
        {
            Rectangle m = Monitors[i];
            if (m.Contains(cx, cy))
            {
                target = m;
                break;
            }

            long dx = cx < m.Left ? m.Left - cx : (cx >= m.Right ? cx - m.Right + 1 : 0);
            long dy = cy < m.Top ? m.Top - cy : (cy >= m.Bottom ? cy - m.Bottom + 1 : 0);
            long distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                target = m;
            }
        }

        int maxX = Math.Max(target.Left, target.Right - rect.Width);
        int maxY = Math.Max(target.Top, target.Bottom - rect.Height);
        return new Point(
            Math.Clamp(rect.X, target.Left, maxX),
            Math.Clamp(rect.Y, target.Top, maxY));
    }

    public int ClampHorizontalToDesktop(int x, int y, int width, int height)
    {
        Point clamped = ClampInsideDesktop(new Rectangle(x, y, width, height));
        return clamped.X;
    }

    private static IReadOnlyList<double> NormalizeScales(int count, IReadOnlyList<double>? scales)
    {
        var result = new double[count];
        for (int i = 0; i < count; i++)
        {
            double s = scales != null && i < scales.Count ? scales[i] : 1.0;
            result[i] = Math.Clamp(s, 0.5, 4.0);
        }

        return result;
    }

    private static Rectangle ComputeUnion(IReadOnlyList<Rectangle> rects)
    {
        Rectangle union = rects[0];
        for (int i = 1; i < rects.Count; i++)
        {
            union = Rectangle.Union(union, rects[i]);
        }

        return union;
    }
}
