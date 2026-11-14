using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Caffeine.Gameplay.UI
{
    public sealed class OpeningShotClip : PlayableAsset, ITimelineClipAsset
    {
        public int shotIndex;
        public ClipCaps clipCaps => ClipCaps.None;
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<OpeningShotBehaviour>.Create(graph);
            playable.GetBehaviour().shotIndex = shotIndex;
            return playable;
        }
    }

    public sealed class OpeningShotBehaviour : PlayableBehaviour
    {
        public int shotIndex;
    }
}
