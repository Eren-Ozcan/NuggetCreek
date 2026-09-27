using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace NuggetCreek.Game
{
    /// <summary>
    /// The HMAC key that signs the save and the Remote Config cache (design doc 13.1 rule 4).
    /// On Android it is 32 random bytes, kept in PlayerPrefs only encrypted with an AES-GCM
    /// key that never leaves AndroidKeyStore (Plugins/Android/SaveKeystore.java), so an edited
    /// PlayerPrefs file cannot be re-signed off the device. The editor and other platforms keep
    /// the device-id key.
    /// </summary>
    public static class SaveKey
    {
        const string WrappedKey = "nc_key";
        const string Alias = "nugget-creek-save";
        const int KeyBytes = 32;

        static byte[] current;

        /// <summary>
        /// Exception type when the Keystore key could not be created or read; the device-id key
        /// is used instead so the player keeps their save. Reported as save_error.
        /// </summary>
        public static string Failure { get; private set; }

        /// <summary>
        /// Whether data signed with <see cref="Legacy"/> is still trusted: only in the launch
        /// that creates the Keystore key (the one-time migration), or when the device key is the
        /// current key anyway. Later launches reject it, because anyone can derive it on the device.
        /// </summary>
        public static bool AcceptsLegacy { get; private set; } = true;

        public static byte[] Current()
        {
            if (current != null)
                return current;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                current = LoadOrCreate();
            }
            catch (Exception e)
            {
                Failure = e.GetType().Name;
                Debug.LogWarning("SaveKey: Keystore unavailable, using the device key: " + e.Message);
                current = Legacy();
            }
#else
            current = Legacy();
#endif
            return current;
        }

        /// <summary>
        /// The pre-Keystore key (SHA-256 of the device id). Data signed with it is accepted and
        /// re-signed with <see cref="Current"/> on the next write.
        /// </summary>
        public static byte[] Legacy()
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes("nugget-creek-save/" + SystemInfo.deviceUniqueIdentifier));
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static byte[] LoadOrCreate()
        {
            using (var keystore = new AndroidJavaClass("com.yilkgames.nuggetcreek.SaveKeystore"))
            {
                string wrapped = PlayerPrefs.GetString(WrappedKey, "");
                if (wrapped.Length > 0)
                {
                    byte[] stored = ToBytes(keystore.CallStatic<sbyte[]>("unwrap", Alias, wrapped));
                    AcceptsLegacy = false;
                    return stored;
                }

                var key = new byte[KeyBytes];
                using (var random = RandomNumberGenerator.Create())
                    random.GetBytes(key);
                PlayerPrefs.SetString(WrappedKey, keystore.CallStatic<string>("wrap", Alias, ToSbytes(key)));
                PlayerPrefs.Save();
                return key;
            }
        }

        static sbyte[] ToSbytes(byte[] bytes)
        {
            var result = new sbyte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }

        static byte[] ToBytes(sbyte[] bytes)
        {
            var result = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }
#endif
    }
}
