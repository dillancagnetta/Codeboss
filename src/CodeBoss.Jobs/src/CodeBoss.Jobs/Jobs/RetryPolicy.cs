using System;
using CodeBoss.Jobs.Model;

namespace CodeBoss.Jobs.Jobs;

/// <summary>
/// Backoff schedule for retrying a failed job.
/// </summary>
public static class RetryPolicy
{
    /// <summary>
    /// Delay before the next attempt: exponential growth, capped, with full jitter.
    /// </summary>
    /// <param name="failedAttempt">
    /// The attempt that just failed, 1-based. 1 means the scheduled fire failed and this is the delay
    /// before the first retry.
    /// </param>
    /// <param name="baseDelay">Delay scale. The cap is reached after roughly log2(max/base) attempts.</param>
    /// <param name="maxDelay">Ceiling on the exponential term, before jitter.</param>
    /// <param name="random">Jitter source. Defaults to <see cref="Random.Shared"/>.</param>
    /// <remarks>
    /// <para><b>Capped</b>, because uncapped doubling reaches multi-hour delays within ten attempts,
    /// long past the point where the job's next scheduled fire would have run anyway.</para>
    ///
    /// <para><b>Jittered</b>, because jobs that fail together usually failed for the same reason — a
    /// downstream dependency is down. Retrying them in lockstep re-creates the thundering herd that
    /// took it down. Full jitter (uniform over the whole window rather than a fraction of it) spreads
    /// them widest for the same mean delay.</para>
    /// </remarks>
    public static TimeSpan Delay(int failedAttempt, TimeSpan baseDelay, TimeSpan maxDelay, Random random = null)
    {
        if (failedAttempt < 1) failedAttempt = 1;
        if (baseDelay <= TimeSpan.Zero) baseDelay = TimeSpan.FromSeconds(1);
        if (maxDelay < baseDelay) maxDelay = baseDelay;

        random ??= Random.Shared;

        // Exponent is failedAttempt - 1 so the first retry waits one baseDelay, not half of one.
        // Guard the power against overflow to infinity on a large attempt count.
        var exponential = Math.Min(
            baseDelay.TotalSeconds * Math.Pow(2, Math.Min(failedAttempt - 1, 32)),
            maxDelay.TotalSeconds);

        return TimeSpan.FromSeconds(random.NextDouble() * exponential);
    }

    /// <summary>Backoff for a job row, using its configured base and cap.</summary>
    public static TimeSpan DelayFor(ServiceJob job, int failedAttempt, Random random = null) =>
        Delay(
            failedAttempt,
            TimeSpan.FromSeconds(job?.RetryBackoffBaseSeconds ?? 30),
            TimeSpan.FromSeconds(job?.RetryBackoffMaxSeconds ?? 900),
            random);

    /// <summary>
    /// Whether another attempt is allowed. <paramref name="failedAttempt"/> is 1-based, so a job with
    /// <c>MaxRetries = 2</c> permits attempts 2 and 3.
    /// </summary>
    public static bool ShouldRetry(ServiceJob job, int failedAttempt) =>
        job is { MaxRetries: > 0 } && failedAttempt <= job.MaxRetries;
}
