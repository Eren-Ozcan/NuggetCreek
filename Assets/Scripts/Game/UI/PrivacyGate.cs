using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// First-launch screen (design doc 8.6, 9, 14.2): a neutral age question, then Accept or
    /// Read Policy. Accept stays off until an age is picked, and no answer is preselected, so
    /// the screen does not nudge a child toward "older".
    /// </summary>
    public sealed class PrivacyGate
    {
        readonly RectTransform root;
        readonly Button accept;
        readonly Button[] ages = new Button[3];
        static readonly AgeBand[] Bands = { AgeBand.Under13, AgeBand.Teen, AgeBand.Adult };
        AgeBand picked;

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>Raised with the age answer when the player accepts.</summary>
        public event Action<AgeBand> Accepted;

        public PrivacyGate(Transform canvas)
        {
            root = Ui.Image("PrivacyGate", canvas, Palette.Background).rectTransform.Fill();
            RectTransform card = Ui.Panel("Card", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(960, 1240));

            Text title = Ui.Title("Title", card, "Welcome to Nugget Creek", 56);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -150), new Vector2(-40, -40));

            Text question = Ui.Label("Question", card, "How old are you?", 42, TextAnchor.MiddleCenter, Palette.Text);
            question.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -260), new Vector2(-40, -170));
            string[] texts = { "Under 13", "13 - 17", "18 or older" };
            string[] names = { "AgeUnder13", "AgeTeen", "AgeAdult" };
            for (int i = 0; i < ages.Length; i++)
            {
                int index = i;
                ages[i] = Ui.Button(names[i], card, texts[i], Palette.ButtonAlt, () => Pick(index), out _, 36);
                ages[i].AsRect().Place(new Vector2(i / 3f, 1), new Vector2((i + 1) / 3f, 1),
                    new Vector2(i == 0 ? 40 : 10, -400), new Vector2(i == 2 ? -40 : -10, -280));
            }

            Text body = Ui.Label("Body", card,
                "We save your progress on this phone. Analytics, crash reports and ads keep the game free " +
                "and help us fix it. Ad choices can be changed any time in Settings.\n\n" +
                "Tap Accept to agree to our Privacy Policy.",
                34, TextAnchor.UpperLeft, Palette.TextMuted);
            body.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(50, 200), new Vector2(-50, -440));

            Ui.Button("ReadPolicy", card, "Read Policy", Palette.ButtonAlt, () => Application.OpenURL(Compliance.PrivacyPolicyUrl), out _, 40)
                .AsRect().Place(Vector2.zero, new Vector2(0.5f, 0), new Vector2(40, 40), new Vector2(-15, 160));
            accept = Ui.Button("GateAccept", card, "Accept", Palette.Button, Accept, out _, 40);
            accept.AsRect().Place(new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(15, 40), new Vector2(-40, 160));
            accept.interactable = false;
            root.SetActive(false);
        }

        public void Open()
        {
            picked = AgeBand.Unknown;
            accept.interactable = false;
            for (int i = 0; i < ages.Length; i++)
                ages[i].image.color = Palette.ButtonAlt;
            root.SetAsLastSibling();
            root.SetActive(true);
        }

        /// <summary>Closes without an answer (the debug "Skip intro"); nothing is saved.</summary>
        public void Dismiss() => root.SetActive(false);

        void Pick(int index)
        {
            picked = Bands[index];
            for (int i = 0; i < ages.Length; i++)
                ages[i].image.color = i == index ? Palette.Button : Palette.ButtonAlt;
            accept.interactable = true;
        }

        void Accept()
        {
            if (picked == AgeBand.Unknown)
                return;
            root.SetActive(false);
            Accepted?.Invoke(picked);
        }
    }
}
