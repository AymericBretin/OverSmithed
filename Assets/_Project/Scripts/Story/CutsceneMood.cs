using UnityEngine;
using UnityEngine.Rendering;

namespace OverSmithed.Story
{
    /// <summary>
    /// A lighting mood of a cutscene (night, morning): its children hold the lights and the post-process Volume of
    /// that mood, and it sets the sky, ambient light and fog. A shot turns its mood on (the others off).
    /// </summary>
    public class CutsceneMood : MonoBehaviour
    {
        [SerializeField] private Material _skybox;
        [Header("Ambient (trilight)")]
        [SerializeField] private Color _ambientSky = new(0.6f, 0.6f, 0.7f);
        [SerializeField] private Color _ambientEquator = new(0.5f, 0.5f, 0.5f);
        [SerializeField] private Color _ambientGround = new(0.3f, 0.3f, 0.3f);
        [Header("Fog (linear)")]
        [SerializeField] private bool _fog = true;
        [SerializeField] private Color _fogColor = new(0.9f, 0.85f, 0.75f);
        [SerializeField] private float _fogStart = 45f;
        [SerializeField] private float _fogEnd = 420f;

        public void Apply()
        {
            gameObject.SetActive(true);
            if (_skybox != null)
            {
                RenderSettings.skybox = _skybox;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = _ambientSky;
            RenderSettings.ambientEquatorColor = _ambientEquator;
            RenderSettings.ambientGroundColor = _ambientGround;
            RenderSettings.fog = _fog;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;
        }
    }
}
