using UnityEditor;
using UnityEngine;

namespace RailTracer
{
    // Music in Resources/Audio streams from disk; sound effects in Audio/Sfx don't (a long track would otherwise be decompressed whole into memory).
    class MusicImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/RailTracer/Resources/Audio/")) return;
            var a = (AudioImporter)assetImporter;
            var s = a.defaultSampleSettings;
            // Sound effects (Audio/Sfx) are short and must play at once: decompressed into memory instead.
            s.loadType = assetPath.Contains("/Audio/Sfx/") ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.Streaming;
            a.defaultSampleSettings = s;
        }
    }
}
