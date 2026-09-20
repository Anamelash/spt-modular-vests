using BepInEx.Configuration;
using UnityEngine;

namespace ModularVests.DevTools
{
    /// <summary>Which attached pouches the editor draws a bounding box for.</summary>
    internal enum PouchBoxesMode
    {
        Selected,
        SelectedAndMirror,
        Cluster,
        All,
    }

    /// <summary>F12 settings of the dev tools (their own config file).</summary>
    internal static class DevConfig
    {
        private const string Editor = "1. Bone editor";
        private const string EditorKeys = "4. Bone editor keys";
        private const string Layout = "2. Cells";
        private const string Surface = "3. Surface snap";

        public static ConfigEntry<bool> BoneEditorEnabled;
        public static ConfigEntry<bool> EditorDebugLog;
        public static ConfigEntry<float> MoveSnap;
        public static ConfigEntry<float> RotateSnap;
        public static ConfigEntry<float> MoveStep;
        public static ConfigEntry<float> RotateStep;
        public static ConfigEntry<float> FineFactor;

        public static ConfigEntry<float> CellWidth;
        public static ConfigEntry<float> CellHeight;
        public static ConfigEntry<bool> LiveMirror;
        public static ConfigEntry<bool> MeshDiagnostics;
        public static ConfigEntry<PouchBoxesMode> PouchBoxes;

        public static ConfigEntry<bool> SurfaceSnap;
        public static ConfigEntry<float> SurfaceOffset;
        public static ConfigEntry<float> SurfaceLift;

        public static ConfigEntry<KeyCode> SurfaceSnapToggle;
        public static ConfigEntry<KeyCode> NextCluster;
        public static ConfigEntry<KeyCode> AxisXModifier;
        public static ConfigEntry<KeyCode> AxisYModifier;
        public static ConfigEntry<KeyCode> AxisZModifier;
        public static ConfigEntry<KeyCode> RotateModifier;
        public static ConfigEntry<KeyCode> FineModifier;
        public static ConfigEntry<KeyCode> NudgePlus;
        public static ConfigEntry<KeyCode> NudgeMinus;
        public static ConfigEntry<KeyCode> ResetKey;

        public static ConfigEntry<KeyboardShortcut> SaveShortcut;
        public static ConfigEntry<KeyboardShortcut> CopyShortcut;
        public static ConfigEntry<KeyboardShortcut> PasteShortcut;
        public static ConfigEntry<KeyboardShortcut> UndoShortcut;

        public static void Bind(ConfigFile config)
        {
            BoneEditorEnabled = config.Bind(Editor, "Enabled", false, new ConfigDescription(
                "Lay out the pouch cells of a rig on the Modding screen by dragging their bones. " +
                "Changes are written to bones.json (and pouch mounts to mounts.json) next to the " +
                "plugin. Can be switched at any time.", null, Order(7)));
            EditorDebugLog = config.Bind(Editor, "Debug log", true, new ConfigDescription(
                "Write what the editor is doing (state, mouse presses, what was grabbed) to the " +
                "BepInEx log. Useful when a grab does not work.", null, Order(6)));
            MoveSnap = config.Bind(Editor, "Drag snap (m)", 0f, new ConfigDescription(
                "Dragging moves in steps of this size. 0 = smooth.",
                new AcceptableValueRange<float>(0f, 0.05f), Order(5)));
            RotateSnap = config.Bind(Editor, "Rotation snap (deg)", 0f, new ConfigDescription(
                "Dragging rotates in steps of this size. 0 = smooth.",
                new AcceptableValueRange<float>(0f, 90f), Order(4)));
            MoveStep = config.Bind(Editor, "Nudge step (m)", 0.005f, new ConfigDescription(
                "Distance per nudge key press.", new AcceptableValueRange<float>(0.0005f, 0.05f), Order(3)));
            RotateStep = config.Bind(Editor, "Nudge rotation step (deg)", 5f, new ConfigDescription(
                "Angle per nudge key press.", new AcceptableValueRange<float>(0.5f, 45f), Order(2)));
            FineFactor = config.Bind(Editor, "Fine factor", 0.2f, new ConfigDescription(
                "Drag and nudge multiplier while the fine modifier is held.",
                new AcceptableValueRange<float>(0.01f, 1f), Order(1)));

            CellWidth = config.Bind(Layout, "Cell width (m)", 0.065f, new ConfigDescription(
                "Size of one pouch cell across: the cell frame, corner sampling of the surface normal, " +
                "and the step of 'Arrange cluster'.", new AcceptableValueRange<float>(0.02f, 0.2f), Order(4)));
            CellHeight = config.Bind(Layout, "Cell height (m)", 0.09f, new ConfigDescription(
                "Size of one pouch cell down.", new AcceptableValueRange<float>(0.02f, 0.2f), Order(3)));
            LiveMirror = config.Bind(Layout, "Live mirror", false, new ConfigDescription(
                "Every edit of a cell is mirrored onto its cell of the paired cluster at once.",
                null, Order(2)));
            PouchBoxes = config.Bind(Layout, "Pouch boxes", PouchBoxesMode.SelectedAndMirror, new ConfigDescription(
                "Which attached pouches get their bounding box drawn in the editor: the pouch on the " +
                "selected cell, it and its mirror, every pouch of the selected cluster, or all of them. " +
                "Switched on the editor panel; kept here between game starts.",
                null, new ConfigurationManagerAttributes { Browsable = false }));
            MeshDiagnostics = config.Bind(Layout, "Mesh diagnostics", true, new ConfigDescription(
                "Log, once per model, the colliders and meshes of a rig opened in the editor and whether " +
                "their geometry can be read (for the surface snap).", null, Order(1)));

            SurfaceSnap = config.Bind(Surface, "Surface snap", false, new ConfigDescription(
                "While on, every edit keeps the bone on the surface of the rig's mesh or does not " +
                "happen: dragging follows the surface under the cursor, the X/Y arms slide along it, " +
                "Z is locked, rotation is roll around the surface normal.", null, Order(3)));
            SurfaceOffset = config.Bind(Surface, "Surface offset (m)", 0f, new ConfigDescription(
                "Gap between the surface and a snapped cell (the cell's origin is on the surface at 0).",
                new AcceptableValueRange<float>(0f, 0.03f), Order(2)));
            SurfaceLift = config.Bind(Surface, "Reproject reach (m)", 0.05f, new ConfigDescription(
                "How far above and below a point the surface is looked for when a bone is put back on it.",
                new AcceptableValueRange<float>(0.005f, 0.2f), Order(1)));

            // Arrow keys rotate the preview on the Modding screen and Escape closes it:
            // the defaults stay clear of both.
            SurfaceSnapToggle = config.Bind(EditorKeys, "Surface snap toggle", KeyCode.G, Desc(
                "Switch the surface snap on and off.", 13));
            NextCluster = config.Bind(EditorKeys, "Next cluster", KeyCode.Tab, Desc(
                "Select the same cell in the next cluster (with Shift: the previous one). " +
                "Keys 1-4 select a cell of the current cluster.", 12));
            AxisXModifier = config.Bind(EditorKeys, "Axis X modifier", KeyCode.X, Desc(
                "Held while dragging or nudging: constrain to the X axis (red).", 11));
            AxisYModifier = config.Bind(EditorKeys, "Axis Y modifier", KeyCode.Y, Desc(
                "Held while dragging or nudging: constrain to the Y axis (green).", 10));
            AxisZModifier = config.Bind(EditorKeys, "Axis Z modifier", KeyCode.Z, Desc(
                "Held while dragging or nudging: constrain to the Z axis (blue).", 9));
            RotateModifier = config.Bind(EditorKeys, "Rotate modifier", KeyCode.LeftAlt, Desc(
                "Held: rotate instead of move — around the held axis, or around the view axis.", 8));
            FineModifier = config.Bind(EditorKeys, "Fine modifier", KeyCode.LeftShift, Desc(
                "Held: the fine factor applies.", 7));
            NudgePlus = config.Bind(EditorKeys, "Nudge +", KeyCode.Equals, Desc(
                "Step the selected bone along/around the held axis.", 6));
            NudgeMinus = config.Bind(EditorKeys, "Nudge -", KeyCode.Minus, Desc(
                "Step the selected bone along/around the held axis, backwards.", 5));
            ResetKey = config.Bind(EditorKeys, "Reset slot", KeyCode.R, Desc(
                "Put the selected bone back to the default layout (right-click on a dot does the same).", 4));

            SaveShortcut = config.Bind(EditorKeys, "Save", new KeyboardShortcut(KeyCode.S, KeyCode.LeftControl),
                Desc("", 3));
            CopyShortcut = config.Bind(EditorKeys, "Copy pose", new KeyboardShortcut(KeyCode.C, KeyCode.LeftControl),
                Desc("", 2));
            PasteShortcut = config.Bind(EditorKeys, "Paste pose", new KeyboardShortcut(KeyCode.V, KeyCode.LeftControl),
                Desc("", 1));
            UndoShortcut = config.Bind(EditorKeys, "Undo", new KeyboardShortcut(KeyCode.Z, KeyCode.LeftControl),
                Desc("", 0));
        }

        private static ConfigDescription Desc(string text, int order) => new ConfigDescription(text, null, Order(order));

        private static object Order(int order) => new ConfigurationManagerAttributes { Order = order };

        /// <summary>Recognised by ConfigurationManager by name; only the fields used are declared.</summary>
        private sealed class ConfigurationManagerAttributes
        {
#pragma warning disable 0414
            public int? Order;
            public bool? Browsable;
#pragma warning restore 0414
        }
    }
}
