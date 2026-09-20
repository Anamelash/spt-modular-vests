using System;
using System.Collections.Generic;
using Diz.Resources;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// The mod's own pouch models (bundles under <see cref="BundlePrefix"/>):
    /// <list type="bullet">
    /// <item>their materials are built on Unity's Standard shader - the only one a bundle made
    /// outside the game's SDK can carry - and stood out next to the game's items. The icon
    /// renderer even leaves Standard alone when it swaps every other shader for its _Icon
    /// variant. As the bundle loads, each material is moved onto the game's item shader
    /// (<see cref="GameShader"/>), keeping its albedo; world, inspect window and icons then
    /// draw them like any other item;</item>
    /// <item>icons of all the mod's items (these pouches, the rigs, the AFAK/IFAK pouches on the
    /// game's models) are cached by the game under a hash of the item; a salt in that hash
    /// (<see cref="IconStyle"/> and the F12 redraw counter) makes the game draw them again
    /// when the models or their look change, without touching the icons of anything else.</item>
    /// </list>
    /// </summary>
    internal static class PouchModelPatch
    {
        public const string BundlePrefix = "modularvests/";

        /// <summary>Recoloured clones of the donors' bundles (build/build-vest-bundles.ps1).</summary>
        public const string VestBundlePrefix = BundlePrefix + "vests/";

        public const string GameShader = "p0/Reflective/Bumped Specular SMap";

        /// <summary>Bump when the models or their look change in a way cached icons should not keep.</summary>
        private const int IconStyle = 6;

        private static readonly int SpecMapId = Shader.PropertyToID("_SpecMap");

        private static readonly HashSet<string> LoggedVestBundles = new HashSet<string>();
        private static readonly List<Material> Converted = new List<Material>();
        private static readonly Dictionary<string, bool> ModItems = new Dictionary<string, bool>();
        private static Texture2D _specMap;
        private static bool _shaderMissingLogged;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(PatchTargets.EasyBundle_SetAssets
                          ?? throw new InvalidOperationException("EasyBundle.Assets setter not found"),
                postfix: new HarmonyMethod(typeof(PouchModelPatch), nameof(AssetsPostfix)));
            harmony.Patch(PatchTargets.IconsHash_HashForItem
                          ?? throw new InvalidOperationException("IconsHash.HashForItem not found"),
                postfix: new HarmonyMethod(typeof(PouchModelPatch), nameof(HashPostfix)));

            ClientConfig.Specular.SettingChanged += (_, __) => UpdateSpecMap();
            ClientConfig.Gloss.SettingChanged += (_, __) => UpdateSpecMap();
            ClientConfig.IconsRedrawRequested += () =>
                Plugin.Log.LogInfo($"[ModularVests] pouch icons will be drawn again (generation {ClientConfig.IconGeneration.Value})");
        }

        // --- the look ---

        private static void AssetsPostfix(EasyBundle __instance, Object[] value)
        {
            try
            {
                if (value == null || !ClientConfig.UseGameShader.Value || __instance.Key == null ||
                    !__instance.Key.StartsWith(BundlePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // A rig bundle is a recoloured clone of the game's own: it already wears the game's
                // shader, so there is nothing to move - one line says it loaded and that is all.
                if (__instance.Key.StartsWith(VestBundlePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (LoggedVestBundles.Add(__instance.Key))
                    {
                        Plugin.Log.LogInfo($"[ModularVests] {__instance.Key}: recoloured rig model loaded");
                    }

                    return;
                }

                // The game loads the prefab (by rcid), not its materials: they are found on its renderers.
                var materials = new HashSet<Material>();
                foreach (var asset in value)
                {
                    if (asset is Material material)
                    {
                        materials.Add(material);
                    }
                    else if (asset is GameObject prefab)
                    {
                        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(includeInactive: true))
                        {
                            foreach (var shared in renderer.sharedMaterials)
                            {
                                if (shared != null)
                                {
                                    materials.Add(shared);
                                }
                            }
                        }
                    }
                }

                // a model's shared maps bundle (normal, specular) has no materials of its own
                if (materials.Count == 0)
                {
                    return;
                }

                var count = 0;
                foreach (var material in materials)
                {
                    if (material.shader != null && StandardShaders.Contains(material.shader.name) && Convert(material))
                    {
                        count++;
                    }
                }

                Plugin.Log.LogInfo($"[ModularVests] {__instance.Key}: {count} of {materials.Count} material(s) moved " +
                                   $"onto {GameShader} (colour space {QualitySettings.activeColorSpace})");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] pouch model look: {ex}");
            }
        }

        private static bool Convert(Material material)
        {
            var shader = Shader.Find(GameShader);
            if (shader == null)
            {
                if (!_shaderMissingLogged)
                {
                    _shaderMissingLogged = true;
                    Plugin.Log.LogWarning($"[ModularVests] shader {GameShader} is not loaded, pouch models keep Standard");
                }

                return false;
            }

            // the maps the bundle carries; _SpecGlossMap is Standard's, gone once the shader changes
            var albedo = material.mainTexture;
            var normal = material.HasProperty(BumpMapId) ? material.GetTexture(BumpMapId) : null;
            var spec = material.HasProperty(SpecGlossMapId) ? material.GetTexture(SpecGlossMapId) : null;
            material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
            material.mainTexture = albedo;

            // everything but the textures as the game's IFAK has it
            foreach (var value in ReferenceFloats)
            {
                if (material.HasProperty(value.Key))
                {
                    material.SetFloat(value.Key, value.Value);
                }
            }

            foreach (var value in ReferenceColors)
            {
                if (material.HasProperty(value.Key))
                {
                    material.SetColor(value.Key, value.Value);
                }
            }

            // a model without maps of its own gets flat ones rather than the shader's fallbacks
            if (material.HasProperty(BumpMapId))
            {
                material.SetTexture(BumpMapId, normal != null ? normal : FlatNormal());
            }

            if (material.HasProperty(SpecMapId))
            {
                material.SetTexture(SpecMapId, spec != null ? spec : SpecMap());
            }

            if (material.HasProperty(CubeId))
            {
                var cube = ReflectionCube();
                material.SetTexture(CubeId, cube);
                if (cube == null && material.HasProperty(ReflectColorId))
                {
                    material.SetColor(ReflectColorId, Color.black);
                }
            }

            Converted.Add(material);
            return true;
        }

        /// <summary>The shaders the bundles are built with (the Specular setup carries the specular map).</summary>
        private static readonly HashSet<string> StandardShaders = new HashSet<string> { "Standard", "Standard (Specular setup)" };

        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int SpecGlossMapId = Shader.PropertyToID("_SpecGlossMap");
        private static readonly int CubeId = Shader.PropertyToID("_Cube");
        private static readonly int ReflectColorId = Shader.PropertyToID("_ReflectColor");

        /// <summary>Reflection cubemap of the IFAK's material (from the game's "cubemaps" bundle).</summary>
        private const string ReferenceCube = "patron_cubemap_metall_matte";

        /// <summary>
        /// The IFAK's material (item_ifak_loot.bundle, item_ifak_LOD0), read from the game's bundle:
        /// what a fabric item on this shader looks like in the game. Textures excepted.
        /// </summary>
        private static readonly Dictionary<string, float> ReferenceFloats = new Dictionary<string, float>
        {
            { "_BumpScale", 1f }, { "_BumpTiling", 1f }, { "_NormalIntensity", 1f }, { "_NormalUVMultiplier", 1f },
            { "_Glossness", 1.53f }, { "_Specularness", 1.06f }, { "_NdotLOffset", 0.4f }, { "_DropsSpec", 128f },
            { "_HasTint", 0f }, { "_USERAIN", 0f }, { "USEHEAT", 0f }, { "_HeatVisible", 1f }, { "_HeatTemp", 0f },
            { "_SkinnedMeshMaterial", 0f }, { "_StencilType", 2f }, { "_Factor", 0f }, { "_Units", 0f },
            { "_NightRippleFakeLightOffset", 0.2f }, { "_RippleFakeLightIntensityOffset", 0.7f }, { "_RippleTexScale", 4f },
            { "_SrcBlend", 1f }, { "_DstBlend", 0f }, { "_ZWrite", 1f }, { "_Cutoff", 0.5f },
        };

        private static readonly Dictionary<string, Color> ReferenceColors = new Dictionary<string, Color>
        {
            { "_Color", new Color(0.80147f, 0.80147f, 0.80147f, 1f) },
            { "_SpecColor", new Color(0.5f, 0.5f, 0.5f, 1f) },
            { "_ReflectColor", new Color(0.625f, 0.625f, 0.625f, 0.5f) },
            { "_BaseTintColor", Color.white },
            { "_DefVals", new Color(1f, 1f, 0f, 0f) },
            { "_SpecVals", new Color(1f, 1f, 0f, 0f) },
            { "_EmissionColor", new Color(0f, 0f, 0f, 1f) },
            { "_Temperature", new Color(0.1f, 0.2f, 0.28f, 0f) },
            { "_HeatCenter", new Color(0f, 0f, 0f, 1f) },
            { "_HeatColor1", new Color(1f, 0f, 0f, 1f) },
            { "_HeatColor2", new Color(1f, 0.34f, 0f, 1f) },
            { "_HeatSize", new Color(0.02f, 0.04f, 0.02f, 1f) },
        };

        private static Texture2D _flatNormal;
        private static Cubemap _cube;
        private static bool _cubeMissingLogged;

        /// <summary>
        /// A normal map that bends nothing, in the packing the game's shader reads: X from alpha, Y
        /// from green (DXT5nm, red is 1 - as in the game's own normal maps).
        /// </summary>
        private static Texture2D FlatNormal()
        {
            if (_flatNormal == null)
            {
                _flatNormal = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false, linear: true)
                {
                    name = "ModularVests.FlatNormal",
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                var pixels = new Color[16];
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color(1f, 0.5f, 0.5f, 0.5f);
                }

                _flatNormal.SetPixels(pixels);
                _flatNormal.Apply();
            }

            return _flatNormal;
        }

        /// <summary>The IFAK's reflection cubemap, when the game has it loaded; null otherwise (no reflection).</summary>
        private static Cubemap ReflectionCube()
        {
            if (_cube != null)
            {
                return _cube;
            }

            foreach (var cube in Resources.FindObjectsOfTypeAll<Cubemap>())
            {
                if (cube.name == ReferenceCube)
                {
                    _cube = cube;
                    return cube;
                }
            }

            if (!_cubeMissingLogged)
            {
                _cubeMissingLogged = true;
                Plugin.Log.LogWarning($"[ModularVests] cubemap {ReferenceCube} is not loaded, pouch models get no reflection");
            }

            return null;
        }

        /// <summary>
        /// One flat specular map for every pouch: specular in RGB, gloss in alpha (the fabric has
        /// no map of its own). Shared, so a change in F12 shows on every pouch at once.
        /// </summary>
        private static Texture2D SpecMap()
        {
            if (_specMap == null)
            {
                _specMap = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false, linear: true)
                {
                    name = "ModularVests.PouchSpecMap",
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                UpdateSpecMap();
            }

            return _specMap;
        }

        private static void UpdateSpecMap()
        {
            if (_specMap == null)
            {
                return;
            }

            var s = ClientConfig.Specular.Value;
            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(s, s, s, ClientConfig.Gloss.Value);
            }

            _specMap.SetPixels(pixels);
            _specMap.Apply();
        }

        // --- the icons ---

        /// <summary>Every item the server registers for the mod has an internal name starting with this.</summary>
        public const string TemplateNamePrefix = "modularvests_";

        private static void HashPostfix(Item item, ref int __result)
        {
            if (item == null || !IsModItem(item))
            {
                return;
            }

            __result ^= unchecked((IconStyle * 7919 + ClientConfig.IconGeneration.Value) * 104729 + 0x5EED);
        }

        /// <summary>
        /// Whether the item is one of the mod's, by its template's internal name (items.jsonc "name"):
        /// the rigs wear models of the game or of another mod, so the model's
        /// bundle does not tell. Cached per template.
        /// </summary>
        private static bool IsModItem(Item item)
        {
            var tpl = item.StringTemplateId;
            if (tpl == null)
            {
                return false;
            }

            if (!ModItems.TryGetValue(tpl, out var mine))
            {
                var name = item.Template?._name;
                mine = name != null && name.StartsWith(TemplateNamePrefix, StringComparison.Ordinal);
                ModItems[tpl] = mine;
            }

            return mine;
        }
    }
}
