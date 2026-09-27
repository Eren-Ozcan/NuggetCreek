using System;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Amos's pre-prompt before the OS notification dialog (design doc 10.1.4). "Not now"
    /// never opens the OS dialog, so Android's two asks are not spent on a player who said no.
    /// </summary>
    public sealed class NotifAskCard
    {
        readonly RectTransform root;
        Action<bool> answered;

        public bool IsOpen => root.gameObject.activeSelf;

        public NotifAskCard(Transform canvas)
        {
            root = Ui.Image("NotifAsk", canvas, Palette.Dim).rectTransform.Fill();
            RectTransform card = Ui.Image("Card", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(900, 620));
            Text title = Ui.Label("Title", card, "Amos", 52, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -120), new Vector2(0, -30));
            Text line = Ui.Label("Line", card, "\"I'll keep digging while you're gone. Want me to holler when the pan's full?\"",
                40, TextAnchor.MiddleCenter, Palette.Text);
            line.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(50, 190), new Vector2(-50, -130));
            Ui.Button("NotNow", card, "Not now", Palette.ButtonAlt, () => Answer(false), out _, 40).AsRect()
                .Place(Vector2.zero, new Vector2(0.5f, 0), new Vector2(40, 40), new Vector2(-15, 160));
            Ui.Button("Yes", card, "Yes", Palette.Button, () => Answer(true), out _, 40).AsRect()
                .Place(new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(15, 40), new Vector2(-40, 160));
            root.SetActive(false);
        }

        public void Open(Action<bool> onAnswer)
        {
            answered = onAnswer;
            root.SetAsLastSibling();
            root.SetActive(true);
        }

        void Answer(bool yes)
        {
            root.SetActive(false);
            Action<bool> done = answered;
            answered = null;
            done?.Invoke(yes);
        }
    }
}
