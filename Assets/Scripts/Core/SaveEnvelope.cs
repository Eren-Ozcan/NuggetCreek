using System;
using System.Security.Cryptography;
using System.Text;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Signs the local save with HMAC-SHA256 (design doc 13.1 rule 4). The key comes from
    /// the platform layer (Android Keystore on device), never from this assembly.
    /// Format: "NC1|&lt;base64 signature&gt;|&lt;payload&gt;".
    /// </summary>
    public static class SaveEnvelope
    {
        const string Version = "NC1";
        const char Separator = '|';

        public static string Wrap(string payload, byte[] key)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            return Version + Separator + Sign(payload, key) + Separator + payload;
        }

        /// <summary>False when the text is malformed or the signature does not match.</summary>
        public static bool TryUnwrap(string text, byte[] key, out string payload)
        {
            payload = null;
            if (string.IsNullOrEmpty(text))
                return false;

            int first = text.IndexOf(Separator);
            int second = first < 0 ? -1 : text.IndexOf(Separator, first + 1);
            if (second < 0 || text.Substring(0, first) != Version)
                return false;

            string signature = text.Substring(first + 1, second - first - 1);
            string body = text.Substring(second + 1);
            if (!FixedTimeEquals(signature, Sign(body, key)))
                return false;

            payload = body;
            return true;
        }

        static string Sign(string payload, byte[] key)
        {
            if (key == null || key.Length == 0)
                throw new ArgumentException("Signing key is empty.", nameof(key));
            using (var hmac = new HMACSHA256(key))
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }

        static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
