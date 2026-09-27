using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class SaveMigrationTests
    {
        static int IndexOf(string id)
        {
            for (int i = 0; i < GameCatalog.Nuggets.Count; i++)
                if (GameCatalog.Nuggets[i].Id == id)
                    return i;
            return -1;
        }

        [Test]
        public void VersionOneCreeksMoveToTheirOddCreek()
        {
            var progress = new PlayerProgress { RegionIndex = 2, RegionsUnlocked = 4, BestRegionsUnlocked = 5 };
            Assert.That(SaveMigration.Upgrade(progress, 1), Is.True);
            Assert.That(GameCatalog.RegionNames[progress.RegionIndex], Is.EqualTo("Silver Fork"));
            Assert.That(GameCatalog.RegionNames[progress.RegionsUnlocked - 1], Is.EqualTo("Red Gulch"));
            Assert.That(GameCatalog.RegionNames[progress.BestRegionsUnlocked - 1], Is.EqualTo("Frost Basin"));
        }

        [Test]
        public void VersionOneNuggetsKeepTheirCountByIdAndLoseDroppedTypes()
        {
            var old = new int[30];
            old[0] = 12;   // pebble
            old[2] = 7;    // teardrop, not in version 2
            old[4] = 9;    // creek_heart, now a boss nugget
            old[8] = 3;    // owl_eye, now a global rare
            var progress = new PlayerProgress { NuggetCatches = old };

            SaveMigration.Upgrade(progress, 1);

            Assert.That(progress.NuggetCatches.Length, Is.EqualTo(46));
            Assert.That(progress.NuggetCatches[IndexOf("pebble")], Is.EqualTo(12));
            Assert.That(progress.NuggetCatches[IndexOf("owl_eye")], Is.EqualTo(3));
            Assert.That(progress.NuggetCatches[IndexOf("creek_heart")], Is.EqualTo(5), "boss drops stop at the third star");
            int total = 0;
            foreach (int count in progress.NuggetCatches)
                total += count;
            Assert.That(total, Is.EqualTo(12 + 3 + 5));
        }

        [Test]
        public void CurrentSavesAreLeftAlone()
        {
            var progress = new PlayerProgress { RegionIndex = 3, RegionsUnlocked = 4 };
            Assert.That(SaveMigration.Upgrade(progress, SaveMigration.CurrentVersion), Is.False);
            Assert.That(progress.RegionIndex, Is.EqualTo(3));
        }
    }
}
