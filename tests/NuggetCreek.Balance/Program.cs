using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NuggetCreek.Core;

namespace NuggetCreek.Balance
{
    /// <summary>
    /// Phase 2.8 balance check (design doc 19.4): plays 30 days with a few seeds and player
    /// profiles and compares creek openings with the target curve (5.0.4, 6.2) and the Gem
    /// income with the budget (5.0.6).
    /// </summary>
    public static class Program
    {
        const double Tolerance = 0.25;

        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            int days = ArgInt(args, "--days", 30);
            int seeds = ArgInt(args, "--seeds", 5);
            bool verbose = args.Contains("--verbose");

            var runs = new List<(BotProfile profile, Action<EconomyConfig> tune)>();
            if (args.Contains("--pine"))
            {
                // Pine Hollow must open inside the first session for every player (design doc 9, 10).
                foreach (double scale in new[] { 3.781, 3.6, 3.4, 3.2, 3.0 })
                {
                    var minutes = Enumerable.Range(0, 20).Select(seed =>
                    {
                        var config = new EconomyConfig();
                        var scales = PacingModel.BotCalibration.ToArray();
                        scales[1] = scale;
                        Calibrator.ApplyScales(config, scales);
                        return new PlayerBot(config, new BotProfile { WatchesAds = false }, 1000 + seed).Run(1).CreekDays[1] * 1440;
                    }).ToList();
                    Console.WriteLine($"scale {scale:0.000}: cost {PacingModel.RoundSignificant(new PacingModel(new Economy(new EconomyConfig())).SolveRegionCost(1).ToDouble() / PacingModel.BotCalibration[1] * scale)}  minutes mean {minutes.Average():0.0} max {minutes.Max():0.0}");
                }
                return 0;
            }
            if (args.Contains("--ratio"))
            {
                // Design rule: 40 min of active play earns at least one 24 h absence (no ad double).
                var model = new PacingModel(new Economy(new EconomyConfig()));
                for (int region = 0; region < new EconomyConfig().RegionCount; region++)
                    Console.WriteLine($"{GameCatalog.RegionNames[region],-14}{model.ActiveToAwayRatio(region),8:0.00}");
                var amos = Enumerable.Range(2, new EconomyConfig().AmosMaxLevel - 1)
                    .Select(level => PacingModel.RoundSignificant(model.SolveAmosLevelCost(level).ToDouble()).ToString("G3"));
                Console.WriteLine("AmosLevelCosts = { " + string.Join(", ", amos) + " }");
                return 0;
            }
            if (args.Contains("--calibrate"))
            {
                double divisor = ArgDouble(args, "--divisor", new EconomyConfig().PrestigeXpDivisor);
                Console.WriteLine($"Calibrating creek costs for the target player (watches ads), prestige divisor {divisor:G3}...");
                double ratio = ArgDouble(args, "--active-ratio", new EconomyConfig().ActiveIdleRatio);
                double pastCap = ArgDouble(args, "--past-cap", new EconomyConfig().OfflinePastCapRate);
                Console.WriteLine($"active/idle ratio {ratio}, past-cap rate {pastCap:0%}");
                Action<EconomyConfig> prestige = c =>
                {
                    c.PrestigeXpDivisor = divisor;
                    c.ActiveIdleRatio = ratio;
                    c.OfflinePastCapRate = pastCap;
                };
                var calibrator = new Calibrator(new BotProfile { Name = "calibration" }, seeds, prestige);
                double[] scales = calibrator.Solve();
                Console.WriteLine("scales: " + string.Join(", ", scales.Skip(1).Select(s => s.ToString("0.000"))));
                EconomyConfig solved = calibrator.Build(scales);
                Console.WriteLine("RegionUnlockCosts = { " + string.Join(", ", solved.RegionUnlockCosts.Select(v => v.ToString("G3"))) + " }");
                Console.WriteLine("TierCosts = { " + string.Join(", ", solved.TierCosts.Select(v => v.ToString("G3"))) + " }");
                Console.WriteLine("UpgradeBaseCosts = { " + string.Join(", ", solved.UpgradeBaseCosts.Select(v => v.ToString("G3"))) + " }");
                runs.Add((new BotProfile { Name = "calibrated: target player (watches ads)" }, c => { prestige(c); Calibrator.ApplyScales(c, scales); }));
                runs.Add((new BotProfile { Name = "calibrated: no ads", WatchesAds = false }, c => { prestige(c); Calibrator.ApplyScales(c, scales); }));
            }
            else 
if (args.Contains("--offline"))
            {
                // Offline levers at the approved wall rule, for the ad-watching target player.
                BotProfile Ads(string name) => new BotProfile { Name = name, Rebirth = RebirthPolicy.Suggested };
                runs.Add((Ads("offline: now"), c => { }));
                runs.Add((Ads("offline: past-cap 17%"), c => c.OfflinePastCapRate = 0.17));
                runs.Add((Ads("offline: past-cap 17%, Amos fixed at 1 h"), c =>
                {
                    c.OfflinePastCapRate = 0.17;
                    for (int i = 0; i < c.AmosLevelCosts.Length; i++)
                        c.AmosLevelCosts[i] = 1e300;
                }));
            }
            else if (args.Contains("--ablate"))
            {
                // Rebirth off so each line shows one system's pull on the first climb.
                BotProfile Base(string name) => new BotProfile { Name = name, WatchesAds = false, Rebirth = RebirthPolicy.Never };
                runs.Add((Base("ablate: everything"), c => { }));
                runs.Add((Base("ablate: no Mother Lode"), c => c.MotherLodeFirstAfterSeconds = 1e12));
                runs.Add((Base("ablate: no chests"), c => c.FirstChestAfterCatches = c.ChestEveryCatches = int.MaxValue));
                var noCrew = Base("ablate: no crew hires");
                noCrew.HiresCrew = false;
                runs.Add((noCrew, c => { }));
                var noDaily = Base("ablate: no daily systems");
                noDaily.DoesDaily = false;
                runs.Add((noDaily, c => { }));
                var bare = Base("ablate: none of the four");
                bare.HiresCrew = false;
                bare.DoesDaily = false;
                runs.Add((bare, c =>
                {
                    c.MotherLodeFirstAfterSeconds = 1e12;
                    c.FirstChestAfterCatches = c.ChestEveryCatches = int.MaxValue;
                }));
            }
            else
            {
                runs.Add((new BotProfile { Name = "target player: watches ads" }, c => { }));
                runs.Add((new BotProfile { Name = "no ads", WatchesAds = false }, c => { }));
            }

            bool pass = true;
            foreach ((BotProfile profile, Action<EconomyConfig> tune) in runs)
            {
                var reports = new List<BotReport>();
                for (int seed = 0; seed < seeds; seed++)
                {
                    var config = new EconomyConfig();
                    tune(config);
                    reports.Add(new PlayerBot(config, profile, 1000 + seed).Run(days));
                }
                pass &= Print(profile, reports, days, verbose);
            }
            Console.WriteLine(pass ? "RESULT: inside the exit gate" : "RESULT: outside the exit gate");
            return 0;
        }

        static bool Print(BotProfile profile, List<BotReport> reports, int days, bool verbose)
        {
            Console.WriteLine(new string('=', 78));
            Console.WriteLine($"{profile.Name}: {reports.Count} runs x {days} days");
            Console.WriteLine(new string('=', 78));

            bool pass = true;
            Console.WriteLine($"{"Creek",-14}{"target day",12}{"bot day (mean)",16}{"min..max",18}{"off by",10}{"prestige",10}");
            double[] targets = PacingModel.TargetDays;
            for (int creek = 1; creek < reports[0].CreekDays.Length; creek++)
            {
                // Pine Hollow: about 10 minutes into the first session (design doc 10).
                double target = creek == 1 ? Calibrator.PineHollowDay : targets[creek];
                var reached = reports.Select(r => r.CreekDays[creek]).Where(d => !double.IsNaN(d)).ToList();
                string name = GameCatalog.RegionNames[creek];
                if (target > days)
                {
                    Console.WriteLine($"{name,-14}{target,12:0.00}{"(after the run)",16}");
                    continue;
                }
                if (reached.Count < reports.Count)
                {
                    Console.WriteLine($"{name,-14}{target,12:0.00}{$"{reached.Count}/{reports.Count} reached",16}");
                    pass &= !profile.WatchesAds;
                    continue;
                }
                double mean = reached.Average();
                double off = (mean - target) / target;
                // The first creeks are minutes apart; judge them in minutes, not in percent.
                bool ok = target < 0.1 ? Math.Abs(mean - target) * 1440 <= 5 : Math.Abs(off) <= Tolerance;
                // Only the target player (watches ads) is held to the gate; the rest is reported.
                if (!profile.WatchesAds && creek > 1)
                    ok = true;
                pass &= ok;
                double multiplier = reports.Average(r => r.CreekMultipliers[creek]);
                Console.WriteLine($"{name,-14}{target,12:0.00}{mean,16:0.00}{$"{reached.Min():0.00}..{reached.Max():0.00}",18}{off,10:+0%;-0%}{"x" + multiplier.ToString("0.0"),10}{(ok ? "" : "  <-- outside")}");
            }

            var rebirths = reports.SelectMany(r => r.RebirthDays).ToList();
            Console.WriteLine($"rebirths per run: {rebirths.Count / (double)reports.Count:0.0}; first at day {(reports.Where(r => r.RebirthDays.Count > 0).Select(r => r.RebirthDays[0]).DefaultIfEmpty(double.NaN).Average()):0.0}");

            Console.WriteLine();
            Console.WriteLine("Gem income per day (mean):");
            var sources = reports.SelectMany(r => r.GemsBySource.Keys).Distinct().OrderBy(k => k);
            double total = reports.Average(r => r.GemsEarned) / days;
            foreach (string source in sources)
            {
                double perDay = reports.Average(r => r.GemsBySource.TryGetValue(source, out long v) ? v : 0) / days;
                Console.WriteLine($"  {source,-20}{perDay,8:0.0}");
            }
            Console.WriteLine($"  {"total",-20}{total,8:0.0}   (budget 5.0.6: 74)");
            double adGems = reports.Average(r => (r.GemsBySource.TryGetValue("wash (ad)", out long w) ? w : 0)
                                                 + (r.GemsBySource.TryGetValue("goal ad", out long g) ? g : 0)) / days;
            Console.WriteLine($"  rewarded share: {(total > 0 ? adGems / total : 0):0%} (limit 35%)");
            if (profile.WatchesAds)
                pass &= total > 0 && adGems / total <= 0.35 && Math.Abs(total - 74) / 74 <= 0.10;
            Console.WriteLine("Gem spend over the run (mean):");
            foreach (string sink in reports.SelectMany(r => r.GemsBySpend.Keys).Distinct().OrderBy(k => k))
                Console.WriteLine($"  {sink,-20}{reports.Average(r => r.GemsBySpend.TryGetValue(sink, out long v) ? v : 0),8:0}");
            Console.WriteLine($"crew hired: {reports.Average(r => r.CrewHired):0.0}; Mother Lodes/day: {reports.Average(r => r.MotherLodes) / days:0.0}; " +
                              $"chests/day: {reports.Average(r => r.ChestsOpened) / days:0.0}; ads/day: {reports.Average(r => r.AdsWatched) / days:0.0}; " +
                              $"offline payout in hours of the offline rate/day (Daily Wash boosts counted, the x2 ad not): {reports.Average(r => r.OfflineSecondsCredited) / days / 3600:0.0}");

            Console.WriteLine("Dollars before the first rebirth, by source:");
            var byDollar = reports.SelectMany(r => r.FirstClimbDollars.Keys).Distinct().ToList();
            double climb = reports.Average(r => r.FirstClimbDollars.Values.Sum());
            foreach (string source in byDollar.OrderByDescending(k => reports.Average(r => r.FirstClimbDollars.TryGetValue(k, out double v) ? v : 0)))
            {
                double share = reports.Average(r => r.FirstClimbDollars.TryGetValue(source, out double v) ? v : 0) / climb;
                Console.WriteLine($"  {source,-14}{share,6:0%}");
            }

            if (verbose)
            {
                Console.WriteLine();
                foreach (string line in reports[0].DayLines)
                    Console.WriteLine("  " + line);
            }
            Console.WriteLine();
            return pass;
        }

        static double ArgDouble(string[] args, string name, double fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length && double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
        }

        static int ArgInt(string[] args, string name, int fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int value) ? value : fallback;
        }
    }
}
