using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverSmithed.Core
{
    /// <summary>
    /// The keys / buttons the players of this machine chose (controls screen, UI-9), saved in PlayerPrefs so they are
    /// still there after a restart. Local only: nothing goes on the network.
    /// A changed key is a "binding override" put over the binding of the input actions asset (the asset file itself is
    /// never changed). The game uses several copies of that asset (one per PlayerInput, one for joining, one for the
    /// lobby hints...) and a copy does not get the overrides of the asset it was made from: every copy that must follow
    /// the chosen keys calls <see cref="Register"/>, and gets the overrides now and after every change.
    /// The Join action follows Interact: in each control scheme, a Join binding with the same default key as an Interact
    /// binding gets Interact's chosen key (the gamepad's Start button stays a Join button too).
    /// </summary>
    public static class ControlBindings
    {
        private const string SaveKey = "settings.controls";
        private const string JoinActionName = "Join/Join";
        private const string InteractActionName = "Player/Interact";

        private static readonly List<InputActionAsset> Registered = new();
        private static bool _loaded;
        private static string _json;

        /// <summary>Raised after the chosen keys changed (also after a reset), once every registered copy has them.</summary>
        public static event Action Changed;

        /// <summary>The overrides as saved (JSON from <c>SaveBindingOverridesAsJson</c>), empty when nothing changed.</summary>
        public static string Json
        {
            get { EnsureLoaded(); return _json; }
        }

        /// <summary>
        /// This copy of the actions follows the chosen keys from now on, until <see cref="Unregister"/>. The same asset
        /// can be registered several times (counted).
        /// </summary>
        public static void Register(InputActionAsset asset)
        {
            if (asset == null)
            {
                return;
            }
            EnsureLoaded();
            Registered.Add(asset);
            Apply(asset);
        }

        /// <summary>
        /// Stops updating this copy. When nobody uses it any more its overrides are removed: the first PlayerInput uses
        /// the asset file itself, which must not keep them in the editor after Play Mode.
        /// </summary>
        public static void Unregister(InputActionAsset asset)
        {
            if (asset == null || !Registered.Remove(asset) || Registered.Contains(asset))
            {
                return;
            }
            asset.RemoveAllBindingOverrides();
        }

        /// <summary>
        /// Saves the overrides of <paramref name="edited"/> (the controls screen's own copy) as the chosen keys and
        /// gives them to every registered copy.
        /// </summary>
        public static void Save(InputActionAsset edited)
        {
            EnsureLoaded();
            _json = edited.SaveBindingOverridesAsJson();
            PlayerPrefs.SetString(SaveKey, _json);
            PlayerPrefs.Save();
            for (var i = Registered.Count - 1; i >= 0; i--)
            {
                if (Registered[i] == null)
                {
                    Registered.RemoveAt(i);
                }
                else if (Registered[i] != edited)
                {
                    Apply(Registered[i]);
                }
            }
            MirrorJoin(edited);
            Changed?.Invoke();
        }

        /// <summary>Puts the default keys back for one control scheme ("Gamepad", "KeyboardLeft"...), then saves.</summary>
        public static void ResetScheme(InputActionAsset edited, string scheme)
        {
            foreach (var map in edited.actionMaps)
            {
                for (var i = 0; i < map.bindings.Count; i++)
                {
                    var indexInAction = IndexInAction(map, i);
                    if (InGroup(map.bindings[i].groups, scheme) && indexInAction >= 0)
                    {
                        map.FindAction(map.bindings[i].action).RemoveBindingOverride(indexInAction);
                    }
                }
            }
            Save(edited);
        }

        /// <summary>Gives the chosen keys to one copy (replacing the overrides it had).</summary>
        public static void Apply(InputActionAsset asset)
        {
            EnsureLoaded();
            asset.RemoveAllBindingOverrides();
            if (!string.IsNullOrEmpty(_json))
            {
                asset.LoadBindingOverridesFromJson(_json, removeExisting: true);
            }
            MirrorJoin(asset);
        }

        /// <summary>True when <paramref name="scheme"/> is one of the binding's groups ("KeyboardLeft;KeyboardRight").</summary>
        public static bool InGroup(string groups, string scheme)
        {
            if (string.IsNullOrEmpty(groups))
            {
                return false;
            }
            foreach (var group in groups.Split(InputBinding.Separator))
            {
                if (group == scheme)
                {
                    return true;
                }
            }
            return false;
        }

        // Domain reload is off: forget everything when Play Mode starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registered.Clear();
            _loaded = false;
            _json = null;
            Changed = null;
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            _json = PlayerPrefs.GetString(SaveKey, string.Empty);
        }

        // Join binding with default key K in scheme S = Interact binding with default key K in S: same chosen key.
        private static void MirrorJoin(InputActionAsset asset)
        {
            var join = asset.FindAction(JoinActionName);
            var interact = asset.FindAction(InteractActionName);
            if (join == null || interact == null)
            {
                return;
            }
            for (var j = 0; j < join.bindings.Count; j++)
            {
                var joinBinding = join.bindings[j];
                string chosen = null;
                foreach (var interactBinding in interact.bindings)
                {
                    if (interactBinding.path == joinBinding.path && interactBinding.groups == joinBinding.groups)
                    {
                        chosen = interactBinding.overridePath;
                        break;
                    }
                }
                if (chosen != null)
                {
                    join.ApplyBindingOverride(j, chosen);
                }
                else if (joinBinding.overridePath != null)
                {
                    join.RemoveBindingOverride(j);
                }
            }
        }

        // Index of the map's binding number <paramref name="mapIndex"/> inside its action's own binding list.
        private static int IndexInAction(InputActionMap map, int mapIndex)
        {
            var binding = map.bindings[mapIndex];
            var action = map.FindAction(binding.action);
            if (action == null)
            {
                return -1;
            }
            for (var i = 0; i < action.bindings.Count; i++)
            {
                if (action.bindings[i].id == binding.id)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
