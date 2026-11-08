using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// Put the UIEventSystem prefab once in every scene that has UI. Mouse, both keyboard sides and every gamepad
    /// can use the menus. It works on its own copy of the input actions, because the first player's PlayerInput
    /// uses the original asset and limits it to that player's devices (the other devices could not use the
    /// menus anymore).
    /// </summary>
    [RequireComponent(typeof(EventSystem), typeof(InputSystemUIInputModule))]
    public class UiEventSystem : MonoBehaviour
    {
        private InputActionAsset _actionsCopy;

        private void Awake()
        {
            // Swap the asset while the module is off, so it enables the actions of the copy, not the original.
            var module = GetComponent<InputSystemUIInputModule>();
            var wasEnabled = module.enabled;
            module.enabled = false;
            _actionsCopy = Instantiate(module.actionsAsset);
            module.actionsAsset = _actionsCopy;
            module.enabled = wasEnabled;
        }

        private void OnDestroy()
        {
            if (_actionsCopy != null)
            {
                Destroy(_actionsCopy);
            }
        }
    }
}
