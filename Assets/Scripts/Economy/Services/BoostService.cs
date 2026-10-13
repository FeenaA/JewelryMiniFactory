using System;

namespace JewelryMiniFactory.Economy
{
    /// <summary>Tracks one Boost window using caller-supplied UTC timestamps, without Unity dependencies.</summary>
    public sealed class BoostService
    {
        public bool Enabled { get; }
        public double DurationSeconds { get; }
        public double Multiplier { get; }
        public DateTimeOffset? StartedAtUtc { get; private set; }
        public DateTimeOffset? EndsAtUtc { get; private set; }

        public BoostService(IFactoryConfigSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Enabled = source.BoostEnabled;
            DurationSeconds = source.BoostDuration;
            Multiplier = source.BoostMultiplier;
            MachineConfig.ValidateAmount(DurationSeconds, nameof(source.BoostDuration));
            MachineConfig.ValidateAmount(Multiplier, nameof(source.BoostMultiplier));
            if (DurationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(source.BoostDuration));
            if (Multiplier < 1) throw new ArgumentOutOfRangeException(nameof(source.BoostMultiplier));
        }

        public bool TryActivate(DateTimeOffset utcNow)
        {
            utcNow = utcNow.ToUniversalTime();
            if (!Enabled || (EndsAtUtc.HasValue && utcNow < EndsAtUtc.Value)) return false;
            // Calculate first so an out-of-range date cannot partially change the window.
            var end = utcNow.AddSeconds(DurationSeconds);
            if (end <= utcNow) throw new ArgumentOutOfRangeException(nameof(DurationSeconds));
            StartedAtUtc = utcNow;
            EndsAtUtc = end;
            return true;
        }

        public bool IsActive(DateTimeOffset utcNow) => Enabled && StartedAtUtc.HasValue &&
            utcNow >= StartedAtUtc.Value && utcNow < EndsAtUtc.Value;

        public double GetRemainingSeconds(DateTimeOffset utcNow) =>
            IsActive(utcNow) ? (EndsAtUtc.Value - utcNow).TotalSeconds : 0;

        public double GetCurrentMultiplier(DateTimeOffset utcNow) => IsActive(utcNow) ? Multiplier : 1;

        /// <summary>
        /// Returns normal seconds plus the boosted overlap weighted by Multiplier.
        /// Pass the result to FactoryService.AccrueIncome. Settle any previous interval
        /// before activating a new Boost; this service retains only the latest window.
        /// Expired windows remain available for calculating intervals across their end.
        /// </summary>
        public double GetEffectiveElapsedSeconds(DateTimeOffset fromUtc, DateTimeOffset toUtc)
        {
            if (toUtc < fromUtc) throw new ArgumentOutOfRangeException(nameof(toUtc));
            var seconds = (toUtc - fromUtc).TotalSeconds;
            if (!Enabled || !StartedAtUtc.HasValue || seconds == 0) return seconds;
            var overlapStart = fromUtc > StartedAtUtc.Value ? fromUtc : StartedAtUtc.Value;
            var overlapEnd = toUtc < EndsAtUtc.Value ? toUtc : EndsAtUtc.Value;
            if (overlapEnd <= overlapStart) return seconds;
            var effective = seconds + (overlapEnd - overlapStart).TotalSeconds * (Multiplier - 1);
            MachineConfig.ValidateAmount(effective, nameof(effective));
            return effective;
        }
    }
}
