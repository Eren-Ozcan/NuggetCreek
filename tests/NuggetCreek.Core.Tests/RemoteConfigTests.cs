using System.Collections.Generic;
using System.Linq;
using NuggetCreek.Core;
using NUnit.Framework;

namespace NuggetCreek.Core.Tests
{
    public class RemoteConfigTests
    {
        static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);

        [Test]
        public void KeysAreSnakeCaseUnlessNamed()
        {
            var keys = RemoteConfig.Export(new EconomyConfig()).Select(p => p.Key).ToList();
            Assert.That(keys, Does.Contain("spawn_base"));
            Assert.That(keys, Does.Contain("active_idle_ratio"));
            Assert.That(keys, Does.Contain("region_unlock_costs"));
            Assert.That(keys, Does.Contain("tier_mult"));
            Assert.That(keys, Does.Contain("upgrade_growth"));
            Assert.That(keys, Does.Contain("crew_window_s"));
            Assert.That(keys, Does.Contain("rv_load_timeout_s"));
            Assert.That(keys, Does.Contain("interstitial_enabled"));
            Assert.That(keys, Does.Contain("interstitial_cooldown_s"));
            Assert.That(keys, Does.Contain("interstitial_unlock_region"));
            Assert.That(keys, Does.Contain("late_double_window_s"));
            Assert.That(keys, Does.Not.Contain("tier_multiplier"));
        }

        [Test]
        public void KeysAreUniqueAndWithinFirebaseLimits()
        {
            var keys = RemoteConfig.Export(new EconomyConfig()).Select(p => p.Key).ToList();
            Assert.That(keys, Is.Unique);
            Assert.That(keys, Has.All.Matches<string>(k => k.Length <= 256 && char.IsLetter(k[0])));
        }

        [Test]
        public void ExportThenApplyRoundTripsEveryField()
        {
            var source = new EconomyConfig();
            var exported = RemoteConfig.Export(source);
            var target = new EconomyConfig();
            RemoteConfigResult result = RemoteConfig.Apply(target, exported);

            Assert.That(result.Rejected, Is.Empty);
            Assert.That(result.Unknown, Is.Empty);
            Assert.That(result.Applied.Count, Is.EqualTo(exported.Count));
            Assert.That(RemoteConfig.Export(target), Is.EqualTo(exported));
        }

        [Test]
        public void AppliesScalarsAndArrays()
        {
            var config = new EconomyConfig();
            RemoteConfig.Apply(config, new[]
            {
                Pair("spawn_base", "0.95"),
                Pair("tier_mult", "1.7"),
                Pair("chest_capacity", "4"),
                Pair("chest_card_odds", "[0.7, 0.25, 0.05]"),
                Pair("gear_box_guarantee", "[\"Common\",\"rare\",\"Legendary\"]"),
                Pair("rebirth_suggest_min_xp", "30"),
            });

            Assert.That(config.SpawnBase, Is.EqualTo(0.95));
            Assert.That(config.TierMultiplier, Is.EqualTo(1.7));
            Assert.That(config.ChestCapacity, Is.EqualTo(4));
            Assert.That(config.ChestCardOdds, Is.EqualTo(new[] { 0.7, 0.25, 0.05 }));
            Assert.That(config.GearBoxGuarantee, Is.EqualTo(new[] { Rarity.Common, Rarity.Rare, Rarity.Legendary }));
            Assert.That(config.RebirthSuggestMinXp, Is.EqualTo(30));
        }

        [Test]
        public void BadValuesKeepTheDefault()
        {
            var config = new EconomyConfig();
            var defaults = new EconomyConfig();
            RemoteConfigResult result = RemoteConfig.Apply(config, new[]
            {
                Pair("spawn_base", "fast"),
                Pair("spawn_cap", "NaN"),
                Pair("chest_capacity", "3.5"),
                Pair("region_unlock_costs", "[0, 1000]"),
                Pair("chest_card_odds", "0.3,0.1,0.05"),
                Pair("gear_box_guarantee", "[\"Common\",\"Epic\",\"Legendary\"]"),
            });

            Assert.That(result.Applied, Is.Empty);
            Assert.That(result.Rejected.Count, Is.EqualTo(6));
            Assert.That(RemoteConfig.Export(config), Is.EqualTo(RemoteConfig.Export(defaults)));
        }

        [Test]
        public void UnknownKeysAreReportedNotApplied()
        {
            RemoteConfigResult result = RemoteConfig.Apply(new EconomyConfig(), new[] { Pair("banner_enabled", "1") });
            Assert.That(result.Unknown, Is.EqualTo(new[] { "banner_enabled" }));
        }

        [Test]
        public void SerializeRoundTrips()
        {
            var values = new List<KeyValuePair<string, string>> { Pair("spawn_base", "0.9"), Pair("rarity_weights", "[0.3,0.1,0.05]"), Pair("note", "a=b") };
            Assert.That(RemoteConfig.Deserialize(RemoteConfig.Serialize(values)), Is.EqualTo(values));
            Assert.That(RemoteConfig.Deserialize(null), Is.Empty);
        }
    }
}
