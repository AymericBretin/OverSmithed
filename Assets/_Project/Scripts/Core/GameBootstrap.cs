using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Core
{
    /// <summary>
    /// Lives in the Bootstrap scene. Keeps the application services alive for the whole session
    /// and opens the main menu.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            SceneManager.LoadScene(SceneNames.Menu);
        }
    }
}
