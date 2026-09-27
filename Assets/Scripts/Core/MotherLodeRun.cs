using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// One Mother Lode (design doc 3.4): 20 seconds of swiping at a vein. Every few hits
    /// raise the combo from x1 toward x10; a pause drops it back to x1. The payout uses
    /// the highest combo reached and the income captured when the event started.
    /// </summary>
    public sealed class MotherLodeRun
    {
        readonly EconomyConfig config;
        readonly int maxCombo;
        int hitsTowardNext;
        double sinceHit;

        public BigNumber IncomePerSecond { get; }
        public double SecondsLeft { get; private set; }
        public int Hits { get; private set; }
        public int Combo { get; private set; } = 1;
        public int PeakCombo { get; private set; } = 1;
        public bool IsPaid { get; private set; }

        /// <summary>Started for Gems rather than by the play counters (mother_lode.trigger).</summary>
        public bool Summoned { get; set; }

        /// <summary>Creek whose boss this run fights; -1 for a plain Mother Lode (design doc 3.4.1).</summary>
        public int BossRegion { get; private set; } = -1;

        public double BossHealth { get; private set; }

        public double BossDamage { get; private set; }

        /// <summary>Damage of one hit at combo x1; each hit deals this times the combo.</summary>
        public double HitDamage { get; private set; }

        public bool IsBossFight => BossRegion >= 0;

        public bool BossBeaten => IsBossFight && BossDamage >= BossHealth;

        /// <summary>Boss nugget this run dropped once paid; -1 for none.</summary>
        public int DroppedNugget { get; internal set; } = -1;

        public bool IsOver => SecondsLeft <= 0;

        public int MaxCombo => maxCombo;

        /// <param name="maxCombo">Combo ceiling; 0 uses the config default.</param>
        public MotherLodeRun(EconomyConfig config, BigNumber incomePerSecond, int maxCombo = 0)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.maxCombo = maxCombo > 0 ? maxCombo : config.MotherLodeMaxCombo;
            IncomePerSecond = incomePerSecond;
            SecondsLeft = config.MotherLodeDurationSeconds;
        }

        /// <summary>Turns the run into a boss fight.</summary>
        public void SetBoss(int regionIndex, double health, double hitDamage)
        {
            BossRegion = regionIndex;
            BossHealth = health;
            HitDamage = hitDamage;
        }

        public void Tick(double deltaSeconds)
        {
            if (IsOver || deltaSeconds <= 0)
                return;
            SecondsLeft = Math.Max(0, SecondsLeft - deltaSeconds);
            sinceHit += deltaSeconds;
            if (sinceHit > config.MotherLodeComboHoldSeconds)
            {
                Combo = 1;
                hitsTowardNext = 0;
            }
        }

        public void Hit()
        {
            if (IsOver)
                return;
            Hits++;
            sinceHit = 0;
            if (Combo < maxCombo && ++hitsTowardNext >= config.MotherLodeHitsPerCombo)
            {
                hitsTowardNext = 0;
                Combo++;
                PeakCombo = Math.Max(PeakCombo, Combo);
            }
            if (IsBossFight)
            {
                BossDamage += HitDamage * Combo;
                // A beaten boss ends the fight at once.
                if (BossBeaten)
                    SecondsLeft = 0;
            }
        }

        /// <summary>Ends the run now; called when it is paid.</summary>
        internal void End()
        {
            SecondsLeft = 0;
            IsPaid = true;
        }
    }
}
