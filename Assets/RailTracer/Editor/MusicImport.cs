using UnityEditor;
using UnityEngine;

namespace RailTracer
{
    // Music in Resources/Audio streams from disk (a long track would otherwise be decompressed whole into memory).
    class MusicImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/RailTracer/Resources/Audio/")) return;
            var a = (AudioImporter)assetImporter;
            var s = a.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            a.defaultSampleSettings = s;
        }
    }
}
