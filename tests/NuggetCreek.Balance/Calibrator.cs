using System;
using System.Linq;
using NuggetCreek.Core;

namespace NuggetCreek.Balance
{
    /// <summary>
    /// Finds, creek by creek, how much the solved unlock cost must be scaled so the target
    /// player reaches each creek on its target day (design doc 5.0.4, 6.2). Tier and upgrade
    /// costs follow the scaled creek costs exactly as tools/economy_tune.py derives them.
    /// </summary>
    public sealed class Calibrator
    {
        readonly BotProfile profile;
        readonly int seeds;
        readonly Action<EconomyConfig> baseTuning;

        public Calibrator(BotProfile profile, int seeds, Action<EconomyConfig> baseTuning)
        {
            this.profile = profile;
            this.seeds = seeds;
            this.baseTuning = baseTuning;
        }

        /// <summary>Scale per creek index (index 0 unused). Creeks past the last calibrated one
        /// (the 30-day bot does not reach them) reuse its scale.</summary>
        public double[] Solve()
        {
            int count = PacingModel.TargetDays.Length;
            int last = PacingModel.LastCalibratedRegion;
            var scales = new double[count];
            for (int i = 0; i < scales.Length; i++)
                scales[i] = 1;
            for (int creek = 1; creek <= last; creek++)
            {
                double target = PacingModel.TargetDays[creek];
                double low = Math.Log(0.05), high = Math.Log(1e5);
                for (int step = 0; step < 16; step++)
                {
                    double mid = (low + high) / 2;
                    SetFrom(scales, creek, Math.Exp(mid));
                    double day = MeanDay(scales, creek, target);
                    if (double.IsNaN(day) || day > target)
                        high = mid;
                    else
                        low = mid;
                }
                // The last scale that still made the target, so a boundary seed never tips over.
                SetFrom(scales, creek, Math.Exp(low));
                Console.WriteLine($"  {GameCatalog.RegionNames[creek],-18} scale {scales[creek],8:0.000}  day {MeanDay(scales, creek, target):0.000} (target {target:0.000})");
            }
            return scales;
        }

        /// <summary>Sets a creek's scale; the last calibrated creek also sets every later one.</summary>
        static void SetFrom(double[] scales, int creek, double scale)
        {
            int until = creek == PacingModel.LastCalibratedRegion ? scales.Length - 1 : creek;
            for (int r = creek; r <= until; r++)
                scales[r] = scale;
        }

        double MeanDay(double[] scales, int creek, double target)
        {
            int days = (int)Math.Ceiling(target * 2.5) + 2;
            // Onboarding never depends on ads: the opening session's creeks are solved for a
            // player who watches none.
            BotProfile player = target < FirstSessionEnd ? new BotProfile { Name = "no ads", WatchesAds = false } : profile;
            var arrivals = Enumerable.Range(0, seeds).Select(seed =>
            {
                EconomyConfig config = Build(scales);
                return new PlayerBot(config, player, 1000 + seed).Run(days).CreekDays[creek];
            }).ToList();
            if (arrivals.Any(double.IsNaN))
                return double.NaN;
            // Day-one creeks land on a session boundary; every seed must make the target session.
            return target < 1 ? arrivals.Max() : arrivals.Average();
        }

        /// <summary>Creeks due before this day belong to the opening session (design doc 10).</summary>
        const double FirstSessionEnd = 0.01;

        /// <summary>The tables the scales give, derived the way the tuning tool derives them.</summary>
        public EconomyConfig Build(double[] scales)
        {
            var config = new EconomyConfig();
            baseTuning(config);
            ApplyScales(config, scales);
            return config;
        }

        public static void ApplyScales(EconomyConfig config, double[] scales)
        {
            var pacing = new PacingModel(new Economy(new EconomyConfig()));
            // SolveRegionCost already carries the shipped calibration; start from the model's own cost.
            for (int r = 1; r < config.RegionCount; r++)
                config.RegionUnlockCosts[r] = PacingModel.RoundSignificant(pacing.SolveRegionCost(r).ToDouble() / PacingModel.BotCalibration[r] * scales[r]);
            for (int t = 1; t < config.TierCount; t++)
                config.TierCosts[t] = PacingModel.RoundSignificant(config.RegionUnlockCosts[t * config.TierRegionStep] * PacingModel.TierShare);
            for (int t = 1; t < config.UpgradeBaseCosts.Length; t++)
            {
                double share = PacingModel.RoundSignificant(config.TierCosts[t] * PacingModel.UpgradeBaseShare);
                config.UpgradeBaseCosts[t] = Math.Max(share, 2 * config.UpgradeBaseCosts[t - 1]);
            }
        }
    }
}
