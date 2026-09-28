using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Shown when this phone and the Google Play copy both hold real progress and the cloud one
    /// is further along (design doc 13.1). Neither side is replaced until the player picks.
    /// </summary>
    public sealed class CloudChoiceModal
    {
        readonly RectTransform root;
        readonly Text localLine;
        readonly Text cloudLine;

        public bool IsOpen => root.gameObject.activeSelf;

        public event Action KeepLocal;
        public event Action LoadCloud;

        public CloudChoiceModal(Transform canvas)
        {
            root = Ui.Image("CloudChoice", canvas, Palette.Background).rectTransform.Fill();
            RectTransform card = Ui.Panel("Card", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(960, 1100));

            Text title = Ui.Title("Title", card, "Two saves found", 56);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -150), new Vector2(-40, -40));

            Text intro = Ui.Label("Intro", card,
                "Your Google Play save is further along than the one on this phone. Pick one; the other is replaced.",
                34, TextAnchor.UpperLeft, Palette.TextMuted);
            intro.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(50, -330), new Vector2(-50, -170));

            localLine = Side(card, "Local", "This phone", -350);
            cloudLine = Side(card, "Cloud", "Google Play", -580);

            Ui.Button("KeepLocal", card, "Keep this phone", Palette.ButtonAlt, () => Pick(KeepLocal), out _, 38)
                .AsRect().Place(Vector2.zero, new Vector2(0.5f, 0), new Vector2(40, 40), new Vector2(-15, 170));
            Ui.Button("LoadCloud", card, "Load Google Play", Palette.Button, () => Pick(LoadCloud), out _, 38)
                .AsRect().Place(new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(15, 40), new Vector2(-40, 170));
            root.SetActive(false);
        }

        static Text Side(RectTransform card, string name, string heading, float top)
        {
            RectTransform box = Ui.Panel(name, card, Palette.ButtonAlt).rectTransform
                .Place(new Vector2(0, 1), Vector2.one, new Vector2(40, top - 200), new Vector2(-40, top));
            Text label = Ui.Label("Heading", box, heading, 38, TextAnchor.UpperLeft, Palette.GoldText, FontStyle.Bold);
            label.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(30, 20), new Vector2(-30, -20));
            Text line = Ui.Label("Line", box, "", 32, TextAnchor.LowerLeft, Palette.Text);
            line.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(30, 20), new Vector2(-30, -20));
            return line;
        }

        public void Open(PlayerProgress local, PlayerProgress cloud)
        {
            localLine.SetText(CloudSave.Describe(local));
            cloudLine.SetText(CloudSave.Describe(cloud));
            root.SetAsLastSibling();
            root.SetActive(true);
        }

        void Pick(Action choice)
        {
            root.SetActive(false);
            choice?.Invoke();
        }
    }
}
