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

    // Cardinal spline through p1 -> p2. Curvature scales the tangents: 0 = straight lines (stopping at each point), 1 = Catmull-Rom, >1 = wider curves
    public static Vector3 Spline(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t, float curvature = 1)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        var m1 = (p2 - p0) * (curvature / 2);
        var m2 = (p3 - p1) * (curvature / 2);
        return (2 * t3 - 3 * t2 + 1) * p1 + (t3 - 2 * t2 + t) * m1 + (-2 * t3 + 3 * t2) * p2 + (t3 - t2) * m2;
    }

    // Exponential smoothing that behaves the same regardless of frame rate
    public static float SmoothingFactor(float smoothing, float dt) => smoothing <= 0 ? 1 : 1 - MathF.Exp(-dt / smoothing);
}
