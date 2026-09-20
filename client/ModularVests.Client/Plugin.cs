using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ModularVests.Client.Bones;
using ModularVests.Client.Patches;

namespace ModularVests.Client
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.anamelash.modularvests";
        public const string Name = "Modular Vests";
        public const string Version = "1.0.0";

        public const string BonesFile = "bones.json";
        public const string MountsFile = "mounts.json";

        internal static ManualLogSource Log;
        internal static Harmony HarmonyInstance;
        internal static BoneStore Bones;
        internal static MountStore Mounts;

        private static int _patchFailures;

        private void Awake()
        {
            Log = Logger;
            try
            {
                Initialize();
            }
            catch (System.Exception ex)
            {
                // Unity swallows Awake exceptions into Player.log
                Log.LogError($"[ModularVests] FATAL: plugin init failed, mod is INACTIVE: {ex}");
            }
        }

        private void Initialize()
        {
            ClientConfig.Bind(Config);
            HarmonyInstance = new Harmony(Guid);

            if (ClientConfig.SelfTestOnLoad.Value)
            {
                var failed = PatchTargets.SelfTest();
                if (failed.Count == 0)
                {
                    Log.LogInfo("[ModularVests] Patch targets self-test: all targets resolved OK");
                }
                else
                {
                    Log.LogError("[ModularVests] Patch targets self-test FAILED for: " + string.Join(", ", failed) +
                                 ". Likely name drift after an SPT update");
                }
            }

            if (!ClientConfig.Enabled.Value)
            {
                Log.LogWarning($"{Name} {Version} loaded DISABLED (F12 > Enabled)");
                return;
            }

            var pluginDir = Path.GetDirectoryName(Info.Location) ?? BepInEx.Paths.PluginPath;
            Bones = new BoneStore(Path.Combine(pluginDir, BonesFile), m => Log.LogWarning("[ModularVests] " + m));
            Bones.Load();
            Mounts = new MountStore(Path.Combine(pluginDir, MountsFile), m => Log.LogWarning("[ModularVests] " + m));
            Mounts.Load();

            Apply("pouch bones", PouchBonePatch.Apply);
            Apply("pouch bones on body", PouchBodyPatch.Apply);
            Apply("pouch seat", PouchSeatPatch.Apply);
            Apply("pouch models", PouchModelPatch.Apply);
            Apply("rig window", RigWindowPatch.Apply);
            Apply("pouch slot panel", PouchSlotPanelPatch.Apply);
            Apply("cell blocking", SlotBlockingPatch.Apply);
            Apply("covered cell icons", CoveredCellPatch.Apply);
            Apply("pouch slot icon", PouchSlotIconPatch.Apply);
            Apply("raid lock", RaidLockPatch.Apply);
            Apply("no automatic placement on pouch cells", NoAutoPlacePatch.Apply);
            Apply("pouch grids for loot", PouchGridsForLootPatch.Apply);
            Apply("pouch contents in the item search", PouchTopLevelItemsPatch.Apply);
            Apply("trader assortment order", TraderAssortOrderPatch.Apply);
            Apply("raid reach", RaidReachPatch.Apply);

            if (_patchFailures > 0)
            {
                Log.LogError($"[ModularVests] {_patchFailures} patch group(s) FAILED to apply, parts of the mod " +
                             "are inactive. See the errors above");
            }

            Log.LogInfo($"{Name} {Version} loaded" +
                        (_patchFailures > 0 ? $" WITH {_patchFailures} FAILED PATCH GROUP(S)" : ""));
        }

        private static void Apply(string what, System.Action<Harmony> apply)
        {
            try
            {
                apply(HarmonyInstance);
            }
            catch (System.Exception ex)
            {
                _patchFailures++;
                Log.LogError($"[ModularVests] {what}: patch failed: {ex}");
            }
        }
    }
}
