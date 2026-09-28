using System;
using System.Text;
using NuggetCreek.Core;
using UnityEngine;
#if UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.SavedGame;
#endif

namespace NuggetCreek.Game
{
    /// <summary>One cloud save slot in the player's own account.</summary>
    public interface ICloudSlot
    {
        /// <summary>The platform is set up for this build (a Play Games app ID exists).</summary>
        bool Configured { get; }

        /// <param name="interactive">False for the silent sign-in at launch; true when the player tapped.</param>
        void SignIn(bool interactive, Action<bool> done);

        /// <summary>Reads the slot: ok, then the JSON or null when the slot is empty; the error code when not ok.</summary>
        void Read(Action<bool, string, string> done);

        void Write(string json, TimeSpan played, string description, Action<bool, string> done);

        void Delete(Action<bool, string> done);
    }

    /// <summary>No cloud: the editor, iOS until its port, and builds without a Play Games app ID.</summary>
    public sealed class NoCloudSlot : ICloudSlot
    {
        public bool Configured => false;
        public void SignIn(bool interactive, Action<bool> done) => done(false);
        public void Read(Action<bool, string, string> done) => done(false, null, "off");
        public void Write(string json, TimeSpan played, string description, Action<bool, string> done) => done(false, "off");
        public void Delete(Action<bool, string> done) => done(false, "off");
    }

#if UNITY_ANDROID
    /// <summary>Play Games Saved Games: the copy lives in the player's Google account, not with us.</summary>
    public sealed class PlayGamesSlot : ICloudSlot
    {
        bool activated;

        public bool Configured
        {
            get
            {
                PlayGamesSettings settings = PlayGamesSettings.LoadInstance();
                return settings != null && !string.IsNullOrEmpty(settings.AppId);
            }
        }

        public void SignIn(bool interactive, Action<bool> done)
        {
            if (!activated)
            {
                PlayGamesPlatform.Activate();
                activated = true;
            }
            Action<SignInStatus> finished = status => done(status == SignInStatus.Success);
            if (interactive)
                PlayGamesPlatform.Instance.ManuallyAuthenticate(finished);
            else
                PlayGamesPlatform.Instance.Authenticate(finished);
        }

        static void Open(DataSource source, Action<SavedGameRequestStatus, ISavedGameMetadata> done)
        {
            // Play time only grows, so the longer copy is the one further along.
            PlayGamesPlatform.Instance.SavedGame.OpenWithAutomaticConflictResolution(CloudSave.SlotName, source,
                ConflictResolutionStrategy.UseLongestPlaytime, done);
        }

        public void Read(Action<bool, string, string> done)
        {
            Open(DataSource.ReadNetworkOnly, (status, metadata) =>
            {
                if (status != SavedGameRequestStatus.Success)
                {
                    done(false, null, status.ToString());
                    return;
                }
                PlayGamesPlatform.Instance.SavedGame.ReadBinaryData(metadata, (readStatus, bytes) =>
                {
                    if (readStatus != SavedGameRequestStatus.Success)
                        done(false, null, readStatus.ToString());
                    else
                        done(true, bytes == null || bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes), null);
                });
            });
        }

        public void Write(string json, TimeSpan played, string description, Action<bool, string> done)
        {
            Open(DataSource.ReadCacheOrNetwork, (status, metadata) =>
            {
                if (status != SavedGameRequestStatus.Success)
                {
                    done(false, status.ToString());
                    return;
                }
                SavedGameMetadataUpdate update = new SavedGameMetadataUpdate.Builder()
                    .WithUpdatedPlayedTime(played)
                    .WithUpdatedDescription(description)
                    .Build();
                PlayGamesPlatform.Instance.SavedGame.CommitUpdate(metadata, update, Encoding.UTF8.GetBytes(json),
                    (commitStatus, _) => done(commitStatus == SavedGameRequestStatus.Success, commitStatus.ToString()));
            });
        }

        public void Delete(Action<bool, string> done)
        {
            Open(DataSource.ReadNetworkOnly, (status, metadata) =>
            {
                if (status != SavedGameRequestStatus.Success)
                {
                    done(false, status.ToString());
                    return;
                }
                PlayGamesPlatform.Instance.SavedGame.Delete(metadata);
                done(true, null);
            });
        }
    }
#endif

    /// <summary>
    /// Keeps the local save and the cloud slot in step (design doc 13.1 rule 4). Nothing is
    /// uploaded until the cloud copy has been read and settled, so a new phone never overwrites
    /// progress it has not seen. A "Delete my data" that could not reach the cloud is remembered
    /// and finished at the next sign-in, before anything is read back.
    /// </summary>
    public sealed class CloudSync
    {
        public enum Status
        {
            /// <summary>Not signed in, or no cloud on this build.</summary>
            Off,
            Checking,
            /// <summary>The cloud copy is further along; waiting for the player's pick.</summary>
            Choosing,
            Ready,
            /// <summary>Reading or deleting failed; nothing uploads this session until a retry.</summary>
            Failed,
        }

        const string DeletePendingKey = "nc_cloud_delete";

        readonly ICloudSlot slot;
        readonly Func<PlayerProgress> local;
        readonly bool localDamaged;
        bool uploading;
        bool uploadAgain;
        float lastUploadAt = float.NegativeInfinity;

        public Status Current { get; private set; }

        public bool Available => slot.Configured;

        /// <summary>The cloud copy should replace the local save, with or without asking.</summary>
        public event Action<PlayerProgress, CloudChoice> Found;

        /// <summary>save_error parameters: stage, code.</summary>
        public event Action<string, string> Failed;

        public event Action Changed;

        public static ICloudSlot CreateSlot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new PlayGamesSlot();
#else
            return new NoCloudSlot();
#endif
        }

        public CloudSync(ICloudSlot slot, Func<PlayerProgress> local, bool localDamaged)
        {
            this.slot = slot;
            this.local = local;
            this.localDamaged = localDamaged;
        }

        /// <summary>Signs in (silently at launch, with the Play Games prompt when tapped) and settles the slot.</summary>
        public void Start(bool interactive)
        {
            if (!slot.Configured || Current == Status.Checking || Current == Status.Choosing)
                return;
            Set(Status.Checking);
            slot.SignIn(interactive, signedIn =>
            {
                if (!signedIn)
                    Set(Status.Off);
                else if (PlayerPrefs.GetInt(DeletePendingKey, 0) == 1)
                    FinishDelete();
                else
                    ReadSlot();
            });
        }

        void FinishDelete()
        {
            slot.Delete((ok, code) =>
            {
                if (!ok)
                {
                    Failed?.Invoke("cloud_delete", code);
                    Set(Status.Failed);
                    return;
                }
                PlayerPrefs.DeleteKey(DeletePendingKey);
                PlayerPrefs.Save();
                Set(Status.Ready);
                Upload(true);
            });
        }

        void ReadSlot()
        {
            slot.Read((ok, json, code) =>
            {
                if (!ok)
                {
                    Failed?.Invoke("cloud_read", code);
                    Set(Status.Failed);
                    return;
                }
                PlayerProgress cloud = json == null ? null : SaveStore.FromJson(json);
                if (json != null && cloud == null)
                    Failed?.Invoke("cloud_read", "parse");
                CloudChoice choice = CloudSave.Choose(local(), localDamaged, cloud);
                if (choice == CloudChoice.KeepLocal)
                {
                    Set(Status.Ready);
                    Upload(true);
                    return;
                }
                Set(Status.Choosing);
                Found?.Invoke(cloud, choice);
            });
        }

        /// <summary>The player kept this phone's save over a further cloud copy; it replaces the cloud one.</summary>
        public void KeepLocal()
        {
            if (Current != Status.Choosing)
                return;
            Set(Status.Ready);
            Upload(true);
        }

        /// <summary>Uploads the current save; unforced calls wait out the upload interval.</summary>
        public void Upload(bool force)
        {
            if (Current != Status.Ready)
                return;
            if (!force && Time.realtimeSinceStartup - lastUploadAt < CloudSave.UploadIntervalSeconds)
                return;
            if (uploading)
            {
                uploadAgain = true;
                return;
            }
            uploading = true;
            lastUploadAt = Time.realtimeSinceStartup;
            PlayerProgress progress = local();
            slot.Write(SaveStore.ToJson(progress), TimeSpan.FromSeconds(Math.Max(0, progress.PlaySeconds)),
                CloudSave.Describe(progress), (ok, code) =>
                {
                    uploading = false;
                    if (!ok)
                        Failed?.Invoke("cloud_write", code);
                    if (uploadAgain)
                    {
                        uploadAgain = false;
                        Upload(true);
                    }
                });
        }

        /// <summary>
        /// "Delete my data": removes the cloud copy now if possible, and otherwise at the next
        /// sign-in; until then this device neither reads nor restores it.
        /// </summary>
        public void Delete()
        {
            if (!slot.Configured)
                return;
            PlayerPrefs.SetInt(DeletePendingKey, 1);
            PlayerPrefs.Save();
            Set(Status.Off);
            slot.Delete((ok, _) =>
            {
                if (!ok)
                    return;
                PlayerPrefs.DeleteKey(DeletePendingKey);
                PlayerPrefs.Save();
            });
        }

        void Set(Status status)
        {
            Current = status;
            Changed?.Invoke();
        }
    }
}
