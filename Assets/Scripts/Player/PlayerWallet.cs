using System;
using Caffeine.Gameplay.Presentation;
using UnityEngine;

namespace Caffeine.Gameplay.Player
{
    // Coins the player has collected.
    [DisallowMultipleComponent]
    public sealed class PlayerWallet : MonoBehaviour
    {
        public int Coins { get; private set; }

        public event Action<int> CoinsChanged;


        public void AddCoins(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            Coins += amount;
            CoinsChanged?.Invoke(Coins);
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            FeedbackAudioController.Play(FeedbackSound.Coin);
        }

        public void RestoreCoins(int amount)
        {
            Coins = Mathf.Max(0, amount);
            CoinsChanged?.Invoke(Coins);
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || Coins < amount)
            {
                return false;
            }

            Coins -= amount;
            CoinsChanged?.Invoke(Coins);
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            return true;
        }
    }
}
