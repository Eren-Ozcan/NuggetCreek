using UnityEditor;
using UnityEngine;

namespace NuggetCreek.Editor
{
    /// <summary>
    /// Import settings for the audio under Resources/Audio (from the private pictures repo):
    /// music loops stream as Vorbis so they cost little memory; short effects are decoded on
    /// load so they start without delay. Mono throughout, the game has no stereo field.
    /// </summary>
    sealed class AudioImportRules : AssetPostprocessor
    {
        const string Root = "Assets/Resources/Audio/";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root))
                return;
            var importer = (AudioImporter)assetImporter;
            bool music = assetPath.StartsWith(Root + "Music/");
            importer.forceToMono = true;
            importer.loadInBackground = music;
            importer.defaultSampleSettings = new AudioImporterSampleSettings
            {
                loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad,
                compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM,
                quality = music ? 0.6f : 1f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
            };
        }
    }
}
