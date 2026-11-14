using System;
using UnityEngine;

namespace Caffeine.Gameplay.Combat
{
    internal sealed class RaycastBuffer
    {
        private RaycastHit[] hits = new RaycastHit[32];

        public RaycastHit this[int index] => hits[index];

        public int Cast(Vector3 origin, Vector3 direction, float distance, int layerMask,
            QueryTriggerInteraction triggers)
        {
            while (true)
            {
                int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, layerMask, triggers);
                if (count < hits.Length)
                {
                    return count;
                }

                // A full buffer may omit the nearest hit, so repeat with more room.
                Array.Resize(ref hits, hits.Length * 2);
            }
        }
    }
}
