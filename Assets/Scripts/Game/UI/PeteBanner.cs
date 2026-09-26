using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Old Pete's speech band above the bottom bar (design doc 9.1, 14.4): one dry line at a
    /// time, each shown once, tap to dismiss. Pete never sells anything.
    /// </summary>
    public sealed class PeteBanner
    {
        readonly GameSession session;
        readonly Button root;
        readonly Text text;
        PeteLine? showing;

        public PeteBanner(GameSession session, Transform canvas, float bottom)
        {
            this.session = session;
            root = Ui.Button("Pete", canvas, "", Palette.Amos, Dismiss, out text, 34);
            root.AsRect().Box(Vector2.zero, new Vector2(620, 170), new Vector2(20, bottom + 20));
            text.alignment = TextAnchor.MiddleLeft;
            text.fontStyle = FontStyle.Normal;
            text.rectTransform.Fill(20);
            root.SetActive(false);
        }

        /// <summary>Shows the next due line unless a menu or the Mother Lode has the screen.</summary>
        public void Refresh(bool screenBusy)
        {
            if (screenBusy)
            {
                root.SetActive(false);
                return;
            }
            // Re-read every frame: a line whose moment passed before the tap goes away by itself.
            showing = session.NextPeteLine();
            root.SetActive(showing.HasValue);
            if (showing.HasValue)
                text.SetText("Old Pete: " + Line(showing.Value));
        }

        void Dismiss()
        {
            if (showing.HasValue)
                session.MarkSeen(showing.Value);
            showing = null;
            root.SetActive(false);
        }

        public static string Line(PeteLine line)
        {
            switch (line)
            {
                case PeteLine.WelcomeBack: return "Welcome back. Amos found this while you were gone.";
                case PeteLine.Welcome: return "Something's glinting in the water, kid. Run your finger across it.";
                case PeteLine.FirstDust: return "That's gold dust. Keep at it and the creek pays.";
                case PeteLine.UpgradesOpen: return "Got a few dollars. A better shovel wouldn't hurt.";
                case PeteLine.AmosForHire: return "Amos is looking for work. He pans even when you don't.";
                case PeteLine.FirstChest: return "A chest washed up. Open it.";
                case PeteLine.MapOpen: return "Pine Hollow's upstream. Richer water, if you can pay the claim.";
                case PeteLine.FreeHire: return "Two drifters want work. Pick one. First one's on me.";
                case PeteLine.DailyOpen: return "Come by every day. The creek rewards regulars.";
                case PeteLine.GuildOpen: return "The Prospectors' Guild has noticed you. Take a look.";
                default: return "";
            }
        }
    }
}
