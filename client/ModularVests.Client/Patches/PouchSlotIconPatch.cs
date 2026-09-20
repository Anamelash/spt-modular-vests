using System;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using EFT.Utilities;
using HarmonyLib;
using UnityEngine;

namespace ModularVests.Client.Patches
{
    /// <summary>
    /// The inspect window draws an empty slot over a background picked by the slot's name
    /// ("Slots/" + name, e.g. a magazine silhouette for mod_magazine). The game has none for
    /// mod_pouch_N, so the pouch cells stayed blank white; they get the mod's own picture.
    /// </summary>
    internal static class PouchSlotIconPatch
    {
        private const string ResourceName = "ModularVests.pouch_slot.png";

        /// <summary>A slot background of the game, to draw ours at the same size.</summary>
        private const string ReferenceSprite = "Slots/mod_magazine";

        private static Sprite _sprite;
        private static bool _failed;

        public static void Apply(Harmony harmony)
        {
            var target = PatchTargets.ModSlotView_Show ?? throw new InvalidOperationException("ModSlotView.Show not found");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(PouchSlotIconPatch), nameof(Postfix)));
        }

        private static void Postfix(ModSlotView __instance, Slot slot)
        {
            try
            {
                if (!PouchSlots.IsPouchSlot(slot))
                {
                    return;
                }

                var sprite = Sprite();
                if (sprite != null)
                {
                    __instance.SetSlotBackImage(sprite);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ModularVests] pouch slot icon: {ex}");
            }
        }

        private static Sprite Sprite()
        {
            if (_sprite != null || _failed)
            {
                return _sprite;
            }

            _failed = true; // one attempt: a broken resource is reported once
            using (var stream = typeof(PouchSlotIconPatch).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    Plugin.Log.LogError($"[ModularVests] pouch slot icon: resource {ResourceName} missing");
                    return null;
                }

                var bytes = new byte[stream.Length];
                var read = 0;
                while (read < bytes.Length)
                {
                    var n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0)
                    {
                        break;
                    }

                    read += n;
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                if (!texture.LoadImage(bytes))
                {
                    Plugin.Log.LogError("[ModularVests] pouch slot icon: the picture could not be read");
                    return null;
                }

                texture.name = "modularvests_pouch_slot";
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;

                // SetSlotBackImage sizes the image natively: as many units as a game slot picture
                var reference = ResourcesCache.Pop<Sprite>(ReferenceSprite);
                var pixelsPerUnit = reference != null && reference.rect.width > 0f
                    ? reference.pixelsPerUnit * texture.width / reference.rect.width
                    : 100f * texture.width / 125f;

                _sprite = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), pixelsPerUnit);
                _sprite.name = texture.name;
                UnityEngine.Object.DontDestroyOnLoad(texture);
                _failed = false;
                Plugin.Log.LogInfo($"[ModularVests] pouch slot icon loaded ({texture.width}px, sized like " +
                                   $"{(reference != null ? $"{ReferenceSprite} {reference.rect.width}px" : "a 125px slot")})");
                return _sprite;
            }
        }
    }
}
