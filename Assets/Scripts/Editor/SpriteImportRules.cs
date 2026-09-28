using UnityEditor;
using UnityEngine;

namespace NuggetCreek.Editor
{
    /// <summary>
    /// Import settings for the game art under Resources/Sprites (cut from the art sheets by the
    /// local sprite tool): UI sprites without mipmaps, ASTC on Android. Creek backgrounds and
    /// map panels keep their full size; everything else is capped at 512, the size the cutter exports.
    /// </summary>
    sealed class SpriteImportRules : AssetPostprocessor
    {
        const string Root = "Assets/Resources/Sprites/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root))
                return;
            var importer = (TextureImporter)assetImporter;
            bool background = assetPath.StartsWith(Root + "Creeks/") || assetPath.StartsWith(Root + "Map/");
            bool logo = assetPath.StartsWith(Root + "Identity/logo");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = !background;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = background ? 2048 : logo ? 1024 : 512;
            importer.textureCompression = TextureImporterCompression.Compressed;

            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = importer.maxTextureSize,
                format = background ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.ASTC_6x6,
            });
        }
    }
}
