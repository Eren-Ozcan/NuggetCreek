using System.IO;
using UnityEditor.Android;

namespace NuggetCreek.Editor
{
    /// <summary>
    /// Google Mobile Ads marks RECEIVE_BOOT_COMPLETED with tools:node="remove", which also
    /// strips the one the notification package needs to restore its alarms after a phone
    /// restart; the 30 day notification chain (design doc 10.1.2) would then be lost. The
    /// launcher manifest has the highest merge priority, so declaring the permission there with
    /// tools:node="replace" keeps it.
    /// </summary>
    sealed class BootPermissionFix : IPostGenerateGradleAndroidProject
    {
        const string Permission =
            "<uses-permission android:name=\"android.permission.RECEIVE_BOOT_COMPLETED\" tools:node=\"replace\" />";

        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string manifest = Path.Combine(unityLibraryPath, "..", "launcher", "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest))
                return;
            string text = File.ReadAllText(manifest);
            if (text.Contains("RECEIVE_BOOT_COMPLETED"))
                return;
            int application = text.IndexOf("<application");
            if (application < 0)
                return;
            File.WriteAllText(manifest, text.Insert(application, Permission + "\n  "));
        }
    }
}
