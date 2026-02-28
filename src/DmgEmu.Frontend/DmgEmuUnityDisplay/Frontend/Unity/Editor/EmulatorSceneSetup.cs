using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DmgEmu.Frontend.Unity;

namespace DmgEmu.Frontend.Unity.Editor
{
    /// <summary>
    /// Provides editor utilities for quickly creating a sample scene that
    /// is preconfigured for running the DMG emulator frontend.
    /// </summary>
    public static class EmulatorSceneSetup
    {
        private const string DefaultScenePath = "Assets/Scenes/EmulatorScene.unity";
        private const string DefaultRomPath = "Assets/Roms/pkred.gb";

        [MenuItem("DMG Emulator/Setup Sample Scene")]
        public static void CreateSampleScene()
        {
            // create a new, empty scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ensure the Assets/Scenes folder exists
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            // create a quad and position it
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "EmulatorDisplay";
            quad.transform.position = Vector3.zero;
            // keep rotation at identity; orientation corrections are now handled
            // by the UnityDisplay flip parameters rather than by rotating the mesh.
            quad.transform.rotation = Quaternion.identity;

            // add a camera if none exists
            if (Camera.main == null)
            {
                var camObj = new GameObject("Main Camera");
                var cam = camObj.AddComponent<Camera>();
                cam.tag = "MainCamera";
                // use orthographic projection and place closer so the quad fills the view
                cam.orthographic = true;
                cam.orthographicSize = 1.0f; // quad is 1 unit tall
                cam.transform.position = new Vector3(0, 0, -5);
                cam.clearFlags = CameraClearFlags.Color;
                cam.backgroundColor = Color.black;
            }

            // create an empty gameobject for controller
            var controllerObj = new GameObject("EmulatorController");
            var controller = controllerObj.AddComponent<EmulatorController>();

            // assign display renderer and default rom path via exposed properties
            controller.DisplayRenderer = quad.GetComponent<Renderer>();
            controller.RomPath = DefaultRomPath;

            // optionally create a material that uses the emulator texture
            // (EmulatorController will assign the texture at runtime)

            // save the scene asset
            EditorSceneManager.SaveScene(scene, DefaultScenePath);

            Debug.Log($"Sample emulator scene created at '{DefaultScenePath}'.");
            EditorUtility.DisplayDialog("DMG Emulator", "Sample scene has been created and saved.\nYou can open it via the Project window.", "OK");
        }
    }
}
