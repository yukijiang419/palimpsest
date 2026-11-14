using System;
using TMPro;
using UnityEngine;

namespace Caffeine.Gameplay.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class TypewriterText : MonoBehaviour
    {
        private TMP_Text label;
        private int characterCount;
        private float charactersPerSecond;
        private double startedAt;
        private Action completed;
        public bool IsPlaying { get; private set; }

        public void Play(string text, float speed, Action onCompleted = null)
        {
            Cancel();
            if (label == null) label = GetComponent<TMP_Text>();
            label.maxVisibleCharacters = 0;
            label.text = text ?? string.Empty;
            label.ForceMeshUpdate();
            characterCount = label.textInfo.characterCount;
            charactersPerSecond = Mathf.Max(1f, speed);
            startedAt = Time.unscaledTimeAsDouble;
            completed = onCompleted;
            IsPlaying = true;
            if (characterCount == 0) Complete();
        }

        // Returns true only when this call consumed an unfinished passage.
        public bool Complete()
        {
            if (!IsPlaying) return false;
            IsPlaying = false;
            label.maxVisibleCharacters = int.MaxValue;
            var callback = completed;
            completed = null;
            callback?.Invoke();
            return true;
        }

        public void Cancel()
        {
            IsPlaying = false;
            completed = null;
        }

        private void Update()
        {
            if (!IsPlaying) return;
            double visible = (Time.unscaledTimeAsDouble - startedAt) * charactersPerSecond;
            if (visible >= characterCount) Complete();
            else label.maxVisibleCharacters = (int)visible;
        }

        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();
    }
}
