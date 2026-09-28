using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Settings (design doc 14.2, 14.3, 15.5): music and effects, vibration, high contrast, the ad consent choices
    /// where the law asks for them, the Google Play cloud save, the privacy policy and "Delete my data".
    /// </summary>
    public sealed class SettingsPanel
    {
        readonly PlayerProgress progress;
        readonly RectTransform root;
        readonly Text musicLabel;
        readonly Text soundLabel;
        readonly Text vibrationLabel;
        readonly Text contrastLabel;
        readonly Button privacyChoices;
        readonly Button deleteButton;
        readonly Text deleteLabel;
        readonly Button cancelDelete;
        readonly Func<bool> privacyChoicesRequired;
        readonly CloudSync cloud;
        readonly Button cloudButton;
        readonly Text cloudLabel;
        bool confirmingDelete;

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>A setting changed; save it.</summary>
        public event Action Changed;

        /// <summary>The ad consent form should reopen.</summary>
        public event Action PrivacyChoicesRequested;

        /// <summary>The player confirmed "Delete my data".</summary>
        public event Action DeleteConfirmed;

        public SettingsPanel(PlayerProgress progress, Func<bool> privacyChoicesRequired, CloudSync cloud, Transform canvas)
        {
            this.progress = progress;
            this.privacyChoicesRequired = privacyChoicesRequired;
            this.cloud = cloud;
            cloud.Changed += Refresh;
            root = Ui.Image("Settings", canvas, Palette.Panel).rectTransform.Fill();

            Text title = Ui.Title("Title", root, "SETTINGS", 52);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);
            Ui.CloseButton(root, Close);

            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -160));
            RectTransform list = Ui.ScrollList(body, 16, 24);

            Section(list, "SOUND");
            Ui.PreferredHeight(Ui.Button("Music", list, "", Palette.ButtonAlt, ToggleMusic, out musicLabel, 38), 120);
            Ui.PreferredHeight(Ui.Button("Effects", list, "", Palette.ButtonAlt, ToggleSound, out soundLabel, 38), 120);

            Section(list, "ACCESSIBILITY");
            Ui.PreferredHeight(Ui.Button("Vibration", list, "", Palette.ButtonAlt, CycleVibration, out vibrationLabel, 38), 120);
            Ui.PreferredHeight(Ui.Button("HighContrast", list, "", Palette.ButtonAlt, ToggleContrast, out contrastLabel, 38), 120);
            Text textSize = Ui.Label("TextSize", list,
                $"Text size follows your phone's font setting, up to 130% (now {Mathf.RoundToInt(Ui.TextScale * 100)}%).",
                30, TextAnchor.MiddleLeft, Palette.TextMuted);
            Ui.PreferredHeight(textSize, 100);

            Section(list, "SAVE").SetActive(cloud.Available);
            cloudButton = Ui.Button("CloudSave", list, "", Palette.ButtonAlt, OnCloud, out cloudLabel, 38);
            Ui.PreferredHeight(cloudButton, 120);
            cloudButton.SetActive(cloud.Available);

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

        static Text Section(Transform list, string text)
        {
            Text label = Ui.Label("Section", list, text, 34, TextAnchor.LowerLeft, Palette.GoldText, FontStyle.Bold);
            Ui.PreferredHeight(label, 70);
            return label;
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
            musicLabel.SetText("Music: " + (progress.MusicOn ? "On" : "Off"));
            soundLabel.SetText("Sound effects: " + (progress.SoundOn ? "On" : "Off"));
            vibrationLabel.SetText("Vibration: " + VibrationText(progress.Vibration));
            contrastLabel.SetText("High contrast: " + (progress.HighContrast ? "On" : "Off"));
            privacyChoices.SetActive(privacyChoicesRequired());
            cloudLabel.SetText(CloudText(cloud.Current));
        }

        static string CloudText(CloudSync.Status status)
        {
            switch (status)
            {
                case CloudSync.Status.Ready:
                    return "Google Play save: On";
                case CloudSync.Status.Checking:
                case CloudSync.Status.Choosing:
                    return "Google Play save: Checking...";
                case CloudSync.Status.Failed:
                    return "Google Play save: Tap to retry";
                default:
                    return "Google Play save: Sign in";
            }
        }

        void OnCloud()
        {
            if (cloud.Current == CloudSync.Status.Off || cloud.Current == CloudSync.Status.Failed)
                cloud.Start(true);
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

        void ToggleMusic()
        {
            progress.MusicOn = !progress.MusicOn;
            Sound.MusicOn = progress.MusicOn;
            Refresh();
            Changed?.Invoke();
        }

        void ToggleSound()
        {
            progress.SoundOn = !progress.SoundOn;
            Sound.SoundOn = progress.SoundOn;
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
