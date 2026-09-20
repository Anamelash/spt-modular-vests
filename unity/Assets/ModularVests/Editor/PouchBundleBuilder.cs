using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Batch build of the Modular Vests pouch bundles (SPT 4.1 / EFT, Unity 2022.3.43f1).
/// Run: Unity.exe -batchmode -quit -projectPath &lt;unity&gt;
///      -executeMethod PouchBundleBuilder.Build -logFile build.log
///
/// Input: the pouch kits copied by build/build-bundles.ps1 into Assets/ModularVests/Source
/// (&lt;model&gt;/&lt;model&gt;_lod_meshes.fbx with _LOD0/_LOD1 meshes, centred pivot, metres, Y-up;
/// textures/&lt;prefix&gt;_albedo_&lt;variant&gt;.png, normal and specular maps).
///
/// Output: one bundle per colour variant, modularvests/&lt;model&gt;_&lt;variant&gt;.bundle, holding one
/// prefab of the same name - EFT takes the asset named like its bundle when the item's rcid
/// does not name another (IEasyBundle.SameNameAsset), and the items name it as rcid as well.
/// The prefab: the two LOD meshes under one LODGroup, a Standard material carrying the EFT
/// albedo, normal and specular maps, a BoxCollider, the EFT PreviewPivot and a ModPlacer
/// that stands the pouch on its cell: upright, its back on the rig's surface.
/// </summary>
public static class PouchBundleBuilder
{
    private const string SourceRoot = "Assets/ModularVests/Source";
    private const string GeneratedRoot = "Assets/ModularVests/Generated";
    private const string BundlePrefix = "modularvests/";
    private const string OutputDir = "BundleOutput";

    /// <summary>
    /// Shipped sizes (the sources are 2048²), those of the game's own IFAK: albedo and normal 1024,
    /// specular 512. The normal and the specular map are shared by every colour yet copied into
    /// each colour's bundle.
    /// </summary>
    private const int AlbedoSize = 1024;

    private const int NormalSize = 1024;

    private const int SpecSize = 512;

    /// <summary>Turn of the model for its inventory icon, degrees around its up axis.</summary>
    private const float IconTurn = 160f;

    private const float Lod0Height = 0.05f;
    private const float Lod1Height = 0.002f;

    private sealed class PouchModel
    {
        public string Name;
        public string TexturePrefix;
        public string[] Variants;

        /// <summary>How many cells wide the pouch sits on a rig (its footprint's width).</summary>
        public int Cells;

        /// <summary>
        /// Size against the rig's scale, when the real thing is not as wide as its cell: 1 (the
        /// default) fills the cell, 0.7 is a model 30 % smaller. Set by eye, in game.
        /// </summary>
        public float Size = 1f;
    }

    private static readonly PouchModel[] Models =
    {
        new PouchModel { Name = "magpouch_07", TexturePrefix = "magpouch_07", Cells = 1, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
        new PouchModel { Name = "magpouch_02_opentop", TexturePrefix = "magpouch_02", Cells = 1, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
        new PouchModel { Name = "magpouch_03_flapper_close", TexturePrefix = "magpouch_03", Cells = 1, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
        new PouchModel { Name = "pouch_01_gadget", TexturePrefix = "pouch_01", Cells = 1, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
        new PouchModel { Name = "pouch_03_admin", TexturePrefix = "pouch_03", Cells = 2, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
        new PouchModel { Name = "pouch_06_survival", TexturePrefix = "pouch_06", Cells = 2, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
        new PouchModel { Name = "frag_grenade_pouch", TexturePrefix = "frag_grenade_pouch", Cells = 1, Variants = new[] { "multicam", "black", "coyote", "olive", "emr_summer" } },
        new PouchModel { Name = "small_pouch", TexturePrefix = "small_pouch", Cells = 1, Size = 0.7f, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
        new PouchModel { Name = "grenade_pouch", TexturePrefix = "grenade_pouch", Cells = 1, Size = 0.9f, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
        new PouchModel { Name = "vertical_pouch", TexturePrefix = "vertical_pouch", Cells = 1, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
        new PouchModel { Name = "bottle_pouch", TexturePrefix = "bottle_pouch", Cells = 1, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
        new PouchModel { Name = "med_pouch", TexturePrefix = "med_pouch", Cells = 2, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
        new PouchModel { Name = "utility_pouch", TexturePrefix = "utility_pouch", Cells = 2, Variants = new[] { "coyote", "olive", "multicam", "black", "emr_summer" } },
    };

    /// <summary>The size anchor: the magazine pouch is one cell wide, and every model is measured against it.</summary>
    private const string ReferenceModel = "magpouch_07";

    /// <summary>
    /// A model may be this much narrower per cell than the reference and keep its own size;
    /// anything wider, or narrower still, is scaled to exactly the reference's width per cell.
    /// </summary>
    private const float NarrowerAllowed = 0.05f;

    /// <summary>
    /// How a model stands on a cell bone (bone +Z out of the rig, +Y up): the model's own
    /// direction that faces away from the rig, and the one that points up. Checked on
    /// the previews (RenderPreviews): every kit faces +Z (flap / front panel) with its
    /// MOLLE straps on -Z, and stand along +Y.
    /// </summary>
    private static readonly Vector3 ModelOutward = Vector3.forward;
    private static readonly Vector3 ModelUp = Vector3.up;

    public static void Build()
    {
        Run(() =>
        {
            foreach (var model in Models)
            {
                ConfigureModel(model);
            }

            FitWidths();
            var prefabs = new List<string>();
            foreach (var model in Models)
            {
                foreach (var variant in model.Variants)
                {
                    prefabs.Add(BuildPrefab(model, variant));
                }
            }

            BuildBundles(prefabs, Models);
        });
    }

    /// <summary>Front, side and top views of every model, to see which way it faces.</summary>
    public static void RenderPreviews()
    {
        Run(() =>
        {
            var dir = Path.Combine(OutputDir, "previews");
            Directory.CreateDirectory(dir);
            foreach (var model in Models)
            {
                ConfigureModel(model);
            }

            FitWidths();
            foreach (var model in Models)
            {
                var material = BuildMaterial(model, model.Variants[0]);
                var instance = InstantiateModel(model, material);
                try
                {
                    var bounds = LocalBounds(instance);
                    Debug.Log($"[ModularVests] {model.Name}: bounds centre {bounds.center:F4}, size {bounds.size:F4}");
                    foreach (var (name, direction) in new[]
                             {
                                 ("posZ", Vector3.forward), ("negZ", Vector3.back), ("posX", Vector3.right),
                                 ("posY", Vector3.up),
                             })
                    {
                        Render(instance, bounds, direction, Path.Combine(dir, $"{model.Name}_{name}.png"));
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        });
    }

    private static void Run(Action action)
    {
        try
        {
            // The game renders in gamma space, and shaders packed into a bundle are compiled for
            // the project's colour space.
            if (PlayerSettings.colorSpace != ColorSpace.Gamma)
            {
                throw new Exception("the project must be in the gamma colour space " +
                                    "(ProjectSettings: m_ActiveColorSpace: 0), like the game");
            }

            action();
            Debug.Log("[ModularVests] OK");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ModularVests] FAILED: {ex}");
            EditorApplication.Exit(1);
        }
    }

    // --- import ---

    private static string FbxPath(PouchModel model) => $"{SourceRoot}/{model.Name}/{model.Name}_lod_meshes.fbx";

    /// <summary>Albedo in RGB, the reflection mask of the game's item shader in alpha.</summary>
    private static string AlbedoPath(PouchModel model, string variant) =>
        $"{SourceRoot}/{model.Name}/textures/{model.TexturePrefix}_albedo_{variant}.png";

    /// <summary>Tangent-space normal map (+Y), one for every colour of a model.</summary>
    private static string NormalPath(PouchModel model) =>
        $"{SourceRoot}/{model.Name}/textures/{model.TexturePrefix}_normal.png";

    /// <summary>Specular strength, grey, linear values; one for every colour of a model.</summary>
    private static string SpecPath(PouchModel model) =>
        $"{SourceRoot}/{model.Name}/textures/{model.TexturePrefix}_spec.png";

    /// <summary>
    /// Brings every model to the rig's scale: its width per cell against the reference's (see
    /// <see cref="NarrowerAllowed"/>), times the model's own <see cref="PouchModel.Size"/> for the
    /// ones that are smaller than their cell. The scale goes into the FBX import (Scale Factor), so
    /// the seat, the collider and the icon are all worked out from the scaled model.
    /// </summary>
    private static void FitWidths()
    {
        var reference = Models.Single(m => m.Name == ReferenceModel);
        SetScale(reference, 1f);
        var perCell = MeasureWidth(reference) / reference.Cells;
        foreach (var model in Models)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath(model));
            var own = MeasureWidth(model) / importer.globalScale / model.Cells;
            var ratio = own / perCell;
            var scale = (ratio > 1f || ratio < 1f - NarrowerAllowed ? 1f / ratio : 1f) * model.Size;
            SetScale(model, scale);
            Debug.Log($"[ModularVests] {model.Name}: {own * model.Cells:0.000} m over {model.Cells} cell(s), " +
                      $"{(ratio - 1f) * 100f:+0.0;-0.0}% against {ReferenceModel}" +
                      (Mathf.Abs(model.Size - 1f) > 1e-4f ? $", own size {model.Size:0.00}" : "") +
                      $" -> scale {scale:0.000}");
        }
    }

    private static void SetScale(PouchModel model, float scale)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath(model));
        if (Mathf.Abs(importer.globalScale - scale) > 1e-4f)
        {
            importer.globalScale = scale;
            importer.SaveAndReimport();
        }
    }

    /// <summary>Width (X, across the rig) of the model's LOD0 as imported, in metres.</summary>
    private static float MeasureWidth(PouchModel model)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath(model)));
        try
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            var lod0 = renderers.Where(r => r.name.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase)).ToArray();
            var measured = lod0.Length > 0 ? lod0 : renderers;
            var bounds = measured[0].bounds;
            foreach (var renderer in measured)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            return bounds.size.x;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static void ConfigureModel(PouchModel model)
    {
        var importer = AssetImporter.GetAtPath(FbxPath(model)) as ModelImporter
                       ?? throw new Exception($"FBX not found: {FbxPath(model)}");
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.isReadable = false;
        importer.SaveAndReimport();

        foreach (var variant in model.Variants)
        {
            // DXT5 (the alpha is the reflection mask), as the game's own item albedo
            ImportTexture(AlbedoPath(model, variant), TextureImporterType.Default, srgb: true, AlbedoSize);
        }

        // Unity packs a normal map into DXT5nm (AG), the packing the game's shader reads
        ImportTexture(NormalPath(model), TextureImporterType.NormalMap, srgb: false, NormalSize);
        // grey strengths, no alpha: DXT1, as the game's own item specular map
        ImportTexture(SpecPath(model), TextureImporterType.Default, srgb: false, SpecSize);
    }

    private static void ImportTexture(string path, TextureImporterType type, bool srgb, int size)
    {
        var texture = AssetImporter.GetAtPath(path) as TextureImporter
                      ?? throw new Exception($"texture not found: {path}");
        texture.textureType = type;
        texture.maxTextureSize = size;
        texture.textureCompression = TextureImporterCompression.Compressed;
        texture.mipmapEnabled = true;
        texture.sRGBTexture = srgb;
        texture.alphaSource = TextureImporterAlphaSource.FromInput;
        texture.alphaIsTransparency = false;
        texture.SaveAndReimport();
    }

    /// <summary>
    /// Standard (Specular setup) only carries the maps into the bundle: the client moves the
    /// material onto the game's item shader, reading _MainTex, _BumpMap and _SpecGlossMap (as
    /// its _SpecMap). Built-in Standard is all a bundle made outside the game's SDK can hold.
    /// </summary>
    private static Material BuildMaterial(PouchModel model, string variant)
    {
        var shader = Shader.Find("Standard (Specular setup)")
                     ?? throw new Exception("Standard (Specular setup) shader not found");
        var material = new Material(shader)
        {
            name = $"{model.Name}_{variant}",
            mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath(model, variant)),
        };
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath(model)));
        material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_SpecGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SpecPath(model)));
        material.EnableKeyword("_SPECGLOSSMAP");
        material.SetFloat("_GlossMapScale", 0.15f); // nylon: a dull sheen, until the client swaps the shader

        Directory.CreateDirectory(GeneratedRoot);
        var path = $"{GeneratedRoot}/{material.name}.mat";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(material, path);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    /// <summary>The FBX hierarchy as plain objects, every renderer with the material.</summary>
    private static GameObject InstantiateModel(PouchModel model, Material material)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath(model));
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
        }

        return instance;
    }

    /// <summary>Box of the LOD0 meshes in the root's space.</summary>
    private static Bounds LocalBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>()
            .Where(r => r.name.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (renderers.Length == 0)
        {
            renderers = root.GetComponentsInChildren<MeshRenderer>();
        }

        if (renderers.Length == 0)
        {
            throw new Exception($"{root.name}: no mesh renderers");
        }

        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
        {
            bounds.Encapsulate(r.bounds);
        }

        return bounds; // the root stands at the origin, unrotated: world = root space
    }

    // --- prefab ---

    private static string BuildPrefab(PouchModel model, string variant)
    {
        var name = $"{model.Name}_{variant}";
        var material = BuildMaterial(model, variant);
        var root = InstantiateModel(model, material);
        root.name = name;
        try
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            var lod0 = renderers.Where(r => r.name.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
                .Cast<Renderer>().ToArray();
            var lod1 = renderers.Where(r => r.name.EndsWith("_LOD1", StringComparison.OrdinalIgnoreCase))
                .Cast<Renderer>().ToArray();
            if (lod0.Length == 0)
            {
                throw new Exception($"{name}: no _LOD0 mesh in {FbxPath(model)}");
            }

            // Unity already makes one for meshes named _LOD0/_LOD1; its thresholds are set here
            var group = root.GetComponent<LODGroup>() ?? root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(lod1.Length > 0
                ? new[] { new LOD(Lod0Height, lod0), new LOD(Lod1Height, lod1) }
                : new[] { new LOD(Lod1Height, lod0) });
            group.RecalculateBounds();

            var bounds = LocalBounds(root);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;

            var pivot = root.AddComponent<PreviewPivot>();
            pivot.pivotRotation = Quaternion.identity;
            pivot.scale = Vector3.one;
            pivot.Icon = new PreviewPivot.IconSettings
            {
                // unturned, the icon shows the -Z side - the straps; the front is +Z, seen a
                // little from the side
                rotation = Quaternion.Euler(0f, IconTurn, 0f),
                boundsScale = 1f,
                orthographic = true,
            };

            // on a cell: upright, facing out, the back of the pouch on the surface (the
            // pivot is the centre of the box, half the depth inside)
            var placer = root.AddComponent<ModPlacer>();
            var turn = Quaternion.Inverse(Quaternion.LookRotation(ModelOutward, ModelUp));
            var depth = Mathf.Abs(Vector3.Dot(bounds.size, ModelOutward));
            var centre = turn * bounds.center;
            placer.ModRotation = turn.eulerAngles;
            placer.ModPosition = new Vector3(-centre.x, -centre.y, depth / 2f - centre.z);
            placer.ModScale = Vector3.one;

            Directory.CreateDirectory(GeneratedRoot);
            var path = $"{GeneratedRoot}/{name}.prefab";
            AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[ModularVests] prefab {name}: size {bounds.size:F3}, mount {placer.ModPosition:F4} / " +
                      $"{placer.ModRotation:F1}, LOD0 {lod0.Length}, LOD1 {lod1.Length}");
            return path;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // --- bundles ---

    /// <summary>
    /// One bundle per colour (the prefab, its material and albedo), plus one per model with what
    /// every colour shares - the normal and the specular map - so they are shipped once. The
    /// colour bundles depend on it; the dependencies go to <see cref="DependenciesFile"/> for the
    /// server manifest, and the game loads the shared bundle first.
    /// </summary>
    private static void BuildBundles(List<string> prefabs, PouchModel[] models)
    {
        foreach (var name in AssetDatabase.GetAllAssetBundleNames())
        {
            AssetDatabase.RemoveAssetBundleName(name, forceRemove: true);
        }

        foreach (var path in prefabs)
        {
            AssetImporter.GetAtPath(path).SetAssetBundleNameAndVariant(
                BundlePrefix + Path.GetFileNameWithoutExtension(path) + ".bundle", "");
        }

        var shared = new HashSet<string>();
        foreach (var model in models)
        {
            var bundle = SharedBundle(model);
            shared.Add(bundle);
            AssetImporter.GetAtPath(NormalPath(model)).SetAssetBundleNameAndVariant(bundle, "");
            AssetImporter.GetAtPath(SpecPath(model)).SetAssetBundleNameAndVariant(bundle, "");
        }

        if (Directory.Exists(OutputDir + "/modularvests"))
        {
            Directory.Delete(OutputDir + "/modularvests", recursive: true);
        }

        Directory.CreateDirectory(OutputDir);
        var manifest = BuildPipeline.BuildAssetBundles(OutputDir, BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64) ?? throw new Exception("BuildAssetBundles returned null");

        // bundle -> its dependencies, one line each: "key<TAB>dep1,dep2"
        var lines = new List<string>();
        foreach (var bundle in manifest.GetAllAssetBundles())
        {
            var file = Path.Combine(OutputDir, bundle);
            var dependencies = manifest.GetAllDependencies(bundle);
            // only the model's own shared maps: anything else would mean an asset pulled in by mistake
            var foreign = dependencies.Where(d => !shared.Contains(d)).ToArray();
            if (foreign.Length > 0)
            {
                throw new Exception($"{bundle} depends on {string.Join(", ", foreign)}, which is not a shared maps bundle");
            }

            if (shared.Contains(bundle) && dependencies.Length > 0)
            {
                throw new Exception($"shared bundle {bundle} depends on {string.Join(", ", dependencies)}");
            }

            lines.Add(bundle + "\t" + string.Join(",", dependencies));
            Debug.Log($"[ModularVests] bundle {bundle}: {new FileInfo(file).Length / 1024} KB" +
                      (dependencies.Length > 0 ? " (needs " + string.Join(", ", dependencies) + ")" : ""));
        }

        File.WriteAllLines(Path.Combine(OutputDir, DependenciesFile), lines);
    }

    private static string SharedBundle(PouchModel model) => BundlePrefix + model.Name + "_maps.bundle";

    /// <summary>Written next to the bundles for build-bundles.ps1, which fills the server manifest from it.</summary>
    private const string DependenciesFile = "dependencies.txt";

    // --- previews ---

    private static void Render(GameObject target, Bounds bounds, Vector3 from, string file)
    {
        const int size = 512;
        var cameraObject = new GameObject("preview camera");
        var lightObject = new GameObject("preview light");
        var texture = new RenderTexture(size, size, 24);
        try
        {
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = bounds.extents.magnitude;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.25f, 0.3f, 0.35f);
            camera.transform.position = bounds.center + from * (bounds.extents.magnitude * 4f);
            camera.transform.LookAt(bounds.center, Mathf.Abs(Vector3.Dot(from, Vector3.up)) > 0.9f
                ? Vector3.forward
                : Vector3.up);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 10f;
            camera.targetTexture = texture;

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = camera.transform.rotation * Quaternion.Euler(20f, 20f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f);

            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(size, size, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            image.Apply();
            File.WriteAllBytes(file, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally
        {
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(lightObject);
            texture.Release();
        }
    }
}
