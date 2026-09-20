using System;
using BepInEx.Configuration;
using UnityEngine;

namespace ModularVests.Client
{
    /// <summary>F12 settings (BepInEx ConfigurationManager).</summary>
    internal static class ClientConfig
    {
        private const string General = "1. General";
        private const string Models = "2. Pouch models";

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> LockPouchesInRaid;
        public static ConfigEntry<int> RigWindowWidth;

        public static ConfigEntry<bool> SelfTestOnLoad;
        public static ConfigEntry<bool> DebugLog;

        public static ConfigEntry<bool> UseGameShader;
        public static ConfigEntry<float> Specular;
        public static ConfigEntry<float> Gloss;
        public static ConfigEntry<int> IconGeneration;
        public static ConfigEntry<bool> RedrawIcons;

        /// <summary>Pressed in F12: the pouch icons are to be drawn again.</summary>
        public static event Action IconsRedrawRequested;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(General, "Enabled", true, new ConfigDescription(
                "Master switch: pouch grids in the rig window, pouch bones on the model, raid rules. " +
                "Takes effect on the next game start.", null, Order(6)));
            LockPouchesInRaid = config.Bind(General, "Lock pouches in raid", true, new ConfigDescription(
                "Pouches cannot be attached to or detached from a rig during a raid. " +
                "Their contents stay accessible either way.", null, Order(5)));
            RigWindowWidth = config.Bind(General, "Rig window width (cells)", 6, new ConfigDescription(
                "The rig window lays the pouch grids out in rows no wider than this, each row centred. " +
                "Takes effect the next time a rig window opens.", new AcceptableValueRange<int>(2, 16), Order(4)));

            SelfTestOnLoad = config.Bind(General, "Patch self-test on load", true, new ConfigDescription(
                "Resolve every patch target at startup and report the ones that are missing.",
                null, Order(2)));
            DebugLog = config.Bind(General, "Debug log", false, new ConfigDescription(
                "Write where the pouch bones of each rig model went (preview and character) to the " +
                "BepInEx log.", null, Order(1)));

            UseGameShader = config.Bind(Models, "Use game shader", true, new ConfigDescription(
                "Draw the mod's pouch models with the shader of the game's own items, so they look like " +
                "the rest of the gear on the rig, in the inspect window and on their icons. " +
                "Takes effect on the next game start.", null, Order(5)));
            Specular = config.Bind(Models, "Specular", 0.12f, new ConfigDescription(
                "How much light the fabric reflects, for pouch models without a specular map of their own " +
                "(the mod's models have one). Changes apply at once; redraw the icons to see them there.",
                new AcceptableValueRange<float>(0f, 1f), Order(4)));
            Gloss = config.Bind(Models, "Gloss", 0.25f, new ConfigDescription(
                "How sharp the reflections are, for pouch models without a specular map of their own.",
                new AcceptableValueRange<float>(0f, 1f), Order(3)));
            IconGeneration = config.Bind(Models, "Icon generation", 0, new ConfigDescription(
                "Bumped by the redraw button; part of the icon cache key of the mod's pouches.",
                null, new ConfigurationManagerAttributes { Browsable = false }));
            RedrawIcons = config.Bind(Models, "Redraw pouch icons", false, new ConfigDescription(
                "Throws away the cached icons of the mod's items - rigs and pouches - and of whatever " +
                "holds them, so the game draws them again. Icons of other items are left alone. Open " +
                "windows show the new icons once they are reopened.",
                null, new ConfigurationManagerAttributes
                {
                    Order = 1,
                    HideDefaultButton = true,
                    CustomDrawer = DrawRedrawButton,
                }));
        }

        private static void DrawRedrawButton(ConfigEntryBase entry)
        {
            if (GUILayout.Button("Redraw", GUILayout.ExpandWidth(true)))
            {
                IconGeneration.Value++;
                IconsRedrawRequested?.Invoke();
            }
        }

        private static object Order(int order) => new ConfigurationManagerAttributes { Order = order };

        /// <summary>Recognised by ConfigurationManager by name; only the fields used are declared.</summary>
        private sealed class ConfigurationManagerAttributes
        {
#pragma warning disable 0414
            public int? Order;
            public bool? Browsable;
            public bool? HideDefaultButton;
            public Action<ConfigEntryBase> CustomDrawer;
#pragma warning restore 0414
        }
    }
}
