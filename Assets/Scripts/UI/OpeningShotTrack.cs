using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Caffeine.Gameplay.UI
{
    [TrackColor(0.68f, 0.51f, 0.29f)]
    [TrackClipType(typeof(OpeningShotClip))]
    [TrackBindingType(typeof(OpeningSequenceController))]
    public sealed class OpeningShotTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
            => ScriptPlayable<OpeningShotMixer>.Create(graph, inputCount);
    }

    public sealed class OpeningShotMixer : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var controller = playerData as OpeningSequenceController;
            if (controller == null) return;
            for (int i = 0; i < playable.GetInputCount(); i++)
            {
                if (playable.GetInputWeight(i) <= 0) continue;
                var clip = (ScriptPlayable<OpeningShotBehaviour>)playable.GetInput(i);
                controller.EvaluateShot(clip.GetBehaviour().shotIndex,
                    clip.GetDuration() > 0 ? (float)(clip.GetTime() / clip.GetDuration()) : 0, (float)clip.GetDuration());
            }
        }
    }
}
