using UnityEditor;
using UnityEngine;

namespace NuggetCreek.Editor
{
    /// <summary>
    /// Import settings for the game art under Resources/Sprites (cut from the art sheets by the
    /// local sprite tool): UI sprites without mipmaps, ASTC on Android. Creek backgrounds, top-down
    /// rivers and map panels keep their full size, the dredge tiers and the logo get 1024; everything
    /// else is capped at 512, the size the cutter exports.
    /// </summary>
    sealed class SpriteImportRules : AssetPostprocessor
    {
        const string Root = "Assets/Resources/Sprites/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root))
                return;
            var importer = (TextureImporter)assetImporter;
            bool background = assetPath.StartsWith(Root + "Creeks/") || assetPath.StartsWith(Root + "Map/") || assetPath.StartsWith(Root + "Rivers/");
            bool logo = assetPath.StartsWith(Root + "Identity/logo");
            bool dredge = assetPath.StartsWith(Root + "Dredge/");
            // Moving bands of the dredge slide their texture along, so it has to repeat.
            bool flow = dredge && assetPath.Contains("_flow_");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = !background;
            importer.wrapMode = flow ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = background ? 2048 : logo || dredge ? 1024 : 512;
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
