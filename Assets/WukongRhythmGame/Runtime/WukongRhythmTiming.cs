using System;

public enum WukongTimingGrade { TooEarly, Perfect, Good, TooLate }

/// <summary>Shared by every contact path and by deterministic timing regression tests.</summary>
public static class WukongRhythmTiming
{
    public static WukongTimingGrade Judge(double error, double perfectWindow, double goodWindow)
    {
        if (double.IsNaN(error) || double.IsInfinity(error)) return WukongTimingGrade.TooLate;
        goodWindow = Math.Max(0.001, goodWindow);
        perfectWindow = Math.Max(0, Math.Min(perfectWindow, goodWindow));
        const double precision = 0.000001;
        if (error < -goodWindow - precision) return WukongTimingGrade.TooEarly;
        if (error > goodWindow + precision) return WukongTimingGrade.TooLate;
        return Math.Abs(error) <= perfectWindow + precision ? WukongTimingGrade.Perfect : WukongTimingGrade.Good;
    }

    public static float Accuracy(int perfect, int good, int total)
    {
        return total <= 0 ? 100f : (float)(100.0 * (perfect + good * 0.65) / total);
    }
}
