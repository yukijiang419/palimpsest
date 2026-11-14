using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Caffeine.Gameplay.CameraSystem;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Enemy;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.Puzzles;
using Caffeine.Gameplay.Quests;
using Caffeine.Gameplay.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Caffeine.Gameplay.Persistence
{
    [Serializable] public sealed class SaveGame
    {
        public int schemaVersion = 1;
        public string contentVersion = "palimpsest-1", savedUtc, scene;
        public string destinationScene;
        public long sequence;
        public bool hasPuzzle, hasBoss;
        public Vector3 position;
        public Quaternion rotation;
        public int coins;
        public HealthShield.SaveState vitals;
        public PlayerStamina.SaveState stamina;
        public MazeSystem.SaveState maze;
        public WeaponManager.SaveState weapons;
        public ThirdPersonCameraController.SaveState camera;
        public PuzzleRunState.SaveState run;
        public QuestProgress.SaveState[] quests;
        public CampusEncounterDirector.SaveState campus;
        public TimelinePuzzleArea.SaveState puzzle;
        public EnemySystem.SaveState drops;
        public BossController.SaveState boss;
        public string tutorial;
    }

    // One owner serializes writes and coordinates restoration before gameplay resumes.
    [DefaultExecutionOrder(10000)]
    public sealed class SaveGameService : MonoBehaviour
    {
        public static SaveGameService Instance { get; private set; }
        public static bool Restoring { get; private set; }
        public static bool Travelling { get; private set; }
        public bool Ready { get; private set; }
        public bool HasSave => saved != null;
        public bool HasStoredData => !string.IsNullOrEmpty(currentJson) || !string.IsNullOrEmpty(backupJson);
        public bool Busy { get; private set; }
        public string Status { get; private set; } = "Reading save...";
        public event Action Changed;
        private SaveGame saved;
        private string currentJson, backupJson, pendingJson, writingJson;
        private bool activeRun, dirty, replaceNext;
        private bool sceneLoading;
        private string destinationScene;
        private bool writingReplacement;
        private string committedState, writingState;
        private float nextSave;
        private long sequence;
        private int requestId, generation;
        private string pendingRequest;
        private float requestDeadline;
        private string StorageDirectory
        {
            get
            {
#if UNITY_EDITOR
                string test = UnityEditor.SessionState.GetString("Palimpsest.SaveTestDirectory", "");
                if (!string.IsNullOrEmpty(test)) return test;
#endif
                return Application.persistentDataPath;
            }
        }
        private string FilePath => Path.Combine(StorageDirectory, "Palimpsest.save.json");

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void PalimpsestSaveRead(string receiver, string request);
        [DllImport("__Internal")] private static extern void PalimpsestSaveWrite(string receiver, string request, string current, string backup);
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; Restoring = Travelling = false; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            var root = new GameObject("Palimpsest Save Service");
            DontDestroyOnLoad(root); Instance = root.AddComponent<SaveGameService>();
        }
        private void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            pendingRequest = "read"; requestDeadline = Time.realtimeSinceStartup + 15;
            PalimpsestSaveRead(gameObject.name, pendingRequest);
#else
            try { ReadCompleted(File.Exists(FilePath) ? File.ReadAllText(FilePath) : "", File.Exists(FilePath+".bak") ? File.ReadAllText(FilePath+".bak") : ""); }
            catch (Exception e) { Fail(e.Message); Ready = true; }
#endif
        }
        [Serializable] private sealed class Reply { public string request, current, backup, error; }
        [UnityEngine.Scripting.Preserve] public void OnStorageReply(string json)
        {
            var reply = JsonUtility.FromJson<Reply>(json);
            if (reply.request != pendingRequest) return;
            pendingRequest = null;
            if (!string.IsNullOrEmpty(reply.error)) { Busy = false; Ready = true; Fail(reply.error); return; }
            if (reply.request == "read") ReadCompleted(reply.current,reply.backup);
            else WriteCompleted();
        }
        private void ReadCompleted(string current, string backup)
        {
            currentJson = current; backupJson = backup;
            saved = Decode(current, out string error);
            // Unknown versions remain untouched; don't silently roll them backwards.
            if (saved == null && error != "Unsupported save version.")
            {
                saved = Decode(backup, out _);
                if (saved != null) { currentJson = backup; Status = "Recovered previous save."; }
                else Status = string.IsNullOrEmpty(current) ? "" : error;
            }
            else Status = saved != null ? "Continue available" : error;
            sequence = saved?.sequence ?? 0;
            Ready = true; Changed?.Invoke();
        }
        public static SaveGame Decode(string json, out string error)
        {
            error = "Save data is damaged.";
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var s = JsonUtility.FromJson<SaveGame>(json);
                if (s.schemaVersion != 1 || s.contentVersion != "palimpsest-1") { error = "Unsupported save version."; return null; }
                // JsonUtility expands null inline classes into default objects. Presence is explicit.
                if (!s.hasPuzzle) s.puzzle = null;
                if (!s.hasBoss) s.boss = null;
                if (s.campus?.enemies != null)
                    for (int i = 0; i < s.campus.enemies.Length; i++)
                        if (!s.campus.enemies[i].spawned) s.campus.enemies[i] = null;
                if (s.sequence < 1 || !Application.CanStreamedLevelBeLoaded(s.scene) || s.vitals == null || s.stamina == null || s.run == null || s.quests == null || s.weapons?.weapons == null || s.weapons.equipped?.Length != 2 || s.maze == null || s.camera == null || s.drops?.coins == null || s.drops.drops == null || s.campus?.rounds == null || s.campus.enemies == null) return null;
                if (!string.IsNullOrEmpty(s.destinationScene) && !Application.CanStreamedLevelBeLoaded(s.destinationScene)) return null;
                if (s.coins < 0 || !Finite(s.position) || !Finite(s.rotation) || !float.IsFinite(s.vitals.CurrentHealth) || s.vitals.CurrentHealth < 0 || s.vitals.MaxHealth <= 0 || s.vitals.CurrentHealth > s.vitals.MaxHealth || s.vitals.IsDead != (s.vitals.CurrentHealth <= 0)) return null;
                if (s.quests.Any(q => q == null || string.IsNullOrWhiteSpace(q.id) || !Enum.IsDefined(typeof(QuestStage), q.stage)) || s.quests.Select(q=>q.id).Distinct().Count()!=s.quests.Length) return null;
                if (s.weapons.weapons.Any(w=>w==null || string.IsNullOrEmpty(w.id) || !float.IsFinite(w.bonus) || w.bonus<0) || s.weapons.weapons.Select(w=>w.id).Distinct().Count()!=s.weapons.weapons.Length) return null;
                if (s.drops.coins.Any(c=>c==null || c.value<=0 || string.IsNullOrEmpty(c.id) || !Finite(c.position)) || s.drops.drops.Any(d=>d==null || d.total<=0 || d.pieces<=0 || d.index<0 || d.index>=d.pieces || !Finite(d.position))) return null;
                if (s.puzzle != null && (!Enum.IsDefined(typeof(TimelineEra),s.puzzle.era) || s.puzzle.index<1 || s.puzzle.index>3 || !Finite(s.puzzle.firstPosition) || !Finite(s.puzzle.secondPosition))) return null;
                if (s.boss != null && (s.boss.vitals == null || s.boss.minions == null || s.boss.minions.Any(m=>m==null || m.prefab<0 || m.prefab>2 || m.vitals==null || m.combat==null || !Finite(m.position)))) return null;
                var config = ConfigSystem.Instance?.Config;
                if (config != null)
                {
                    if (s.quests.Any(q => !config.Quests.Definitions.Any(d=>d.QuestId==q.id))) return null;
                    var puzzle = config.Quests.Definitions.FirstOrDefault(d=>d.HasPuzzle && d.Puzzle.sceneName==s.scene);
                    if ((puzzle != null) != s.hasPuzzle || (s.scene==config.Quests.BossSceneName) != s.hasBoss) return null;
                    if (puzzle != null && s.puzzle.index!=puzzle.Puzzle.puzzleIndex) return null;
                    if (s.campus.rounds.Length!=0 && (s.campus.rounds.Length!=config.Encounters.SquadsPerRound.Length || s.campus.enemies.Length!=config.Encounters.EnemyCount)) return null;
                }
                error = null; return s;
            }
            catch { return null; }
        }
        private static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        private static bool Finite(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w) && q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w > .5f;
        public static void RequestSave() { if (Instance != null && Instance.activeRun && !Restoring && !Travelling) Instance.dirty = true; }
        public void BeginNewRun()
        {
            generation++; replaceNext = true; pendingJson = null; activeRun = true;
            PuzzleRunState.ResetRun(); dirty = true; nextSave = 0;
        }
        public void EndRun() { activeRun = false; dirty = false; pendingJson = null; }
        private void LateUpdate()
        {
            if (pendingRequest != null && Time.realtimeSinceStartup > requestDeadline) { pendingRequest = null; Busy = false; Ready = true; Fail("Storage did not respond. Retry saving."); }
            if (activeRun && !Restoring && !sceneLoading && (dirty || Time.realtimeSinceStartup >= nextSave))
            {
                dirty = false; nextSave = Time.realtimeSinceStartup + 5;
                try
                {
                    var snapshot = Capture();
                    snapshot.sequence = 0; snapshot.savedUtc = null;
                    string state = JsonUtility.ToJson(snapshot);
                    if (state != (Busy ? writingState : committedState) || replaceNext)
                    {
                        snapshot.sequence = ++sequence; snapshot.savedUtc = DateTime.UtcNow.ToString("O");
                        pendingJson = JsonUtility.ToJson(snapshot);
                    }
                    else pendingJson = null;
                }
                catch (Exception e) { Fail(e.Message); }
            }
            PumpWrite();
        }
        private SaveGame Capture()
        {
            var p = FindFirstObjectByType<PlayerInputReader>();
            var screen = FindFirstObjectByType<TutorialPresenter>();
            return new SaveGame { scene = SceneManager.GetActiveScene().name, destinationScene = destinationScene,
                position=p.transform.position, rotation=p.transform.rotation, coins=p.GetComponent<PlayerWallet>().Coins,
                vitals=p.GetComponent<HealthShield>().CaptureState(), stamina=p.GetComponent<PlayerStamina>().CaptureState(),
                maze=FindFirstObjectByType<MazeSystem>().CaptureState(), weapons=p.GetComponentInChildren<WeaponManager>().CaptureState(),
                camera=FindFirstObjectByType<ThirdPersonCameraController>().CaptureState(), run=PuzzleRunState.CaptureState(), quests=QuestProgress.CaptureState(),
                hasPuzzle=FindFirstObjectByType<TimelinePuzzleArea>()!=null, hasBoss=BossController.Current!=null,
                campus=CampusEncounterDirector.CaptureState(), puzzle=FindFirstObjectByType<TimelinePuzzleArea>()?.CaptureState(),
                drops=FindFirstObjectByType<EnemySystem>().CaptureState(), boss=BossController.Current?.CaptureState(), tutorial=screen.CaptureTutorial() };
        }
        private void PumpWrite()
        {
            if (Busy || pendingJson == null || !Ready) return;
            writingJson = pendingJson; pendingJson = null; Busy = true; Status = "Saving..."; Changed?.Invoke();
            writingReplacement = replaceNext;
            string backup = writingReplacement ? "" : currentJson ?? "";
            backupJson = backup;
            var state = JsonUtility.FromJson<SaveGame>(writingJson);
            state.sequence = 0; state.savedUtc = null; writingState = JsonUtility.ToJson(state);
            pendingRequest = generation + ":" + (++requestId); requestDeadline = Time.realtimeSinceStartup + 15;
#if UNITY_WEBGL && !UNITY_EDITOR
            PalimpsestSaveWrite(gameObject.name,pendingRequest,writingJson,backup);
#else
            try
            {
                Directory.CreateDirectory(StorageDirectory);
                File.WriteAllText(FilePath+".tmp",writingJson);
                File.WriteAllText(FilePath+".bak",backup);
                if(File.Exists(FilePath)) File.Replace(FilePath+".tmp",FilePath,null); else File.Move(FilePath+".tmp",FilePath);
                pendingRequest=null; WriteCompleted();
            }
            catch(Exception e) { pendingRequest=null; Busy=false; Fail(e.Message); }
#endif
        }
        private void WriteCompleted()
        {
            currentJson=writingJson; saved=Decode(currentJson,out _); Busy=false;
            committedState = writingState;
            if (writingReplacement) replaceNext = false;
            Status="Saved"; Changed?.Invoke();
        }
        private void Fail(string error) { Status="Save failed: "+error; Debug.LogWarning(Status); Changed?.Invoke(); }
        public IEnumerator Flush()
        {
            RequestSave(); yield return null;
            while(Busy || pendingJson != null || dirty) yield return null;
        }
        public IEnumerator Travel(string scene)
        {
            destinationScene = scene;
            Travelling = true; dirty = true;
            FindFirstObjectByType<PlayerInputReader>().SetTutorialInputBlocked(true);
            FindFirstObjectByType<GameTimeController>().SetTutorialPaused(true);
            yield return Flush();
            if(Status.StartsWith("Save failed")) { destinationScene=null; Travelling=false; PuzzleRunState.CancelPendingArrival(); FindFirstObjectByType<TutorialPresenter>().TravelFailed(); yield break; }
            sceneLoading=true;
            SceneManager.LoadScene(scene);
            yield return null; yield return null;
            destinationScene=null; sceneLoading=false; Travelling=false; RequestSave();
        }
        public void Continue()
        {
            if (saved == null || Busy || Restoring || Travelling) return;
            var state = Decode(currentJson, out string error);
            if (state == null) { Fail(error); return; }
            StartCoroutine(Restore(state));
        }
        private IEnumerator Restore(SaveGame s)
        {
            Restoring=true; activeRun=false; Time.timeScale=0;
            bool arriving = !string.IsNullOrEmpty(s.destinationScene);
            string scene = arriving ? s.destinationScene : s.scene;
            PuzzleRunState.ResetRun(); PuzzleRunState.RestoreState(s.run); QuestProgress.RestoreState(s.quests); CampusEncounterDirector.RestoreState(s.campus);
            yield return SceneManager.LoadSceneAsync(scene);
            yield return null; yield return null;
            var p=FindFirstObjectByType<PlayerInputReader>();
            var puzzle=FindFirstObjectByType<TimelinePuzzleArea>();
            if(!arriving && s.puzzle!=null) puzzle.RestoreState(s.puzzle);
            if(!arriving && s.boss!=null && BossController.Current!=null) yield return BossController.Current.RestoreState(s.boss);
            p.GetComponent<HealthShield>().RestoreState(s.vitals);
            p.GetComponent<PlayerStamina>().RestoreState(s.stamina);
            p.GetComponent<PlayerWallet>().RestoreCoins(s.coins);
            p.GetComponentInChildren<WeaponManager>().RestoreState(s.weapons);
            FindFirstObjectByType<MazeSystem>().RestoreState(s.maze);
            if (!arriving)
            {
                p.GetComponent<PlayerMoveController>().TeleportTo(s.position,s.rotation);
                FindFirstObjectByType<ThirdPersonCameraController>().RestoreState(s.camera);
                FindFirstObjectByType<EnemySystem>().RestoreState(s.drops);
            }
            else if (scene == "StartScene" && s.run.hasReturnPosition)
                p.GetComponent<PlayerMoveController>().TeleportTo(s.run.returnPosition,s.run.returnRotation);
            if (arriving) s.tutorial = null;
            if(s.vitals.IsDead) p.GetComponent<PlayerDeathController>().RestoreDeathState();
            var ui=FindFirstObjectByType<TutorialPresenter>(); ui.RestoreSaveScreen(s);
            Restoring=false; activeRun=true; nextSave=Time.realtimeSinceStartup+5;
        }
        private void OnApplicationFocus(bool focus) { if(!focus) RequestSave(); }
        private void OnApplicationPause(bool pause) { if(pause) RequestSave(); }
        [UnityEngine.Scripting.Preserve] public void OnPageHidden(string unused) => RequestSave();
        public void StartNewGame() { if(!Busy && !Restoring && !Travelling) StartCoroutine(NewGame()); }
        private IEnumerator NewGame()
        {
            EndRun(); Travelling=true; PuzzleRunState.ResetRun();
            yield return SceneManager.LoadSceneAsync("StartScene");
            yield return null; yield return null;
            BeginNewRun(); Travelling=false;
            FindFirstObjectByType<TutorialPresenter>().StartRun();
        }

    }
}
