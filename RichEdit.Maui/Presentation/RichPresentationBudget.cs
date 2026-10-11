using System.Diagnostics;

namespace RichEdit.Maui;

// A pulse yields between indivisible native/view operations. It cannot preempt
// an application factory or a native layout call already in progress.
internal sealed class RichPresentationBudget
{
    internal const double TargetMilliseconds = 8;
    private readonly Func<double> _elapsed;
    private int _started;

    internal RichPresentationBudget() : this(ElapsedClock()) { }
    internal RichPresentationBudget(Func<double> elapsed) => _elapsed = elapsed;

    internal bool TryStart(double estimatedMilliseconds = 0)
    {
        if (_started != 0 && _elapsed() + estimatedMilliseconds >= TargetMilliseconds) return false;
        _started++;
        return true;
    }

    private static Func<double> ElapsedClock()
    {
        var started = Stopwatch.GetTimestamp();
        return () => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
}

// Fit marginal cost across differently sized publications. Fixed native setup
// and layout overhead must not cause repeated one-item batches that perform
// the same setup once for every reservation.
internal sealed class RichPresentationCostEstimate
{
    private readonly Queue<(int Count, int Existing, double Milliseconds)> _samples = [];
    internal double MillisecondsPerItem { get; private set; } = 0.25;

    internal void Observe(int count, double milliseconds, int existing = 0)
    {
        if (count <= 0 || !double.IsFinite(milliseconds) || milliseconds < 0) return;
        _samples.Enqueue((count, existing, milliseconds));
        if (_samples.Count > 16) _samples.Dequeue();
        if (_samples.Count < 6) return;
        var meanCount = _samples.Average(sample => sample.Count);
        if (_samples.Max(sample => sample.Count) - _samples.Min(sample => sample.Count) < Math.Max(4, meanCount / 4)) return;
        var meanTime = _samples.Average(sample => sample.Milliseconds);
        var meanExisting = _samples.Average(sample => sample.Existing);
        var variance = 0d;
        var covariance = 0d;
        var existingVariance = 0d;
        var existingCovariance = 0d;
        var cross = 0d;
        foreach (var sample in _samples)
        {
            var distance = sample.Count - meanCount;
            variance += distance * distance;
            covariance += distance * (sample.Milliseconds - meanTime);
            var previous = sample.Existing - meanExisting;
            existingVariance += previous * previous;
            existingCovariance += previous * (sample.Milliseconds - meanTime);
            cross += distance * previous;
        }
        var marginal = covariance / variance;
        var retainedCost = 0d;
        if (existingVariance > 0)
        {
            var determinant = variance * existingVariance - cross * cross;
            // Correlated sample sizes cannot distinguish setup growth from
            // new-object cost. Keep the prior estimate until they can.
            if (determinant < variance * existingVariance * 0.05) return;
            marginal = (covariance * existingVariance - existingCovariance * cross) / determinant;
            retainedCost = (existingCovariance * variance - covariance * cross) / determinant;
        }
        var fixedCost = meanTime - marginal * meanCount - retainedCost * meanExisting;
        if (marginal <= 0 || retainedCost < 0 || fixedCost < -meanTime * 0.25) return;
        marginal = Math.Clamp(marginal, MillisecondsPerItem / 2, MillisecondsPerItem * 2);
        MillisecondsPerItem = Math.Max(0.01, 0.8 * MillisecondsPerItem + 0.2 * marginal);
    }
}
