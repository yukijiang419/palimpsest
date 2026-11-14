using System;
using System.Collections.Generic;
using Caffeine.Gameplay.Config;
using UnityEngine;

namespace Caffeine.Gameplay.Player
{
    // A dropped coin: pops out, lands, then flies to the player when they come close.
    // With a Rigidbody on the coin (or its parent), physics handles the fall; otherwise the coin moves itself.
    [DisallowMultipleComponent]
    public sealed class CoinPickup : MonoBehaviour
    {
        private const float PlayerCenterHeight = 0.8f;
        private const float BobAmplitude = 0.05f;
        private const float BobFrequency = 2f;

        private GameplayConfig.CoinSettings settings;
        private PlayerWallet wallet;
        private Transform root;
        private Rigidbody body;
        private Collider[] colliders;
        private bool[] colliderEnabled;
        private readonly List<CoinPickup> ignoredCoins = new(10);
        private Action<CoinPickup> returnToPool;
        private Vector3 initialScale;
        private bool initialKinematic;
        private bool initialGravity;
        private bool active;
        private int value;
        private Vector3 velocity;
        private float groundY;
        private float collectableAt;
        private bool landed;
        private bool attracted;

        internal Transform Root => root;

        internal void Initialize(Transform coinRoot, Action<CoinPickup> release)
        {
            root = coinRoot;
            body = GetComponentInParent<Rigidbody>(true);
            colliders = root.GetComponentsInChildren<Collider>(true);
            colliderEnabled = new bool[colliders.Length];
            for (int index = 0; index < colliders.Length; index++)
            {
                colliderEnabled[index] = colliders[index].enabled;
            }

            initialScale = root.localScale;
            if (body != null)
            {
                initialKinematic = body.isKinematic;
                initialGravity = body.useGravity;
            }

            returnToPool = release;
        }

        public void Launch(PlayerWallet targetWallet, int coinValue, Vector3 launchVelocity, GameplayConfig.CoinSettings coinSettings)
        {
            if (root == null)
            {
                Rigidbody parentBody = GetComponentInParent<Rigidbody>(true);
                Initialize(parentBody != null ? parentBody.transform : transform, null);
            }

            wallet = targetWallet;
            value = coinValue;
            settings = coinSettings;
            collectableAt = Time.time + settings.PickupDelay;
            landed = false;
            attracted = false;
            active = true;
            saveId = Guid.NewGuid().ToString("N");
            velocity = launchVelocity;
            root.localScale = initialScale;
            RestorePhysics();
            root.gameObject.SetActive(true);

            if (body != null)
            {
                body.linearVelocity = launchVelocity;
                body.angularVelocity = UnityEngine.Random.insideUnitSphere * 10f;
                body.WakeUp();
                return;
            }

            Vector3 position = root.position;
            groundY = Physics.Raycast(position, Vector3.down, out RaycastHit hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : position.y - settings.SpawnHeight;
        }

        private void Update()
        {
            if (!active)
            {
                return;
            }

            if (wallet == null)
            {
                Release();
                return;
            }

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            Vector3 position = root.position;
            Vector3 target = wallet.transform.position + Vector3.up * PlayerCenterHeight;

            if (!attracted && Time.time >= collectableAt
                && (target - position).sqrMagnitude <= settings.MagnetRadius * settings.MagnetRadius)
            {
                StartAttraction();
            }

            if (attracted)
            {
                position = Vector3.MoveTowards(position, target, settings.MagnetSpeed * deltaTime);
                root.position = position;
                root.Rotate(Vector3.up, settings.SpinDegreesPerSecond * deltaTime, Space.World);
                if ((target - position).sqrMagnitude <= settings.CollectDistance * settings.CollectDistance)
                {
                    active = false;
                    wallet.AddCoins(value);
                    Release();
                }

                return;
            }

            if (body != null)
            {
                return;
            }

            root.Rotate(Vector3.up, settings.SpinDegreesPerSecond * deltaTime, Space.World);
            float restY = groundY + settings.HoverHeight;
            if (!landed)
            {
                velocity.y -= settings.Gravity * deltaTime;
                position += velocity * deltaTime;
                if (velocity.y < 0f && position.y <= restY)
                {
                    position.y = restY;
                    landed = true;
                }
            }
            else
            {
                position.y = restY + Mathf.Sin(Time.time * BobFrequency * Mathf.PI * 2f) * BobAmplitude;
            }

            root.position = position;
        }

        internal void IgnoreCollisionsWith(CoinPickup other)
        {
            SetIgnoreCollision(other, true);
            ignoredCoins.Add(other);
            other.ignoredCoins.Add(this);
        }

        private void SetIgnoreCollision(CoinPickup other, bool ignore)
        {
            foreach (Collider coinCollider in colliders)
            {
                foreach (Collider otherCollider in other.colliders)
                {
                    Physics.IgnoreCollision(coinCollider, otherCollider, ignore);
                }
            }
        }

        internal void Deactivate()
        {
            active = false;
            foreach (CoinPickup other in ignoredCoins)
            {
                if (other != null)
                {
                    SetIgnoreCollision(other, false);
                    other.ignoredCoins.Remove(this);
                }
            }

            ignoredCoins.Clear();
            wallet = null;
            settings = null;
            value = 0;
            velocity = Vector3.zero;
            groundY = 0f;
            collectableAt = 0f;
            landed = false;
            attracted = false;
            RestorePhysics();
            root.gameObject.SetActive(false);
        }

        private void RestorePhysics()
        {
            for (int index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = colliderEnabled[index];
            }

            if (body != null)
            {
                body.isKinematic = initialKinematic;
                body.useGravity = initialGravity;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
        }

        private void Release()
        {
            active = false;
            if (returnToPool != null)
            {
                returnToPool(this);
            }
            else
            {
                Destroy(root.gameObject);
            }
        }

        // Hand the coin over from physics to scripted movement so it can fly through walls and bodies.
        private void StartAttraction()
        {
            attracted = true;
            if (body == null)
            {
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            foreach (Collider coinCollider in colliders)
            {
                coinCollider.enabled = false;
            }
        }
        [Serializable] public sealed class SaveState { public string id; public Vector3 position; public int value; public float delay; }
        private string saveId;
        public SaveState CaptureState() => active ? new SaveState { id=saveId, position=root.position, value=value, delay=Mathf.Max(0f,collectableAt-Time.time) } : null;
        public void RestoreState(SaveState s, PlayerWallet target, GameplayConfig.CoinSettings config)
        {
            root.position=s.position; Launch(target,s.value,Vector3.zero,config); saveId=s.id;
            collectableAt=Time.time+s.delay;
        }

    }
}
