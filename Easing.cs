using System;
using System.ComponentModel.DataAnnotations;
using System.Numerics;

namespace Cammy;

public static class Easing
{
    public enum Curve
    {
        Linear,
        Sine,
        Quad,
        Cubic,
        Quart,
        Quint,
        Expo,
        Circ
    }

    public enum Direction
    {
        [Display(Name = "In / Out")] InOut,
        In,
        Out
    }

    public static float Ease(float t, Curve curve, Direction direction)
    {
        t = Math.Clamp(t, 0, 1);
        return direction switch
        {
            Direction.In => EaseIn(t, curve),
            Direction.Out => 1 - EaseIn(1 - t, curve),
            _ => t < 0.5f ? EaseIn(t * 2, curve) / 2 : 1 - EaseIn(2 - t * 2, curve) / 2
        };
    }

    private static float EaseIn(float t, Curve curve) => curve switch
    {
        Curve.Sine => 1 - MathF.Cos(t * MathF.PI / 2),
        Curve.Quad => t * t,
        Curve.Cubic => t * t * t,
        Curve.Quart => t * t * t * t,
        Curve.Quint => t * t * t * t * t,
        Curve.Expo => t <= 0 ? 0 : MathF.Pow(2, 10 * t - 10),
        Curve.Circ => 1 - MathF.Sqrt(1 - t * t),
        _ => t
    };

    public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
    }

    // Exponential smoothing that behaves the same regardless of frame rate
    public static float SmoothingFactor(float smoothing, float dt) => smoothing <= 0 ? 1 : 1 - MathF.Exp(-dt / smoothing);
}
