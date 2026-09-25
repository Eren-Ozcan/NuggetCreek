using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using NuggetCreek.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace NuggetCreek.PlayModeTests
{
    /// <summary>
    /// Drives the greybox scene through its main loop by pressing real buttons: spawns,
    /// hiring Amos, idle income, and the offline return with trusted time. Swipe input is
    /// covered by the core tests (GameSession.Collect); these check the wiring.
    /// Set NC_SHOT_DIR to also save screenshots of each step.
    /// </summary>
    public class GreyboxSmokeTests
    {
        [UnitySetUp]
        public IEnumerator LoadFreshGame()
        {
            SaveStore.Delete();
            yield return SceneManager.LoadSceneAsync("Creek");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator ClearSave()
        {
            SaveStore.Delete();
            yield return null;
        }

        [UnityTest]
        public IEnumerator FreshGameSpawnsGoldAndHiresAmos()
        {
            Assert.That(Label("Dollars").text, Is.EqualTo("$0"));
            Assert.That(Label("Goal").text, Does.Contain("swipe up 15 Gold Dust by hand  0/15"));
            Assert.That(Label("Gems").text, Is.EqualTo("0 Gems"));

            yield return WaitFor(() => GameObject.Find("GoldDust") != null, 3);
            yield return Shot("1_creek_fresh");

            Click("+$1K");
            yield return null;
            Assert.That(Label("Dollars").text, Is.EqualTo("$1K"));

            Click("UpgradesButton");
            yield return null;
            Assert.That(GameObject.Find("Upgrades"), Is.Not.Null);
            yield return Shot("2_upgrades");

            ClickIn("Amos", "Buy");
            ClickIn("sturdy_shovel", "Buy");
            yield return null;
            Assert.That(Label("Dollars").text, Is.EqualTo("$900"));
            StringAssert.StartsWith("Sturdy Shovel  1/10", LabelIn("sturdy_shovel", "Title").text);

            Click("Close");
            yield return new WaitForSeconds(2.5f);
            StringAssert.Contains("Amos +$", Label("Status").text);
            Assert.That(GameObject.Find("AmosMarker"), Is.Not.Null);
            yield return Shot("3_creek_with_amos");
        }

        [UnityTest]
        public IEnumerator UnlockingPineHollowOpensTierTwo()
        {
            Click("+$1K");
            Click("MapButton");
            yield return null;
            yield return Shot("4_map");
            ClickIn("Pine Hollow", "Action");
            yield return null;

            StringAssert.StartsWith("Pine Hollow", Label("Status").text);
            Assert.That(Label("Dollars").text, Is.EqualTo("$30"));
            Assert.That(GameObject.Find("Map"), Is.Null, "map closes after unlocking");
        }

        [UnityTest]
        public IEnumerator SilverForkOffersCrewForGems()
        {
            Click("+$1M");
            Click("MapButton");
            yield return null;
            ClickIn("Pine Hollow", "Action");
            yield return null;
            Assert.That(IsActive("CandidateModal"), Is.False, "no candidates in Pine Hollow");

            Click("+$1M");
            Click("MapButton");
            yield return null;
            ClickIn("Silver Fork", "Action");
            yield return null;
            Assert.That(IsActive("CandidateModal"), Is.True);
            StringAssert.StartsWith("Pick one. The offer ends in 10:00", LabelIn("CandidateModal", "Timer").text);
            Button hire = Find("CandidateModal").GetComponentsInChildren<Button>().First(b => b.name == "Hire");
            Assert.That(hire.interactable, Is.False, "no Gems yet");

            Click("Later");
            yield return null;
            Assert.That(IsActive("CrewChip"), Is.True);
            Click("+50 Gems");
            Click("CrewChip");
            yield return null;
            yield return Shot("6_crew_candidates");

            string hiredName = LabelIn("CandidateModal", "Name").text;
            ClickIn("CandidateModal", "Hire");
            yield return null;
            Assert.That(IsActive("CandidateModal"), Is.False);
            Assert.That(IsActive("CrewChip"), Is.False);
            Assert.That(Label("Gems").text, Is.EqualTo("40 Gems"));

            Click("UpgradesButton");
            yield return null;
            Text title = Find("Upgrades").GetComponentsInChildren<Text>().First(t => t.name == "Title" && t.text.StartsWith(hiredName + "  Lv"));
            StringAssert.StartsWith(hiredName + "  Lv 1/10", title.text);
            yield return Shot("7_crew_row");
        }

        [UnityTest]
        public IEnumerator MotherLodeRunsAndPays()
        {
            Assert.That(IsActive("SummonLode"), Is.False, "the Gem call waits for the first natural event");
            Click("Lode now");
            yield return null;
            yield return null;
            Assert.That(IsActive("MotherLode"), Is.True);
            StringAssert.StartsWith("x1", LabelIn("MotherLode", "Combo").text);
            yield return Shot("8_mother_lode");

            float scale = Time.timeScale;
            Time.timeScale = 20;
            try
            {
                yield return WaitFor(() => IsActive("LodeCollect"), 5);
            }
            finally
            {
                Time.timeScale = scale;
            }
            yield return Shot("9_mother_lode_result");
            StringAssert.Contains("+1 Gem", LabelIn("Result", "Amount").text);
            Assert.That(Label("Gems").text, Is.EqualTo("1 Gem"));
            Assert.That(Label("Dollars").text, Is.Not.EqualTo("$0"));

            Click("LodeCollect");
            yield return null;
            Assert.That(IsActive("MotherLode"), Is.False);
            Assert.That(IsActive("SummonLode"), Is.True);
        }

        [UnityTest]
        public IEnumerator CollectionLayersStayClosedUntilOpened()
        {
            yield return null;
            Assert.That(IsActive("Vein"), Is.False, "the vein is closed before perks");

            Click("Layers");
            yield return null;
            Assert.That(IsActive("Vein"), Is.True);
            StringAssert.StartsWith("VEIN x1.0  0/20", Label("Vein").text);
            yield return Shot("10_collection_layers");
        }

        [UnityTest]
        public IEnumerator OfflineReturnPaysAfterTrustedTime()
        {
            Click("+$1K");
            Click("UpgradesButton");
            yield return null;
            ClickIn("Amos", "Buy");
            Click("Close");
            yield return null;

            Click("Away 2h");
            yield return null;
            // Needs the network for trusted time; the modal must not offer Claim before it.
            yield return WaitFor(() => IsActive("Claim"), 15);
            yield return Shot("5_offline_modal");

            StringAssert.Contains("You were away 2h", LabelIn("Card", "Body").text);
            StringAssert.Contains("Amos stopped at 1h", LabelIn("Card", "CapLine").text);
            Assert.That(IsActive("UpgradeAmos"), Is.True);

            string before = Label("Dollars").text;
            Click("Claim");
            yield return null;
            Assert.That(GameObject.Find("OfflineModal"), Is.Null);
            Assert.That(Label("Dollars").text, Is.Not.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator SaveSurvivesReload()
        {
            Click("+$1M");
            Click("UpgradesButton");
            yield return null;
            ClickIn("Amos", "Buy");
            Click("Close");
            // The autosave also runs on quit and pause; force one through a scene reload path.
            SendSave();
            yield return SceneManager.LoadSceneAsync("Creek");
            yield return null;

            StringAssert.StartsWith("$999", Label("Dollars").text);
            StringAssert.Contains("Amos +$", Label("Status").text);
        }

        // --- helpers ---

        static void SendSave() =>
            UnityEngine.Object.FindAnyObjectByType<GameRoot>().SendMessage("Save");

        static GameObject Find(string name)
        {
            GameObject go = GameObject.Find(name);
            Assert.That(go, Is.Not.Null, $"'{name}' not found");
            return go;
        }

        static bool IsActive(string name) => GameObject.Find(name) != null;

        static Text Label(string name) => Find(name).GetComponent<Text>();

        static Text LabelIn(string parent, string child) =>
            Find(parent).GetComponentsInChildren<Text>(true).First(t => t.name == child);

        static void Click(string name)
        {
            var button = Find(name).GetComponent<Button>();
            Assert.That(button.interactable, Is.True, $"'{name}' is not interactable");
            button.onClick.Invoke();
        }

        static void ClickIn(string parent, string child)
        {
            Button button = Find(parent).GetComponentsInChildren<Button>(true).First(b => b.name == child);
            Assert.That(button.interactable, Is.True, $"'{parent}/{child}' is not interactable");
            button.onClick.Invoke();
        }

        static IEnumerator WaitFor(Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end)
                    Assert.Fail($"Condition not met within {seconds}s");
                yield return null;
            }
        }

        /// <summary>Renders the overlay UI through the camera into a 1080x1920 PNG.</summary>
        static IEnumerator Shot(string name)
        {
            string dir = Environment.GetEnvironmentVariable("NC_SHOT_DIR");
            if (string.IsNullOrEmpty(dir))
                yield break;
            yield return null;

            Camera camera = Camera.main;
            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas).ToArray();
            foreach (Canvas canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
            }

            var target = new RenderTexture(1080, 1920, 24);
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            UnityEngine.Object.Destroy(target);

            foreach (Canvas canvas in canvases)
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
        }
    }
}
