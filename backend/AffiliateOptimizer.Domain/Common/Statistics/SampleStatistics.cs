using AffiliateOptimizer.Domain.Common.Clicks;

namespace AffiliateOptimizer.Domain.Common.Statistics;

/// <summary>
/// Mean, standard error and a 95% interval (normal approximation) over realized commissions. The interval is
/// computed from two observations on, but <see cref="Reliable"/> says whether it deserves to be called one:
/// the operator screens never claim a difference below <see cref="ReliableSampleSize"/> per side.
/// </summary>
public sealed record SampleStatistics(int Count, decimal Mean, decimal? StandardError, decimal? CiLow, decimal? CiHigh)
{
    public const double Z95 = 1.96;
    public const int ReliableSampleSize = 30;

    public static readonly SampleStatistics Empty = new(0, 0m, null, null, null);

    public bool Reliable => Count >= ReliableSampleSize && StandardError is not null;

    public static SampleStatistics Compute(IReadOnlyCollection<decimal> values)
    {
        if (values.Count == 0) return Empty;

        var n = values.Count;
        var sum = values.Sum();
        var mean = sum / n;

        if (n < 2) return new SampleStatistics(n, Money.Round4(mean), null, null, null);

        var meanDouble = (double)mean;
        var variance = values.Sum(v => Math.Pow((double)v - meanDouble, 2)) / (n - 1);
        var standardError = Math.Sqrt(variance / n);
        var halfWidth = (decimal)(Z95 * standardError);

        return new SampleStatistics(
            n,
            Money.Round4(mean),
            decimal.Round((decimal)standardError, 6, MidpointRounding.AwayFromZero),
            Money.Round4(mean - halfWidth),
            Money.Round4(mean + halfWidth));
    }

    /// <summary>Two-sample z test at 95%: true when the means differ by more than 1.96·√(SE₁² + SE₂²).</summary>
    public static bool MeansDiffer(SampleStatistics a, SampleStatistics b)
    {
        if (a.StandardError is not { } seA || b.StandardError is not { } seB) return false;
        var combined = Math.Sqrt((double)(seA * seA + seB * seB));
        return Math.Abs((double)(a.Mean - b.Mean)) > Z95 * combined;
    }

    /// <summary>True when this interval lies entirely above the other's: the only case the operator UI calls "leading".</summary>
    public bool ClearlyAbove(SampleStatistics other)
        => CiLow is { } low && other.CiHigh is { } otherHigh && low > otherHigh;
}
