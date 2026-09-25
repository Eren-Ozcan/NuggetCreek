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

        /// <summary>Pine Hollow opens about 10 minutes into the first session (design doc 10).</summary>
        public const double PineHollowDay = 10.0 / 1440;

        public Calibrator(BotProfile profile, int seeds, Action<EconomyConfig> baseTuning)
        {
            this.profile = profile;
            this.seeds = seeds;
            this.baseTuning = baseTuning;
        }

        /// <summary>Scale per creek index 1..6 (index 0 unused); creek 6 (post-launch) follows creek 5.</summary>
        public double[] Solve()
        {
            var scales = new double[7];
            for (int i = 0; i < scales.Length; i++)
                scales[i] = 1;
            for (int creek = 1; creek <= 5; creek++)
            {
                double target = creek == 1 ? PineHollowDay : PacingModel.TargetDays[creek];
                double low = Math.Log(0.05), high = Math.Log(1e5);
                for (int step = 0; step < 16; step++)
                {
                    double mid = (low + high) / 2;
                    scales[creek] = Math.Exp(mid);
                    if (creek == 5)
                        scales[6] = scales[5];
                    double day = MeanDay(scales, creek, target);
                    if (double.IsNaN(day) || day > target)
                        high = mid;
                    else
                        low = mid;
                }
                // The last scale that still made the target, so a boundary seed never tips over.
                scales[creek] = Math.Exp(low);
                if (creek == 5)
                    scales[6] = scales[5];
                Console.WriteLine($"  {GameCatalog.RegionNames[creek],-12} scale {scales[creek],8:0.000}  day {MeanDay(scales, creek, target):0.000} (target {target:0.000})");
            }
            return scales;
        }

        double MeanDay(double[] scales, int creek, double target)
        {
            int days = (int)Math.Ceiling(target * 2.5) + 2;
            // Onboarding never depends on ads: Pine Hollow is solved for a player who watches none.
            BotProfile player = creek == 1 ? new BotProfile { Name = "no ads", WatchesAds = false } : profile;
            var arrivals = Enumerable.Range(0, seeds).Select(seed =>
            {
                EconomyConfig config = Build(scales);
                return new PlayerBot(config, player, 1000 + seed).Run(days).CreekDays[creek];
            }).ToList();
            if (arrivals.Any(double.IsNaN))
                return double.NaN;
            // The first creeks land on a session boundary; every seed must make the target session.
            return creek <= 2 ? arrivals.Max() : arrivals.Average();
        }

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
            var regions = new double[8];
            // SolveRegionCost already carries the shipped calibration; start from the model's own cost.
            for (int r = 1; r <= 6; r++)
                regions[r] = PacingModel.RoundSignificant(pacing.SolveRegionCost(r).ToDouble() / PacingModel.BotCalibration[r] * scales[r]);
            regions[7] = regions[6] * regions[6] / regions[5];
            for (int r = 1; r < config.RegionCount; r++)
                config.RegionUnlockCosts[r] = regions[r];
            for (int t = 1; t < config.TierCount; t++)
                config.TierCosts[t] = PacingModel.RoundSignificant(regions[t] * PacingModel.TierShare);
            for (int t = 1; t < config.UpgradeBaseCosts.Length; t++)
            {
                double share = PacingModel.RoundSignificant(config.TierCosts[t] * PacingModel.UpgradeBaseShare);
                config.UpgradeBaseCosts[t] = Math.Max(share, 2 * config.UpgradeBaseCosts[t - 1]);
            }
        }
    }
}
