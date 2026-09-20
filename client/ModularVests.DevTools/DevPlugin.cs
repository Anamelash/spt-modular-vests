using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ModularVests.Client;
using ModularVests.Client.Bones;
using ModularVests.DevTools.Patches;

namespace ModularVests.DevTools
{
    /// <summary>
    /// Development tools of Modular Vests: the pouch layout editor on the Modding screen. Not a
    /// part of the mod players get - deployed for development only, never packaged. The plugin
    /// works the same with or without it.
    /// </summary>
    [BepInPlugin(Guid, Name, Plugin.Version)]
    [BepInDependency(Plugin.Guid)]
    public class DevPlugin : BaseUnityPlugin
    {
        public const string Guid = Plugin.Guid + ".devtools";
        public const string Name = Plugin.Name + " DevTools";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            try
            {
                DevConfig.Bind(Config);

                var failed = DevPatchTargets.SelfTest();
                if (failed.Count > 0)
                {
                    Log.LogError("[ModularVests.DevTools] patch targets self-test FAILED for: " +
                                 string.Join(", ", failed.ToArray()));
                }

                if (Plugin.Bones == null)
                {
                    Log.LogWarning("[ModularVests.DevTools] the plugin is disabled or failed to start, tools inactive");
                    return;
                }


                BoneEditorPatch.Apply(new Harmony(Guid), gameObject);
                Log.LogInfo($"{Name} {Plugin.Version} loaded");
            }
            catch (System.Exception ex)
            {
                Log.LogError($"[ModularVests.DevTools] init failed, tools INACTIVE: {ex}");
            }
        }
    }
}
