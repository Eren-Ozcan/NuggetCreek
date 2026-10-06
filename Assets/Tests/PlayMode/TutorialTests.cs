using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using NuggetCreek.Core;
using NuggetCreek.Game;
using NuggetCreek.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static NuggetCreek.PlayModeTests.GreyboxSmokeTests;

namespace NuggetCreek.PlayModeTests
{
    /// <summary>
    /// Old Pete's lessons (design doc 9.1) played through the real overlay: each step waits for
    /// its tap, press or catches, and nothing outside the cued control can be pressed.
    /// Set NC_SHOT_DIR to also save screenshots of each step.
    /// </summary>
    public class TutorialTests
    {
        [UnitySetUp]
        public IEnumerator LoadFreshGame()
        {
            SaveStore.Delete();
            yield return SceneManager.LoadSceneAsync("Creek");
            yield return null;
            Click("AgeAdult");
            Click("GateAccept");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator ClearSave()
        {
            SaveStore.Delete();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheFirstLessonTalksThenWaitsForGold()
        {
            GameSession session = RobustnessTests.Session();
            yield return WaitForLine(Lessons.Of(PeteLine.Welcome)[0].Line);
            Assert.That(Blocked("SettingsButton"), Is.True, "nothing but Pete takes a tap");
            yield return Shot("t0_welcome");

            Tap();
            yield return WaitForLine(Lessons.Of(PeteLine.Welcome)[1].Line);
            yield return Shot("t1_swipe");

            var creek = Object.FindAnyObjectByType<CreekView>();
            Transform water = creek.transform.Find("Gold");
            for (int i = 0; i < 5; i++)
            {
                yield return WaitFor(() => water.childCount > 0, 5);
                var gold = (RectTransform)water.GetChild(water.childCount - 1);
                creek.Sweep(gold.anchoredPosition + new Vector2(-40, 0), gold.anchoredPosition + new Vector2(40, 0));
                yield return null;
            }
            Assert.That(session.HasSeen(PeteLine.Welcome), Is.True);
            yield return WaitForLine(Lessons.Of(PeteLine.FirstDust)[0].Line);
            Tap();
            yield return WaitForLine(Lessons.Of(PeteLine.FirstDust)[1].Line);
            yield return Shot("t2_goal_board");
            Tap();
            yield return WaitFor(() => !IsActive("Tutorial"), 2);
            Assert.That(session.HasSeen(PeteLine.FirstDust), Is.True);
        }

        [UnityTest]
        public IEnumerator TheShovelLessonLeadsThroughTheUpgradesPanel()
        {
            GameSession session = Prepare(PeteLine.UpgradesOpen, p =>
            {
                p.ManualCollected = 15;
                p.Dollars = 40;
            });
            yield return WaitForLine(Lessons.Of(PeteLine.UpgradesOpen)[0].Line);
            Assert.That(Blocked("UpgradesButton"), Is.True, "the goal comes first");
            Assert.That(Blocked("GoalButton"), Is.False);
            yield return Shot("t3_collect_goal");
            Click("GoalButton");

            yield return WaitForLine(Lessons.Of(PeteLine.UpgradesOpen)[1].Line);
            Click("UpgradesButton");
            yield return WaitForLine(Lessons.Of(PeteLine.UpgradesOpen)[2].Line);
            Assert.That(Blocked("Close"), Is.True, "the shovel first");
            yield return Shot("t4_point_shovel");
            ClickIn("sturdy_shovel", "Buy");

            yield return WaitForLine(Lessons.Of(PeteLine.UpgradesOpen)[3].Line);
            yield return Shot("t5_close_upgrades");
            Click("Close");
            yield return WaitFor(() => !IsActive("Tutorial"), 2);
            Assert.That(session.UpgradeLevel(0), Is.EqualTo(1));
            Assert.That(session.HasSeen(PeteLine.UpgradesOpen), Is.True);
        }

        [UnityTest]
        public IEnumerator TheAmosLessonHiresHim()
        {
            GameSession session = Prepare(PeteLine.AmosForHire, p =>
            {
                p.ManualCollected = 40;
                p.Dollars = 1000;
                p.UpgradeLevels[0] = 1;
            });
            yield return WaitForLine(Lessons.Of(PeteLine.AmosForHire)[0].Line);
            Tap();
            yield return WaitForLine(Lessons.Of(PeteLine.AmosForHire)[1].Line);
            Click("UpgradesButton");
            yield return WaitForLine(Lessons.Of(PeteLine.AmosForHire)[2].Line);
            yield return Shot("t6_point_amos");
            ClickIn("Amos", "Buy");
            yield return WaitForLine(Lessons.Of(PeteLine.AmosForHire)[3].Line);
            Click("Close");
            yield return WaitForLine(Lessons.Of(PeteLine.AmosForHire)[4].Line);
            Tap();
            yield return WaitFor(() => !IsActive("Tutorial"), 2);
            Assert.That(session.Progress.AmosLevel, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TheChestLessonHoldsTheChestOnTheWater()
        {
            GameSession session = Prepare(PeteLine.FirstChest, p =>
            {
                p.ManualCollected = 200;
                p.AmosLevel = 1;
                p.UpgradeLevels[0] = 1;
                p.ChestsDue = 1;
            });
            var creek = Object.FindAnyObjectByType<CreekView>();
            yield return WaitForLine(Lessons.Of(PeteLine.FirstChest)[0].Line, 8);
            // It drifts clear of the top controls, then waits there for the player.
            float last = float.MaxValue;
            yield return WaitFor(() =>
            {
                float y = creek.Floaters.Position.Value.y;
                bool still = Mathf.Abs(y - last) < 0.01f;
                last = y;
                return still;
            }, 8);
            Assert.That(last, Is.LessThan(creek.OpenWaterTop), "below the top controls");
            yield return new WaitForSeconds(2);
            Assert.That(creek.Floaters.Position.Value.y, Is.EqualTo(last).Within(0.01f), "the chest waits for the player");
            yield return Shot("t7_point_chest");
            Assert.That(creek.Floaters.Tap(creek.Floaters.Position.Value), Is.True);

            yield return WaitForLine(Lessons.Of(PeteLine.FirstChest)[1].Line);
            yield return Shot("t8_point_tray");
            Click("Tray0");
            yield return WaitFor(() => !IsActive("Tutorial"), 2);
            Assert.That(session.HasSeen(PeteLine.FirstChest), Is.True);
        }

        [UnityTest]
        public IEnumerator TheFreeHireLessonPointsAtTheOffer()
        {
            GameSession session = Prepare(PeteLine.FreeHire, p =>
            {
                p.ManualCollected = 500;
                p.AmosLevel = 1;
                p.UpgradeLevels[0] = 1;
                p.ChestsOpened = 1;
                p.RegionsUnlocked = p.BestRegionsUnlocked = 5;
            });
            session.OfferCandidates();
            yield return WaitForLine(Lessons.Of(PeteLine.FreeHire)[0].Line);
            yield return Shot("t9_point_crew_chip");
            Click("CrewChip");
            yield return WaitForLine(Lessons.Of(PeteLine.FreeHire)[1].Line);
            Assert.That(Blocked("Later"), Is.True, "a candidate must be hired");
            yield return Shot("t10_pick_crew");
            ClickIn("CandidateModal", "Hire");
            yield return WaitForLine(Lessons.Of(PeteLine.FreeHire)[2].Line);
            Tap();
            yield return WaitFor(() => !IsActive("Tutorial"), 2);
            Assert.That(session.CrewHiredCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TheBossLessonOpensTheCollection()
        {
            GameSession session = Prepare(PeteLine.FirstBoss, p =>
            {
                p.ManualCollected = 500;
                p.AmosLevel = 1;
                p.UpgradeLevels[0] = 1;
                p.ChestsOpened = 1;
                p.NuggetCatches[GameCatalog.BossNugget(0)] = 1;
            });
            IReadOnlyList<LessonStep> steps = Lessons.Of(PeteLine.FirstBoss);
            yield return WaitForLine(steps[0].Line);
            yield return Shot("t11_boss_cheer");
            Tap();
            yield return WaitForLine(steps[1].Line);
            Click("CollectionButton");
            yield return WaitForLine(steps[2].Line);
            yield return Shot("t12_boss_card");
            Tap();
            yield return WaitForLine(steps[3].Line);
            Tap();
            yield return WaitForLine(steps[4].Line);
            Click("Close");
            yield return WaitFor(() => !IsActive("Tutorial"), 2);
            Assert.That(session.HasSeen(PeteLine.FirstBoss), Is.True);
        }

        [UnityTest]
        public IEnumerator TheClaimLessonStakesANewClaim()
        {
            int creeks = new EconomyConfig().RegionCount;
            GameSession session = Prepare(PeteLine.ClaimReady, p =>
            {
                p.ManualCollected = 5000;
                p.AmosLevel = 1;
                p.UpgradeLevels[0] = 1;
                p.ChestsOpened = 1;
                p.TotalEarned = 2e13;
                p.RegionsUnlocked = p.BestRegionsUnlocked = creeks;
                p.RegionIndex = creeks - 1;
                p.TierIndex = new EconomyConfig().TierCount - 1;
            });
            IReadOnlyList<LessonStep> steps = Lessons.Of(PeteLine.ClaimReady);
            yield return WaitForLine(steps[0].Line);
            Assert.That(LabelIn("ClaimBar", "Label").text, Is.EqualTo("New claim!"));
            Tap();
            yield return WaitForLine(steps[1].Line);
            yield return Shot("t13_claim_bar");
            Click("GuildButton");
            yield return WaitForLine(steps[2].Line);
            yield return Shot("t14_stake_claim");
            Click("Rebirth");
            yield return WaitForLine(steps[3].Line);
            yield return Shot("t15_claim_card");
            Click("ClaimConfirmYes");
            yield return WaitForLine(steps[4].Line);
            Tap();
            yield return WaitFor(() => !IsActive("Tutorial"), 2);
            Assert.That(session.Progress.Rebirths, Is.EqualTo(1));
            Assert.That(session.HasSeen(PeteLine.ClaimReady), Is.True);
        }

        // --- helpers ---

        /// <summary>Silences the first-launch lesson, sets the save up, then lets only <paramref name="line"/> come due.</summary>
        static GameSession Prepare(PeteLine line, System.Action<PlayerProgress> setUp)
        {
            Click("Skip intro");
            GameSession session = RobustnessTests.Session();
            setUp(session.Progress);
            session.Progress.PeteSeen = ~(1 << (int)line);
            session.IntroSkipped = false;
            return session;
        }

        static void Tap() => Click("TutorialTap");

        static PeteTutorial Tutorial() =>
            (PeteTutorial)typeof(GameRoot).GetField("tutorial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(Object.FindAnyObjectByType<GameRoot>());

        /// <summary>What Pete's speech panel shows, or null while no lesson plays.</summary>
        static string Spoken()
        {
            GameObject tutorial = GameObject.Find("Tutorial");
            Transform text = tutorial != null ? tutorial.transform.Find("Stage/Speech/Text") : null;
            return text != null ? text.GetComponent<Text>().text : null;
        }

        /// <summary>Waits until Pete has typed out <paramref name="line"/> in full.</summary>
        static IEnumerator WaitForLine(string line, float seconds = 4)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Spoken() != line)
            {
                if (Time.realtimeSinceStartup > end)
                {
                    PeteTutorial tutorial = Tutorial();
                    Assert.Fail($"Pete never said \"{line}\"; he says \"{Spoken()}\" in lesson {tutorial.Lesson}, step \"{tutorial.Line}\"");
                }
                yield return null;
            }
        }

        /// <summary>A finger on the control's centre lands on Pete's shade instead.</summary>
        static bool Blocked(string name)
        {
            var rect = (RectTransform)Find(name).transform;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var data = new PointerEventData(EventSystem.current) { position = (corners[0] + corners[2]) / 2 };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, results);
            return results.Count > 0 && results[0].gameObject.name == "TutorialTap";
        }
    }
}
