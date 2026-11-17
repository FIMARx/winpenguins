using System.Drawing;

namespace WinPenguins.Engine;

public struct SnowParticle
{
    public float X;
    public float Y;
    public float SpeedY;
    public float Phase;
    public float Size;
    public float SettledSecondsRemaining;
    public bool Active;
    public bool Settled;
}

public sealed class SnowSystem
{
    public const int Capacity = 200;
    private readonly SnowParticle[] _particles = new SnowParticle[Capacity];
    private readonly Random _rng;

    public ReadOnlySpan<SnowParticle> Particles => _particles;

    public SnowSystem(Random? rng = null)
    {
        _rng = rng ?? Random.Shared;
    }

    public void Clear()
    {
        Array.Clear(_particles);
    }

    public void Step(CollisionWorld world, IReadOnlyList<Penguin> penguins, double deltaMs, bool enabled)
    {
        if (!enabled)
        {
            Clear();
            return;
        }

        double dt = Math.Clamp(deltaMs / 1000.0, 0.0, 0.1);
        Rectangle virt = world.VirtualBounds;

        for (int i = 0; i < _particles.Length; i++)
        {
            ref SnowParticle f = ref _particles[i];
            if (!f.Active)
            {
                if (_rng.NextDouble() < 0.12)
                {
                    SpawnFlake(ref f, world);
                }
                continue;
            }

            if (f.Settled)
            {
                f.SettledSecondsRemaining -= (float)dt;
                if (f.SettledSecondsRemaining <= 0)
                {
                    f.Active = false;
                    continue;
                }

                // Scatter if a penguin walks right over it or if the window underneath moved away.
                int px = (int)f.X;
                int py = (int)f.Y;
                var testFoot = new Rectangle(px, py - 1, 2, 3);
                if (!world.IntersectsAnyWindow(testFoot) && world.ContainsPoint(px, py + 2))
                {
                    f.Settled = false;
                }
                else
                {
                    for (int pIdx = 0; pIdx < penguins.Count; pIdx++)
                    {
                        Penguin p = penguins[pIdx];
                        if (!p.Active) continue;
                        if (Math.Abs((p.X + p.Width * 0.5f) - f.X) < p.Width * 0.6f &&
                            Math.Abs(p.Bounds.Bottom - f.Y) < 10)
                        {
                            f.Active = false;
                            break;
                        }
                    }
                }
                continue;
            }

            f.Phase += (float)(dt * 2.2);
            f.X += (float)(Math.Sin(f.Phase) * 22.0 * dt);
            f.Y += (float)(f.SpeedY * dt);

            int ix = (int)f.X;
            int iy = (int)f.Y;
            if (!world.ContainsPoint(ix, iy))
            {
                if (iy > virt.Bottom || iy < virt.Top)
                {
                    f.Active = false;
                }
                continue;
            }

            var probe = new Rectangle(ix, iy, 2, 2);
            if (world.IntersectsAnyWindow(probe) || !world.ContainsPoint(ix, iy + 3))
            {
                if (_rng.NextDouble() < 0.6)
                {
                    f.Settled = true;
                    f.SettledSecondsRemaining = 4.0f + (float)(_rng.NextDouble() * 5.0);
                }
                else
                {
                    f.Active = false;
                }
            }
        }
    }

    private void SpawnFlake(ref SnowParticle f, CollisionWorld world)
    {
        Rectangle mon = world.PickSpawnMonitor(_rng, out double monScale);
        f.X = mon.Left + (float)(_rng.NextDouble() * Math.Max(1, mon.Width - 2));
        f.Y = mon.Top + 1f;
        f.SpeedY = (float)((38.0 + _rng.NextDouble() * 52.0) * Math.Max(1.0, monScale));
        f.Phase = (float)(_rng.NextDouble() * Math.PI * 2.0);
        f.Size = (float)((2.0 + _rng.NextDouble() * 2.5) * Math.Max(1.0, monScale * 0.85));
        f.Settled = false;
        f.SettledSecondsRemaining = 0;
        f.Active = true;
    }
}
