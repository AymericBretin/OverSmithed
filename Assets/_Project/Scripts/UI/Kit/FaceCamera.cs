using UnityEngine;
using UnityEngine.Rendering;

namespace OverSmithed.UI
{
    /// <summary>
    /// Keeps a World Space canvas turned like the camera, so it is always readable (progress bars above stations...).
    /// Uses the main camera. Outside Play Mode it is only a preview: the canvas is turned like the game camera just
    /// before the canvases build their mesh (a World Space canvas reads its rotation there, not when a camera draws),
    /// then put back once a view has drawn, so the scene and the prefabs never save a changed rotation.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class FaceCamera : MonoBehaviour
    {
        private Camera _camera;
        private Quaternion _savedRotation;
        private bool _previewing;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                Canvas.willRenderCanvases += HandleWillRenderCanvases;
                RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
#if UNITY_EDITOR
                // Never save the preview rotation, even if a save comes before a view draws.
                UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += HandleSceneSaving;
                UnityEditor.SceneManagement.PrefabStage.prefabSaving += HandlePrefabSaving;
#endif
            }
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= HandleWillRenderCanvases;
            RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= HandleSceneSaving;
            UnityEditor.SceneManagement.PrefabStage.prefabSaving -= HandlePrefabSaving;
#endif
            RestorePreview();
        }

#if UNITY_EDITOR
        private void HandleSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => RestorePreview();
        private void HandlePrefabSaving(GameObject prefab) => RestorePreview();
#endif

        private void LateUpdate()
        {
            if (!Application.isPlaying || !TryGetCamera(out var camera))
            {
                return;
            }
            transform.rotation = camera.transform.rotation;
        }

        private bool TryGetCamera(out Camera camera)
        {
            if (_camera == null || (Application.isPlaying && !_camera.isActiveAndEnabled))
            {
                _camera = Camera.main;
            }
            camera = _camera;
            return camera != null;
        }

        // Edit mode preview: turned like the game camera from the canvas update until a view has drawn.
        private void HandleWillRenderCanvases()
        {
            if (Application.isPlaying || _previewing || !TryGetCamera(out var camera))
            {
                return;
            }
            _savedRotation = transform.localRotation;
            _previewing = true;
            transform.rotation = camera.transform.rotation;
        }

        private void HandleEndCameraRendering(ScriptableRenderContext context, Camera rendering)
        {
            RestorePreview();
        }

        private void RestorePreview()
        {
            if (!_previewing)
            {
                return;
            }
            _previewing = false;
            transform.localRotation = _savedRotation;
        }
    }
}
