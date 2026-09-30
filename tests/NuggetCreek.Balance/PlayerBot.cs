using System;
using System.Collections.Generic;
using System.Linq;
using NuggetCreek.Core;

namespace NuggetCreek.Balance
{
    public enum RebirthPolicy
    {
        /// <summary>Stakes a new claim when the game suggests it (the 5.0.5 wall).</summary>
        Suggested,
        Never,
    }

    /// <summary>How a simulated player behaves.</summary>
    public sealed class BotProfile
    {
        public RebirthPolicy Rebirth = RebirthPolicy.Suggested;
        public bool HiresCrew = true;
        public bool DoesDaily = true;

        public string Name = "typical";
        /// <summary>Session start times, hours after local midnight.</summary>
        public double[] SessionHours = { 8, 12.5, 18, 22 };
        public double SessionMinutes = 7;
        public double FirstSessionMinutes = 10;
        /// <summary>Share of spawns caught by hand.</summary>
        public double CatchRate = 0.9;
        /// <summary>Share of river floaters (chests, pouches, crates) caught; they are big and slow.</summary>
        public double FloaterCatchRate = 0.95;
        /// <summary>Watches offline x2, chest and crate x2, Gem Pouches, ad washes and the streak rescue.</summary>
        public bool WatchesAds = true;
        /// <summary>Guild ads watched per day (Guild XP).</summary>
        public int GuildAdsPerDay = 2;
        /// <summary>Mother Lode hits per second.</summary>
        public double LodeHitsPerSecond = 5;
    }

    /// <summary>What one simulated run measured.</summary>
    public sealed class BotReport
    {
        public BotProfile Profile;
        /// <summary>Day (fractional) each creek was first reached; index = creek. NaN = never.</summary>
        public double[] CreekDays;
        /// <summary>Prestige multiplier when each creek was first reached.</summary>
        public double[] CreekMultipliers;
        /// <summary>Day each creek's boss was first beaten (design doc 3.4.1); NaN = never.</summary>
        public double[] BossDays;
        /// <summary>Rebirths done when each boss was first beaten.</summary>
        public int[] BossRebirths;
        public readonly List<double> RebirthDays = new List<double>();
        public readonly Dictionary<string, long> GemsBySource = new Dictionary<string, long>();
        public readonly Dictionary<string, long> GemsBySpend = new Dictionary<string, long>();
        public readonly List<string> DayLines = new List<string>();
        public int AdsWatched;
        /// <summary>Dollars earned before the first rebirth, by source.</summary>
        public readonly Dictionary<string, double> FirstClimbDollars = new Dictionary<string, double>();
        public double OfflineSecondsCredited;
        public int CrewHired;
        public int MotherLodes;
        public int ChestsOpened;
        public int CratesOpened;
        public int PouchesOpened;
        public int FloatersMissed;
        public int Days;
        public long GemsEarned;

        public void AddGems(string source, long amount)
        {
            if (amount <= 0)
                return;
            GemsBySource.TryGetValue(source, out long total);
            GemsBySource[source] = total + amount;
            GemsEarned += amount;
        }

        public void SpendGems(string sink, long amount)
        {
            if (amount <= 0)
                return;
            GemsBySpend.TryGetValue(sink, out long total);
            GemsBySpend[sink] = total + amount;
        }
    }

    /// <summary>
    /// Plays <see cref="GameSession"/> day by day: sessions of active swiping with idle
    /// income, offline returns in between, and a greedy spend policy. Every rule comes from
    /// the core; the bot only decides what to press and when.
    /// </summary>
    public sealed class PlayerBot
    {
        const double Step = 0.25;
        const double DaySeconds = 86400;
        /// <summary>Game day 20000 starts here (UTC offset 0, rollover 04:00).</summary>
        const long FirstDay = 20000;

        readonly EconomyConfig config;
        readonly BotProfile profile;
        readonly Random rng;
        readonly GameSession session;
        readonly BotReport report;
        double now;
        double leftUtc;
        BigNumber earnedAtDayStart = BigNumber.Zero;
        int rebirthsAtDayStart;
        double spawnIn;

        public PlayerBot(EconomyConfig config, BotProfile profile, int seed)
        {
            this.config = config;
            this.profile = profile;
            rng = new Random(seed);
            session = new GameSession(new Economy(config), new PlayerProgress(), new Random(seed + 1));
            report = new BotReport
            {
                Profile = profile,
                CreekDays = new double[config.RegionCount],
                CreekMultipliers = new double[config.RegionCount],
                BossDays = Enumerable.Repeat(double.NaN, config.RegionCount).ToArray(),
                BossRebirths = new int[config.RegionCount],
            };
            for (int i = 0; i < report.CreekDays.Length; i++)
                report.CreekDays[i] = double.NaN;
            report.CreekDays[0] = 0;
        }

        double Midnight(int day) => (FirstDay + day) * DaySeconds;

        double DayOf(double utc) => (utc - Midnight(0) - profile.SessionHours[0] * 3600) / DaySeconds;

        public BotReport Run(int days)
        {
            for (int day = 0; day < days; day++)
            {
                for (int s = 0; s < profile.SessionHours.Length; s++)
                {
                    double start = Midnight(day) + profile.SessionHours[s] * 3600;
                    double minutes = day == 0 && s == 0 ? profile.FirstSessionMinutes : profile.SessionMinutes;
                    PlaySession(start, minutes * 60, day == 0 && s == 0);
                }
                if (profile.WatchesAds && session.IsUnlocked(Feature.Guild))
                    for (int i = 0; i < profile.GuildAdsPerDay; i++)
                    {
                        session.AddGuildAdXp();
                        report.AdsWatched++;
                    }
                SpendPerks();
                report.DayLines.Add(DayLine(day));
            }
            report.Days = days;
            report.CrewHired = session.CrewHiredCount;
            return report;
        }

        // --- One session ---

        void PlaySession(double start, double seconds, bool first)
        {
            now = start;
            SyncClock();
            if (!first)
                Dollars("offline", ReturnFromAway);
            Dollars("daily", DailyChores);

            double end = start + seconds;
            while (now < end)
            {
                now += Step;
                SyncClock();
                Dollars("swipe", () => Swipe(Step));
                Dollars("idle (open)", () => session.TickIdle(Step));
                session.TickPlay(Step);
                Floaters();
                session.TickCandidates(Step);
                session.TickShop(Step);
                if (session.MotherLodeDue)
                    Dollars("Mother Lode", RunMotherLode);
                Dollars("chests", Chests);
                Dollars("crates", Crates);
                Spend();
                Gems("goal", () => session.ClaimGoal());
                if (WantsRebirth())
                {
                    session.Rebirth();
                    report.RebirthDays.Add(DayOf(now));
                }
            }
            DailyChores();
            leftUtc = now;
        }

        bool WantsRebirth() => profile.Rebirth == RebirthPolicy.Suggested && session.RebirthSuggested;

        void Dollars(string source, Action action)
        {
            BigNumber before = session.Progress.TotalEarned;
            action();
            if (session.Progress.Rebirths > 0)
                return;
            double gained = (session.Progress.TotalEarned - before).ToDouble();
            report.FirstClimbDollars.TryGetValue(source, out double total);
            report.FirstClimbDollars[source] = total + gained;
        }

        void SyncClock()
        {
            session.NowUtc = now;
            long day = GameSession.DayNumber(now, 0, config.DailyRolloverHour);
            session.UpdateDay(day);
        }

        void ReturnFromAway()
        {
            var clock = new OfflineClockInput
            {
                LastSeenUtc = leftUtc,
                TrustedNowUtc = now,
                LastDeviceUtc = leftUtc,
                DeviceNowUtc = now,
                LastMonotonicSeconds = 1000,
                MonotonicNowSeconds = 1000 + (now - leftUtc),
                SameBoot = true,
            };
            OfflineResult result = session.EvaluateOffline(clock);
            if (!result.IsPayable)
                return;
            bool modal = result.ElapsedSeconds >= config.OfflineModalMinAwaySeconds;
            int multiplier = modal && profile.WatchesAds ? 2 : 1;
            if (multiplier == 2)
                report.AdsWatched++;
            session.ClaimOffline(result, multiplier);
            double rate = session.OfflineRate.ToDouble();
            if (rate > 0)
                report.OfflineSecondsCredited += result.Amount.ToDouble() / rate;
            if (modal)
                session.GiveReturnGift();
        }

        void DailyChores()
        {
            if (!profile.DoesDaily || !session.IsUnlocked(Feature.Daily))
                return;
            if (profile.WatchesAds && session.CanRescueStreak)
            {
                session.RescueStreak();
                report.AdsWatched++;
            }
            Gems("streak", () => session.ClaimStreak().HasValue);
            while (session.CanWashFree || (profile.WatchesAds && session.CanWashWithAd))
            {
                bool ad = !session.CanWashFree;
                if (ad)
                    report.AdsWatched++;
                Gems(ad ? "wash (ad)" : "wash (free)", () => session.Wash(ad) >= 0);
            }
            for (int slot = 0; slot < session.JobCount; slot++)
            {
                int index = slot;
                Gems("daily jobs", () => session.ClaimJob(index));
            }
        }

        // --- Swiping ---

        void Swipe(double dt)
        {
            spawnIn -= dt;
            while (spawnIn <= 0)
            {
                double rate = session.SpawnRate;
                spawnIn += 1 / rate;
                if (session.TakeBoughtGiant())
                    Catch(CollectibleKind.GiantNugget);
                CollectibleKind kind = session.RollKind(rng.NextDouble());
                if (rng.NextDouble() < profile.CatchRate)
                    Catch(kind);
                else
                    session.LoseCollectible();
            }
        }

        void Catch(CollectibleKind kind)
        {
            bool doubleCatch = session.RollDoubleCatch(rng.NextDouble());
            bool critical = session.RollCritical(rng.NextDouble());
            session.Collect(kind, doubleCatch, critical);
            if (kind != CollectibleKind.GoldDust)
                session.CatchNugget(session.RollNuggetType(rng.NextDouble()));
        }

        void RunMotherLode()
        {
            MotherLodeRun run = session.StartMotherLode();
            double hitEvery = 1 / profile.LodeHitsPerSecond;
            double sinceHit = 0;
            while (!run.IsOver)
            {
                run.Tick(Step);
                sinceHit += Step;
                while (sinceHit >= hitEvery)
                {
                    run.Hit();
                    sinceHit -= hitEvery;
                }
            }
            Gems("Mother Lode", () => !session.FinishMotherLode(run).IsZero);
            report.MotherLodes++;
            if (run.BossBeaten && double.IsNaN(report.BossDays[run.BossRegion]))
            {
                report.BossDays[run.BossRegion] = DayOf(now);
                report.BossRebirths[run.BossRegion] = session.Progress.Rebirths;
            }
        }

        void Chests()
        {
            if (session.ChestReady)
            {
                ChestReward reward = null;
                Gems("chest duplicates", () => (reward = session.ClaimChest()) != null);
                report.ChestsOpened++;
                if (profile.WatchesAds && reward != null)
                {
                    session.DoubleChest(reward);
                    report.AdsWatched++;
                }
            }
            if (session.Progress.ChestsWaiting > 0 && !session.Progress.ChestOpening)
                session.StartChest();
        }

        /// <summary>A floater on the water is caught or missed at once; the crossing time is not modelled.</summary>
        void Floaters()
        {
            if (session.LaunchFloater() == null)
                return;
            if (rng.NextDouble() < profile.FloaterCatchRate)
            {
                session.CatchFloater();
                return;
            }
            session.MissFloater();
            report.FloatersMissed++;
        }

        void Crates()
        {
            while (session.Progress.CratesWaiting > 0)
            {
                CrateReward crate = session.ClaimCrate();
                report.CratesOpened++;
                if (profile.WatchesAds)
                {
                    session.DoubleCrate(crate);
                    report.AdsWatched++;
                }
            }
            while (session.Progress.PouchesWaiting > 0)
            {
                if (!profile.WatchesAds)
                {
                    session.DiscardPouch();
                    continue;
                }
                report.AdsWatched++;
                report.PouchesOpened++;
                Gems("gem pouch (ad)", () => session.OpenPouch() > 0);
            }
        }

        // --- Spending ---

        void Spend()
        {
            // Dollars, greedy: next creek, next tier, Amos, then the cheapest upgrade.
            for (int guard = 0; guard < 200; guard++)
            {
                if (session.IsUnlocked(Feature.Map) && session.UnlockNextRegion())
                {
                    int creek = session.Progress.RegionIndex;
                    if (double.IsNaN(report.CreekDays[creek]))
                    {
                        report.CreekDays[creek] = DayOf(now);
                        report.CreekMultipliers[creek] = session.PrestigeMultiplier;
                    }
                    continue;
                }
                if (session.IsUnlocked(Feature.Upgrades) && (session.BuyNextTier() || session.BuyAmosLevel()))
                    continue;
                int cheapest = -1;
                BigNumber? best = null;
                for (int i = 0; i < GameCatalog.Upgrades.Count; i++)
                {
                    if (!session.IsUpgradeUnlocked(i))
                        continue;
                    BigNumber? cost = session.UpgradeCost(i);
                    if (cost.HasValue && (!best.HasValue || cost.Value < best.Value))
                    {
                        best = cost;
                        cheapest = i;
                    }
                }
                if (cheapest < 0 || !session.IsUnlocked(Feature.Upgrades) || !session.BuyUpgrade(cheapest))
                    break;
            }

            // Gems: hire a candidate, then the third gear slot.
            if (session.HasCandidates && profile.HiresCrew)
            {
                int cost = session.CrewHireCost ?? 0;
                if (session.HireCandidate(session.Candidates[0]))
                    report.SpendGems("crew hire", cost);
            }
            if (session.GearUnlocked && session.CrewHiredCount >= 2)
            {
                int? slot = session.ThirdGearSlotCost;
                if (session.BuyThirdGearSlot())
                    report.SpendGems("third gear slot", slot ?? 0);
            }
        }

        void SpendPerks()
        {
            for (int guard = 0; guard < 100; guard++)
            {
                bool bought = false;
                for (int i = 0; i < GameCatalog.Perks.Count; i++)
                    if (session.RankUpPerk(i))
                        bought = true;
                if (!bought)
                    break;
            }
        }

        void Gems(string source, Func<bool> action)
        {
            int before = session.Progress.Gems;
            action();
            report.AddGems(source, session.Progress.Gems - before);
        }

        string DayLine(int day)
        {
            PlayerProgress p = session.Progress;
            string vsModel = "";
            if (p.Rebirths == rebirthsAtDayStart && p.Rebirths == 0)
            {
                double today = (p.TotalEarned - earnedAtDayStart).ToDouble();
                double model = new PacingModel(session.Economy).DayIncome(Math.Max(0, p.RegionIndex)).ToDouble();
                vsModel = $", earned {NumberFormat.Dollars(today)} = {today / model:0.0}x model day in creek {p.RegionIndex + 1}";
            }
            earnedAtDayStart = p.TotalEarned;
            rebirthsAtDayStart = p.Rebirths;
            return $"day {day + 1,2}: creek {p.RegionIndex + 1} (best {p.BestRegionsUnlocked}), tier {p.TierIndex + 1}, " +
                   $"Amos L{p.AmosLevel} cap {session.OfflineCapSeconds / 3600:0.#}h, crew {session.CrewHiredCount}, rebirths {p.Rebirths}, Guild {p.GuildLevel}, " +
                   $"x{session.PrestigeMultiplier:0.00}, {NumberFormat.Dollars(p.Dollars)}, {p.Gems} Gems{vsModel}";
        }
    }
}
