using System.Collections;
using System.Collections.Generic;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Player;
using UnityEngine;
using Caffeine.Gameplay.Config;

namespace Caffeine.Gameplay.Enemy
{
    // Minion kills break the shield. Body actions share one slot; paper effects run independently.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HealthShield))]
    public sealed class BossController : MonoBehaviour
    {
        public static BossController Current { get; private set; }

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        [Header("Paper rain")]
        [SerializeField] private Material paperMaterial;

        [Header("Paper ring (after the shield breaks)")]
        [SerializeField, Tooltip("Where sprayed paper leaves the printer.")]
        private Transform paperOutlet;
        [SerializeField] private Material wallMaterial;

        [Header("Minions")]
        [SerializeField] private EnemyController[] minionPrefabs = new EnemyController[0];
        [SerializeField] private Transform[] minionSpawnPoints = new Transform[0];

        [Header("Animation (optional)")]
        [SerializeField] private Animator animator;
        [SerializeField] private string introTrigger = "Intro";
        [SerializeField] private string attackTrigger = "Attack";
        [SerializeField] private string summonTrigger = "Summon";
        [SerializeField] private string meleeTrigger = "Melee";
        [SerializeField] private string sprayTrigger = "Spray";
        [SerializeField] private string hitTrigger = "Hit";
        [SerializeField] private string shieldBrokenTrigger = "ShieldBroken";
        [SerializeField] private string deathTrigger = "Die";
        [SerializeField, Tooltip("Bool, true once the shield is broken.")]
        private string vulnerableBool = "Vulnerable";

        private readonly List<EnemyController> minions = new();
        private GameplayConfig.BossSettings settings;
        private HealthShield health;
        private EnemySystem enemySystem;
        private DamageSystem damageSystem;
        private Material warningMaterial;
        private MaterialPropertyBlock warningBlock;
        private readonly List<GameObject> attackEffects = new();
        private Transform player;
        private HealthShield playerHealth;
        private bool active;
        private bool shieldBroken;
        private float nextAttackAt;
        private float nextSummonAt;
        private float nextMeleeAt;
        private bool ringNext = true;
        private Vector3 groundPosition;
        private enum BodyAction { None, Intro, Rain, Spray, Slam, Summon, Hit, ShieldBroken }

        private BodyAction bodyAction;
        private Coroutine bodyRoutine;
        private GameObject slamWarning;
        private bool pendingHit;
        private bool ringActive;
        private float bodyEndsAt;
        private readonly Dictionary<string, float> animationLengths = new();

        public HealthShield Health => health;
        public float HitRadius => settings.HitRadius;
        public bool IsAlive => health != null && health.IsInitialized && !health.IsDead;
        public bool IsVulnerable => IsAlive && health.CurrentShield <= 0f;

        private void Awake()
        {
            settings = ConfigSystem.Instance.Config.Boss;
            health = GetComponent<HealthShield>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        private void OnEnable()
        {
            Current = this;
            health.VitalsChanged += HandleVitalsChanged;
            health.Died += HandleDied;
        }

        private void OnDisable()
        {
            StopAttacks();
            if (Current == this)
            {
                Current = null;
            }

            health.VitalsChanged -= HandleVitalsChanged;
            health.Died -= HandleDied;
        }

        private void Start()
        {
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                {
                    animationLengths[clip.name] = clip.length;
                }
            }
            enemySystem = FindFirstObjectByType<EnemySystem>();
            damageSystem = FindFirstObjectByType<DamageSystem>();
            warningMaterial = Resources.Load<Material>("BossLaserWarning");
            warningBlock = new MaterialPropertyBlock();
            var input = FindFirstObjectByType<PlayerInputReader>();
            if (input != null)
            {
                player = input.transform;
                playerHealth = input.GetComponent<HealthShield>();
            }
            if (enemySystem == null || damageSystem == null || player == null)
            {
                Debug.LogError("BossController needs an EnemySystem, DamageSystem and player in the scene.", this);
            }

            // Hide above the arena until the fight starts.
            groundPosition = transform.position;
            if (settings.JumpIn)
            {
                transform.position = groundPosition + Vector3.up * settings.JumpInHeight;
            }
        }

        private void Update()
        {
            if (Caffeine.Gameplay.Persistence.SaveGameService.Restoring) return;
            if (!IsAlive || player == null || (playerHealth != null && playerHealth.IsDead))
            {
                return;
            }

            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            if (!active)
            {
                if (toPlayer.magnitude > settings.ActivationRange)
                {
                    return;
                }

                var tutorial = FindFirstObjectByType<Caffeine.Gameplay.UI.TutorialPresenter>();
                if (tutorial != null && !tutorial.TryBeginCombat())
                {
                    return;
                }

                active = true;
                float introEnd = Time.time + (settings.JumpIn ? settings.IntroDuration : 0f);
                nextAttackAt = introEnd + settings.AttackInterval * .5f;
                nextSummonAt = introEnd + settings.FirstSummonDelay;
                nextMeleeAt = introEnd;
                if (settings.JumpIn)
                {
                    StartBodyAction(BodyAction.Intro, JumpIn(), null, "Printer_Intro", settings.IntroDuration, settings.JumpInFallTime);
                }
            }

            // The slam area is marked in front of the Boss, so it holds still while winding up.
            if (toPlayer.sqrMagnitude > .01f && bodyAction != BodyAction.Slam)
            {
                Quaternion facing = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, settings.TurnSpeed * Time.deltaTime);
            }

            if (bodyAction != BodyAction.None)
            {
                return;
            }
            if (pendingHit)
            {
                pendingHit = false;
                StartBodyAction(BodyAction.Hit, Feedback(), hitTrigger, "Printer_Hit", .4f);
                return;
            }

            if (!ringActive && Time.time >= nextAttackAt)
            {
                bool ring = shieldBroken && ringNext;
                ringNext = !ring;
                // A ring keeps the player busy until its walls fall.
                nextAttackAt = Time.time + (ring ? RingDuration : 0f)
                    + (shieldBroken ? settings.BrokenAttackInterval : settings.AttackInterval);
                StartBodyAction(ring ? BodyAction.Spray : BodyAction.Rain,
                    ring ? PaperRing() : PaperRain(), ring ? sprayTrigger : attackTrigger,
                    ring ? "Printer_Spray" : "Printer_PaperRain", settings.AttackWindup + (ring ? settings.SprayTime : 0f));
                return;
            }

            if (Time.time >= nextMeleeAt && toPlayer.magnitude <= settings.HitRadius + settings.MeleeRange)
            {
                nextMeleeAt = Time.time + settings.MeleeCooldown;
                StartBodyAction(BodyAction.Slam, Slam(), meleeTrigger, "Printer_Slam", settings.MeleeWindup + .5f);
                return;
            }

            // One wave at a time: a new wave only after every minion of the last one is dead.
            if (!shieldBroken && Time.time >= nextSummonAt && AliveMinions() == 0)
            {
                StartBodyAction(BodyAction.Summon, Summon(), summonTrigger, "Printer_Summon", settings.SummonWindup);
            }
        }

        // Falls with gravity-like easing onto the spawn point; the Intro (landing) animation starts on touchdown.
        private IEnumerator JumpIn()
        {
            Vector3 start = groundPosition + Vector3.up * settings.JumpInHeight;
            for (float t = 0f; t < settings.JumpInFallTime; t += Time.deltaTime)
            {
                float k = t / settings.JumpInFallTime;
                transform.position = Vector3.Lerp(start, groundPosition, k * k);
                yield return null;
            }
            transform.position = groundPosition;
            SetTrigger(introTrigger);
        }

        // Called by the player's melee system after a hit lands on the Boss' health.
        public void PlayHit()
        {
            if (IsAlive && bodyAction != BodyAction.Hit)
            {
                pendingHit = true;
            }
        }

        private void StartBodyAction(BodyAction action, IEnumerator routine, string trigger,
            string clipName, float minimumDuration, float animationDelay = 0f)
        {
            bodyAction = action;
            float duration = minimumDuration;
            if (animationLengths.TryGetValue(clipName, out float clipLength))
            {
                float speed = animator != null ? Mathf.Abs(animator.speed) : 1f;
                duration = Mathf.Max(duration, animationDelay + clipLength / Mathf.Max(.001f, speed) + .15f);
            }
            bodyEndsAt = Time.time + duration;
            if (trigger != null)
            {
                SetTrigger(trigger);
            }
            bodyRoutine = StartCoroutine(RunBodyAction(routine));
        }

        private IEnumerator RunBodyAction(IEnumerator routine)
        {
            yield return routine;
            while (Time.time < bodyEndsAt)
            {
                yield return null;
            }
            bodyAction = BodyAction.None;
            bodyRoutine = null;
        }

        private static IEnumerator Feedback()
        {
            yield return null;
        }

        private void CancelBodyAction()
        {
            if (bodyRoutine != null)
            {
                StopCoroutine(bodyRoutine);
            }
            bodyRoutine = null;
            bodyAction = BodyAction.None;
            if (slamWarning != null)
            {
                RemoveEffect(slamWarning);
                slamWarning = null;
            }
            // A cancelled trigger must not fire after the shield-break animation.
            foreach (string trigger in new[] { introTrigger,
                attackTrigger,
                sprayTrigger,
                meleeTrigger,
                summonTrigger,
                hitTrigger,
                shieldBrokenTrigger })
            {
                if (HasParameter(trigger, AnimatorControllerParameterType.Trigger))
                {
                    animator.ResetTrigger(trigger);
                }
            }
        }

        private void StopAttacks()
        {
            CancelBodyAction();
            StopAllCoroutines();
            pendingHit = false;
            ringActive = false;
            foreach (GameObject piece in attackEffects)
            {
                if (piece != null)
                {
                    Destroy(piece);
                }
            }
            attackEffects.Clear();
        }

        private float RingDuration => settings.AttackWindup + settings.SprayTime + settings.WallRiseTime + settings.WallLifetime;

        // Opens the scanner lid, then sheets flutter down one after another onto warning circles:
        // the first on the player, the rest scattered around them.
        private IEnumerator PaperRain()
        {
            if (settings.AttackWindup > 0f)
            {
                yield return new WaitForSeconds(settings.AttackWindup);
            }

            if (!IsAlive || player == null)
            {
                yield break;
            }

            int count = settings.PaperCount + (shieldBroken ? settings.BrokenExtraPaper : 0);
            StartCoroutine(Drops(player.position, settings.PaperSpread, count));
        }

        private IEnumerator Drops(Vector3 center, float spread, int count)
        {
            var targets = new List<Vector3> { player.position };
            float spacing = Mathf.Min(settings.PaperSpacing, spread * .6f);
            for (int i = 1; i < Mathf.Max(1, count); i++)
            {
                Vector3 candidate = center;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    candidate = center + Random.insideUnitSphere.Flat().normalized * Random.Range(0f, spread);
                    if (targets.TrueForAll(other => (other - candidate).Flat().magnitude >= spacing))
                    {
                        break;
                    }
                }
                targets.Add(candidate);
            }

            foreach (Vector3 point in targets)
            {
                if (!IsAlive)
                {
                    yield break;
                }

                StartCoroutine(PaperDrop(new Vector3(point.x, groundPosition.y, point.z)));
                if (settings.DropInterval > 0f)
                {
                    yield return new WaitForSeconds(settings.DropInterval);
                }
            }
        }

        // The circle fills as the sheet falls; it lands exactly when the circle is full, and only a
        // player still inside the circle then is hit.
        private IEnumerator PaperDrop(Vector3 target)
        {
            GameObject warning = Effect(PrimitiveType.Quad, warningMaterial, "Paper warning");
            warning.transform.SetPositionAndRotation(target + Vector3.up * .03f, Quaternion.Euler(90f, 0f, 0f));
            warning.transform.localScale = Vector3.one * settings.PaperRadius * 2f;
            Renderer warningRenderer = warning.GetComponent<Renderer>();

            GameObject sheet = Sheet("Falling paper", 2f);
            float spin = Random.Range(-1f, 1f) * 180f, phase = Random.value * 6f;
            for (float t = 0f; t < settings.WarningTime; t += Time.deltaTime)
            {
                float k = t / settings.WarningTime;
                SetWarning(warningRenderer, k, 1f);
                // Paper side-slips and tumbles as it falls, settling onto the centre of the circle.
                float sway = (1f - k) * 1.2f;
                Vector3 drift = new(Mathf.Sin(phase + t * 4f) * sway, 0f, Mathf.Cos(phase + t * 3f) * sway);
                sheet.transform.SetPositionAndRotation(target + drift + Vector3.up * Mathf.Lerp(
                    settings.PaperDropHeight,
                    .05f,
                    k * k),
                    Quaternion.Euler(Mathf.Sin(phase + t * 5f) * 40f * (1f - k),
                        spin * t,
                        Mathf.Cos(phase + t * 4f) * 30f * (1f - k)));
                yield return null;
            }
            sheet.transform.SetPositionAndRotation(target + Vector3.up * .05f,
                Quaternion.Euler(0f, spin * settings.WarningTime, 0f));
            RemoveEffect(warning);
            if (TouchesPlayer(target, settings.PaperRadius))
            {
                HurtPlayer(settings.PaperDamage);
            }

            // The landed sheet lies there briefly before it is gone.
            yield return new WaitForSeconds(1.2f);
            RemoveEffect(sheet);
        }

        // Marks a ring on the floor around the player while paper sprays out of the printer, then
        // raises a closed circle of paper walls there and rains paper into it until the walls fall.
        private IEnumerator PaperRing()
        {
            if (settings.AttackWindup > 0f)
            {
                yield return new WaitForSeconds(settings.AttackWindup);
            }

            if (!IsAlive || player == null)
            {
                yield break;
            }

            ringActive = true;

            Vector3 center = player.position.Flat() + Vector3.up * groundPosition.y;
            int segments = Mathf.Max(6, Mathf.CeilToInt(2f * Mathf.PI * settings.RingRadius / Mathf.Max(.5f, settings.WallSegment)));
            // Chords of the circle, slightly overlapped so the ring has no gaps.
            float chord = 2f * settings.RingRadius * Mathf.Sin(Mathf.PI / segments) + settings.WallThickness;
            var walls = new List<(Vector3 position, Quaternion rotation)>();
            var outline = new List<Renderer>();
            for (int i = 0; i < segments; i++)
            {
                Quaternion rotation = Quaternion.Euler(0f, 360f * (i + .5f) / segments, 0f);
                Vector3 position = center + rotation * Vector3.forward * settings.RingRadius;
                walls.Add((position, rotation));
                GameObject mark = Effect(PrimitiveType.Quad, warningMaterial, "Ring warning");
                mark.transform.SetPositionAndRotation(position + Vector3.up * .03f,
                    rotation * Quaternion.Euler(90f, 0f, 0f));
                mark.transform.localScale = new Vector3(chord, settings.WallThickness * 2.5f, 1f);
                outline.Add(mark.GetComponent<Renderer>());
            }

            Vector3 outlet = paperOutlet != null ? paperOutlet.position : transform.position + Vector3.up * 1.5f;
            for (int i = 0; i < segments; i++)
            {
                StartCoroutine(SprayedSheet(outlet, walls[i].position, settings.SprayTime * i / segments * .6f));
            }

            for (float t = 0f; t < settings.SprayTime; t += Time.deltaTime)
            {
                foreach (Renderer mark in outline)
                {
                    SetWarning(mark, t / settings.SprayTime, 1f);
                }

                yield return null;
            }
            foreach (Renderer mark in outline)
            {
                RemoveEffect(mark.gameObject);
            }

            if (!IsAlive)
            {
                yield break;
            }

            // Anyone standing on the ring line when it erupts is struck once.
            float band = Mathf.Abs((player.position - center).Flat().magnitude - settings.RingRadius);
            if (band <= settings.WallThickness * .5f + .5f)
            {
                HurtPlayer(settings.WallDamage);
            }

            foreach ((Vector3 position, Quaternion rotation) in walls)
            {
                StartCoroutine(PaperWall(position, rotation, chord));
            }

            StartCoroutine(RingVolleys(center));
        }

        private IEnumerator RingVolleys(Vector3 center)
        {
            yield return new WaitForSeconds(settings.WallRiseTime + .6f);
            for (int volley = 0; volley < settings.RingVolleys && IsAlive; volley++)
            {
                while (bodyAction != BodyAction.None && IsAlive)
                {
                    yield return null;
                }
                if (!IsAlive || player == null)
                {
                    yield break;
                }

                StartBodyAction(BodyAction.Rain, RingVolley(center), attackTrigger, "Printer_PaperRain", settings.AttackWindup);
                yield return new WaitForSeconds(settings.RingVolleyInterval);
            }
            ringActive = false;
        }

        private IEnumerator RingVolley(Vector3 center)
        {
            if (settings.AttackWindup > 0f)
            {
                yield return new WaitForSeconds(settings.AttackWindup);
            }
            if (IsAlive && player != null)
            {
                StartCoroutine(Drops(center, settings.RingRadius - 1f, settings.RingVolleySize));
            }
        }

        private IEnumerator SprayedSheet(Vector3 from, Vector3 to, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            GameObject sheet = Sheet("Sprayed paper", 1f);
            float flight = Mathf.Max(.1f, settings.SprayTime - delay);
            Vector3 spin = Random.insideUnitSphere * 720f;
            for (float t = 0f; t < flight; t += Time.deltaTime)
            {
                float k = t / flight;
                sheet.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 4f;
                sheet.transform.rotation = Quaternion.Euler(spin * t);
                yield return null;
            }
            RemoveEffect(sheet);
        }

        // A stack of paper slabs that rises from the floor, blocks movement, then sinks away.
        private IEnumerator PaperWall(Vector3 position, Quaternion rotation, float length)
        {
            var wall = new GameObject("Paper wall");
            wall.transform.SetPositionAndRotation(position, rotation);
            attackEffects.Add(wall);
            BoxCollider block = wall.AddComponent<BoxCollider>();
            block.size = new Vector3(length, settings.WallHeight, settings.WallThickness);
            block.center = Vector3.up * settings.WallHeight * .5f;

            var body = new GameObject("Stack").transform;
            body.SetParent(wall.transform, false);
            const int slabs = 7;
            for (int i = 0; i < slabs; i++)
            {
                GameObject slab = Slab(wallMaterial != null ? wallMaterial : paperMaterial);
                slab.transform.SetParent(body, false);
                slab.transform.localPosition = new Vector3(Random.Range(-.06f, .06f),
                    (i + .5f) * settings.WallHeight / slabs,
                    Random.Range(-.04f, .04f));
                slab.transform.localRotation = Quaternion.Euler(0f, Random.Range(-3f, 3f), Random.Range(-1.5f, 1.5f));
                slab.transform.localScale = new Vector3(length,
                    settings.WallHeight / slabs * .96f,
                    settings.WallThickness * Random.Range(.85f, 1f));
            }

            for (float t = 0f; t < settings.WallRiseTime; t += Time.deltaTime)
            {
                body.localScale = new Vector3(1f, Mathf.SmoothStep(0f, 1f, t / settings.WallRiseTime), 1f);
                yield return null;
            }
            body.localScale = Vector3.one;
            yield return new WaitForSeconds(settings.WallLifetime);

            block.enabled = false;
            for (float t = 0f; t < .5f; t += Time.deltaTime)
            {
                body.localScale = new Vector3(1f, 1f - t / .5f, 1f);
                yield return null;
            }
            RemoveEffect(wall);
        }

        // Rears back with the lid open while the slam area fills on the floor in front, then lunges
        // and slams it shut. Only a player still inside the marked area is hit.
        private IEnumerator Slam()
        {
            Vector3 forward = transform.forward.Flat().normalized;
            Vector3 area = transform.position.Flat() + Vector3.up * groundPosition.y
                + forward * (settings.HitRadius + settings.SlamRadius * .7f);
            GameObject mark = Effect(PrimitiveType.Quad, warningMaterial, "Slam warning");
            slamWarning = mark;
            mark.transform.SetPositionAndRotation(area + Vector3.up * .04f, Quaternion.Euler(90f, 0f, 0f));
            mark.transform.localScale = Vector3.one * settings.SlamRadius * 2f;
            Renderer markRenderer = mark.GetComponent<Renderer>();
            for (float t = 0f; t < settings.MeleeWindup; t += Time.deltaTime)
            {
                SetWarning(markRenderer, t / settings.MeleeWindup, 1f);
                yield return null;
            }
            RemoveEffect(mark);
            slamWarning = null;
            if (IsAlive && TouchesPlayer(area, settings.SlamRadius))
            {
                HurtPlayer(settings.MeleeDamage);
            }
            // Recovery: the printer stays turned away briefly, the opening to punish it.
            yield return new WaitForSeconds(.5f);
        }

        private bool TouchesPlayer(Vector3 point, float radius)
        {
            if (player == null || playerHealth == null || playerHealth.IsDead)
            {
                return false;
            }

            float height = point.y - player.position.y;
            return (point - player.position).Flat().magnitude <= radius && height >= -.3f && height <= 2.1f;
        }

        private void HurtPlayer(float damage)
        {
            if (playerHealth != null && !playerHealth.IsDead)
            {
                damageSystem.Resolve(playerHealth, new DamageRequest(DamageSource.NormalAttack, Faction.Enemy, damage));
            }
        }

        // A single sheet of paper; scale makes the larger falling sheets readable from a distance.
        private GameObject Sheet(string name, float scale)
        {
            GameObject sheet = Slab(paperMaterial);
            sheet.name = name;
            sheet.transform.localScale = new Vector3(.42f, .012f, .3f) * scale;
            attackEffects.Add(sheet);
            return sheet;
        }

        private static GameObject Slab(Material material)
        {
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            // Disable now: Destroy only takes effect at the end of the frame, and paper must never push the player.
            Collider slabCollider = slab.GetComponent<Collider>();
            slabCollider.enabled = false;
            Object.Destroy(slabCollider);
            Renderer slabRenderer = slab.GetComponent<Renderer>();
            if (material != null)
            {
                slabRenderer.sharedMaterial = material;
            }

            slabRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return slab;
        }

        private GameObject Effect(PrimitiveType shape, Material material, string name)
        {
            GameObject piece = GameObject.CreatePrimitive(shape);
            piece.name = name;
            Collider pieceCollider = piece.GetComponent<Collider>();
            pieceCollider.enabled = false;
            Destroy(pieceCollider);
            Renderer pieceRenderer = piece.GetComponent<Renderer>();
            pieceRenderer.sharedMaterial = material;
            pieceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pieceRenderer.receiveShadows = false;
            attackEffects.Add(piece);
            return piece;
        }

        private void RemoveEffect(GameObject piece)
        {
            attackEffects.Remove(piece);
            Destroy(piece);
        }

        private void SetWarning(Renderer target, float progress, float alpha)
        {
            target.GetPropertyBlock(warningBlock);
            warningBlock.SetFloat(ProgressId, progress);
            warningBlock.SetFloat(AlphaId, alpha);
            target.SetPropertyBlock(warningBlock);
        }

        // One throw releases the whole wave at once.
        private IEnumerator Summon()
        {
            minions.RemoveAll(minion => minion == null);
            int toSpawn = settings.MinionsPerSummon;
            if (minionPrefabs.Length == 0 || enemySystem == null || toSpawn <= 0)
            {
                yield break;
            }

            if (settings.SummonWindup > 0f)
            {
                yield return new WaitForSeconds(settings.SummonWindup);
            }

            if (IsAlive && !shieldBroken)
            {
                // Prefabs are used in order, so a wave of three is one of each minion type.
                for (int i = 0; i < toSpawn; i++)
                {
                    SpawnMinion(minionPrefabs[i % minionPrefabs.Length]);
                }
            }
        }

        private void SpawnMinion(EnemyController prefab)
        {
            if (prefab == null)
            {
                return;
            }

            Transform point = minionSpawnPoints.Length > 0 ? minionSpawnPoints[Random.Range(
                0,
                minionSpawnPoints.Length)] : null;
            Vector3 position = point != null ? point.position : transform.position + Random.insideUnitSphere.Flat() * 4f;
            position += Random.insideUnitSphere.Flat() * 1.2f;

            EnemyController minion = enemySystem.SpawnEnemy(prefab,
                position,
                Quaternion.LookRotation(transform.forward));
            minions.Add(minion);
            minionIds[minion] = new MinionState { id=System.Guid.NewGuid().ToString("N"), prefab=System.Array.IndexOf(minionPrefabs,prefab) };
            minion.HealthShield.Died += HandleMinionDied;
            StartCoroutine(AggroNextFrame(minion));
        }

        // Dead minions stay in the scene briefly for their death effect, so count by health, not by object.
        private int AliveMinions()
        {
            minions.RemoveAll(minion => minion == null);
            int alive = 0;
            foreach (EnemyController minion in minions)
            {
                if (!minion.HealthShield.IsDead)
                {
                    alive++;
                }
            }

            return alive;
        }

        // The minion's HealthShield initialises in Start, so aggro has to wait a frame.
        private static IEnumerator AggroNextFrame(EnemyController minion)
        {
            yield return null;
            if (minion != null)
            {
                minion.ForceAggro();
            }
        }

        private void HandleMinionDied(HealthShield minionHealth)
        {
            minionHealth.Died -= HandleMinionDied;
            if (!IsAlive || shieldBroken)
            {
                return;
            }

            health.ReduceShield(health.MaxShield / Mathf.Max(1, settings.MinionKillsToBreakShield));
            // Losing shield staggers the Boss; the final loss plays ShieldBroken instead.
            if (health.CurrentShield > 0f)
            {
                PlayHit();
            }

            if (AliveMinions() == 0)
            {
                nextSummonAt = Time.time + settings.NextWaveDelay;
            }
        }

        private void HandleVitalsChanged(HealthShield source)
        {
            if (shieldBroken || !source.IsInitialized || source.CurrentShield > 0f || source.MaxShield <= 0f)
            {
                return;
            }

            shieldBroken = true;
            SetBool(vulnerableBool, true);
            CancelBodyAction();
            pendingHit = false;
            StartBodyAction(BodyAction.ShieldBroken, Feedback(), shieldBrokenTrigger, "Printer_ShieldBroken", 1.6f);
        }

        private void HandleDied(HealthShield source)
        {
            StopAttacks();
            SetTrigger(deathTrigger);
            StartCoroutine(DisappearAfterDeath());
            // Remaining minions collapse with the Boss.
            foreach (EnemyController minion in minions)
            {
                if (minion != null && minion.IsAlive)
                {
                    minion.HealthShield.ApplyResolvedDamage(new DamageResult(minion.HealthShield.CurrentShield,
                        minion.HealthShield.CurrentHealth));
                }
            }
        }

        // Plays the death animation, then dissolves the model (same effect as normal enemies) and removes the Boss.
        private IEnumerator DisappearAfterDeath()
        {
            if (TryGetComponent(out Collider body))
            {
                body.enabled = false;
            }

            if (TryGetComponent(out UnityEngine.AI.NavMeshObstacle obstacle))
            {
                obstacle.enabled = false;
            }

            yield return new WaitForSeconds(settings.DissolveDelay);

            if (!TryGetComponent(out EnemyDeathEffect effect))
            {
                effect = gameObject.AddComponent<EnemyDeathEffect>();
            }
            // Hide rather than deactivate: BossEncounterPresenter on this object still has to show Victory.
            effect.Play(() =>
            {
                foreach (Renderer part in GetComponentsInChildren<Renderer>())
                {
                    part.enabled = false;
                }

                if (animator != null)
                {
                    animator.enabled = false;
                }
            });
        }

        private void SetTrigger(string parameter)
        {
            if (HasParameter(parameter, AnimatorControllerParameterType.Trigger))
            {
                animator.SetTrigger(parameter);
            }
        }

        private void SetBool(string parameter, bool value)
        {
            if (HasParameter(parameter, AnimatorControllerParameterType.Bool))
            {
                animator.SetBool(parameter, value);
            }
        }

        // Missing parameters are skipped so the Boss works before its Animator Controller is set up.
        private bool HasParameter(string parameter, AnimatorControllerParameterType type)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(parameter))
            {
                return false;
            }

            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.type == type && p.name == parameter)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, settings.HitRadius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, settings.ActivationRange);
        }
        [System.Serializable] public sealed class MinionState
        {
            public string id; public int prefab; public bool initialized; public Vector3 position; public Quaternion rotation;
            public HealthShield.SaveState vitals; public EnemyController.SaveState combat;
        }
        [System.Serializable] public sealed class SaveState
        {
            public bool active, broken, ring; public float attack, summon, melee; public Quaternion rotation;
            public HealthShield.SaveState vitals; public MinionState[] minions;
        }
        private readonly Dictionary<EnemyController, MinionState> minionIds = new();
        public SaveState CaptureState()
        {
            var list=new List<MinionState>();
            foreach(var m in minions) if(m!=null && (!m.HealthShield.IsInitialized || m.IsAlive)) {
                var id=minionIds[m]; list.Add(new MinionState {id=id.id,prefab=id.prefab,initialized=m.HealthShield.IsInitialized,position=m.transform.position,rotation=m.transform.rotation,vitals=m.HealthShield.CaptureState(),combat=m.CaptureState()});
            }
            return new SaveState {active=active,broken=shieldBroken,ring=ringNext,attack=Mathf.Max(0,nextAttackAt-Time.time),summon=Mathf.Max(0,nextSummonAt-Time.time),melee=Mathf.Max(0,nextMeleeAt-Time.time),rotation=transform.rotation,vitals=health.CaptureState(),minions=list.ToArray()};
        }
        public IEnumerator RestoreState(SaveState s)
        {
            StopAttacks();
            health.VitalsChanged -= HandleVitalsChanged;
            health.RestoreState(s.vitals);
            health.VitalsChanged += HandleVitalsChanged;
            active=s.active; shieldBroken=s.broken; ringNext=s.ring; pendingHit=false;
            transform.SetPositionAndRotation(active ? groundPosition : groundPosition + (settings.JumpIn ? Vector3.up*settings.JumpInHeight : Vector3.zero),s.rotation);
            nextAttackAt=Time.time+s.attack; nextSummonAt=Time.time+s.summon; nextMeleeAt=Time.time+s.melee;
            SetBool(vulnerableBool,shieldBroken);
            foreach(var old in minions) if(old!=null) { old.HealthShield.Died-=HandleMinionDied; Destroy(old.gameObject); }
            minions.Clear(); minionIds.Clear();
            foreach(var m in s.minions) {
                var enemy=enemySystem.SpawnEnemy(minionPrefabs[m.prefab],m.position,m.rotation);
                minions.Add(enemy); minionIds.Add(enemy,m); enemy.HealthShield.Died+=HandleMinionDied;
            }
            yield return null;
            foreach(var m in minions) { var state=minionIds[m]; if(state.initialized) { m.HealthShield.RestoreState(state.vitals); m.RestoreState(state.combat); } }
            if(health.IsDead) foreach(var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled=false;
        }

    }

    internal static class BossVectorExtensions
    {
        public static Vector3 Flat(this Vector3 value) => new(value.x, 0f, value.z);

    }
}
