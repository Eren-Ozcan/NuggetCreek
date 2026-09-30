using System.IO;
using NuggetCreek.Game;
using NuggetCreek.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace NuggetCreek.Editor
{
    /// <summary>
    /// Regenerates the greybox scene. Everything visible is built by <see cref="GameRoot"/> at
    /// runtime, so the scene only holds a camera, the event system and the root object.
    /// Batchmode: -executeMethod NuggetCreek.Editor.GreyboxSceneBuilder.Build
    /// </summary>
    public static class GreyboxSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Creek.unity";

        [MenuItem("Nugget Creek/Rebuild Greybox Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Palette.Background;
            cameraObject.transform.position = new Vector3(0, 0, -10);

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            new GameObject("Game", typeof(GameRoot));

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"Greybox scene written to {ScenePath}");
        }
    }
}
