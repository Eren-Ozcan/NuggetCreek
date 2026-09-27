using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace NuggetCreek.Editor
{
    /// <summary>
    /// The External Dependency Manager writes the Firebase local Maven repo into settings.gradle
    /// as "file:///" + the raw project path. The space in "Nugget Creek" makes that an invalid
    /// URI ("Cannot convert URI ... to a file"), so escape it in the exported Gradle project.
    /// </summary>
    sealed class GradleProjectPathFix : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string settings = Path.Combine(unityLibraryPath, "..", "settings.gradle");
            if (!File.Exists(settings))
                return;
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
            string raw = "file:///" + project;
            string escaped = "file:///" + project.Replace(" ", "%20");
            string text = File.ReadAllText(settings);
            if (raw != escaped && text.Contains(raw))
                File.WriteAllText(settings, text.Replace(raw, escaped));
        }
    }
}
