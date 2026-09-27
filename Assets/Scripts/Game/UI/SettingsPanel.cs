using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Settings (design doc 14.2, 14.3): vibration, high contrast, the ad consent choices
    /// where the law asks for them, the privacy policy and "Delete my data".
    /// </summary>
    public sealed class SettingsPanel
    {
        readonly PlayerProgress progress;
        readonly RectTransform root;
        readonly Text vibrationLabel;
        readonly Text contrastLabel;
        readonly Button privacyChoices;
        readonly Button deleteButton;
        readonly Text deleteLabel;
        readonly Button cancelDelete;
        readonly Func<bool> privacyChoicesRequired;
        bool confirmingDelete;

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>A setting changed; save it.</summary>
        public event Action Changed;

        /// <summary>The ad consent form should reopen.</summary>
        public event Action PrivacyChoicesRequested;

        /// <summary>The player confirmed "Delete my data".</summary>
        public event Action DeleteConfirmed;

        public SettingsPanel(PlayerProgress progress, Func<bool> privacyChoicesRequired, Transform canvas)
        {
            this.progress = progress;
            this.privacyChoicesRequired = privacyChoicesRequired;
            root = Ui.Image("Settings", canvas, Palette.Panel).rectTransform.Fill();

            Text title = Ui.Label("Title", root, "SETTINGS", 52, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);
            Ui.Button("Close", root, "X", Palette.ButtonAlt, Close, out _).AsRect()
                .Box(Vector2.one, new Vector2(120, 120), new Vector2(-20, -15));

            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -160));
            RectTransform list = Ui.ScrollList(body, 16, 24);

            Section(list, "ACCESSIBILITY");
            Ui.PreferredHeight(Ui.Button("Vibration", list, "", Palette.ButtonAlt, CycleVibration, out vibrationLabel, 38), 120);
            Ui.PreferredHeight(Ui.Button("HighContrast", list, "", Palette.ButtonAlt, ToggleContrast, out contrastLabel, 38), 120);
            Text textSize = Ui.Label("TextSize", list,
                $"Text size follows your phone's font setting, up to 130% (now {Mathf.RoundToInt(Ui.TextScale * 100)}%).",
                30, TextAnchor.MiddleLeft, Palette.TextMuted);
            Ui.PreferredHeight(textSize, 100);

            Section(list, "PRIVACY");
            privacyChoices = Ui.Button("PrivacyChoices", list, "Ad privacy choices", Palette.ButtonAlt,
                () => PrivacyChoicesRequested?.Invoke(), out _, 38);
            Ui.PreferredHeight(privacyChoices, 120);
            Ui.PreferredHeight(Ui.Button("PrivacyPolicy", list, "Privacy Policy", Palette.ButtonAlt,
                () => Application.OpenURL(Compliance.PrivacyPolicyUrl), out _, 38), 120);
            deleteButton = Ui.Button("DeleteData", list, "", Palette.Critical, OnDelete, out deleteLabel, 34);
            Ui.PreferredHeight(deleteButton, 150);
            cancelDelete = Ui.Button("CancelDelete", list, "Keep my progress", Palette.Button, () => SetConfirming(false), out _, 38);
            Ui.PreferredHeight(cancelDelete, 120);

            Text version = Ui.Label("Version", list, $"Nugget Creek {Application.version}", 28, TextAnchor.MiddleCenter, Palette.TextMuted);
            Ui.PreferredHeight(version, 80);
            root.SetActive(false);
        }

        static void Section(Transform list, string text)
        {
            Text label = Ui.Label("Section", list, text, 34, TextAnchor.LowerLeft, Palette.Gold, FontStyle.Bold);
            Ui.PreferredHeight(label, 70);
        }

        public void Open()
        {
            SetConfirming(false);
            root.SetAsLastSibling();
            root.SetActive(true);
            Refresh();
        }

        public void Close() => root.SetActive(false);

        public void Refresh()
        {
            if (!IsOpen)
                return;
            vibrationLabel.SetText("Vibration: " + VibrationText(progress.Vibration));
            contrastLabel.SetText("High contrast: " + (progress.HighContrast ? "On" : "Off"));
            privacyChoices.SetActive(privacyChoicesRequired());
        }

        static string VibrationText(VibrationMode mode)
        {
            switch (mode)
            {
                case VibrationMode.Off:
                    return "Off";
                case VibrationMode.Important:
                    return "Important only";
                default:
                    return "On";
            }
        }

        void CycleVibration()
        {
            progress.Vibration = progress.Vibration == VibrationMode.All ? VibrationMode.Important
                : progress.Vibration == VibrationMode.Important ? VibrationMode.Off : VibrationMode.All;
            Haptics.Mode = progress.Vibration;
            Haptics.Important();
            Refresh();
            Changed?.Invoke();
        }

        void ToggleContrast()
        {
            progress.HighContrast = !progress.HighContrast;
            Refresh();
            Changed?.Invoke();
        }

        void OnDelete()
        {
            if (!confirmingDelete)
            {
                SetConfirming(true);
                return;
            }
            SetConfirming(false);
            DeleteConfirmed?.Invoke();
        }

        void SetConfirming(bool confirming)
        {
            confirmingDelete = confirming;
            deleteLabel.SetText(confirming
                ? "Tap again: this deletes your progress\nand analytics data. It cannot be undone."
                : "Delete my data");
            cancelDelete.SetActive(confirming);
        }
    }
}
