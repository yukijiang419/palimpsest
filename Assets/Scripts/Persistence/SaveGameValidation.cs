#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Caffeine.Gameplay.Persistence;
using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Enemy;
using Caffeine.Gameplay.Puzzles;
using Caffeine.Gameplay.Quests;
using Caffeine.Gameplay.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Uses a new Library directory per run; never reads or replaces the player's save.
public sealed class SaveGameValidation : MonoBehaviour
{
    private readonly List<string> results = new();
    public static void Prepare()
    {
        string directory = Path.GetFullPath("Library/SaveValidation/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        SessionState.SetString("Palimpsest.SaveTestDirectory",directory);
    }
    public static void Run()
    {
        if (string.IsNullOrEmpty(SessionState.GetString("Palimpsest.SaveTestDirectory",""))) throw new InvalidOperationException("Prepare isolated storage before Play Mode.");
        var root = new GameObject("Save validation"); DontDestroyOnLoad(root);
        root.AddComponent<SaveGameValidation>().StartCoroutine(root.GetComponent<SaveGameValidation>().Checks());
    }
    private void Check(bool value, string label)
    {
        if(!value) throw new InvalidOperationException("FAIL " + label);
        results.Add(label); Debug.Log("SAVE PASS " + label);
        File.WriteAllLines("Library/SaveValidation/results.txt",results);
    }
    private SaveGame Read()
    {
        var path=Path.Combine(SessionState.GetString("Palimpsest.SaveTestDirectory",""),"Palimpsest.save.json");
        var data=SaveGameService.Decode(File.ReadAllText(path),out string error);
        Check(data!=null,"valid JSON snapshot: " + error); return data;
    }
    private IEnumerator Checks()
    {
        var service=SaveGameService.Instance;
        while(!service.Ready) yield return null;
        Check(!service.HasSave,"clean isolated title does not create save");
        service.StartNewGame();
        yield return null; yield return null; yield return null; yield return null;
        yield return new WaitForSecondsRealtime(1);
        var ui=FindFirstObjectByType<TutorialPresenter>(); ui.SkipIntro(); ui.FinishTutorial();
        var player=FindFirstObjectByType<PlayerInputReader>();
        player.GetComponent<PlayerWallet>().RestoreCoins(137);
        player.GetComponent<HealthShield>().IncreaseMaxHealth(25);
        player.GetComponent<PlayerStamina>().IncreaseMaxStamina(20);
        var weapons=player.GetComponentInChildren<WeaponManager>(); weapons.AddDamageBonus(0,7);
        var quest=ConfigSystem.Instance.Config.Quests.Definitions.First(d=>d.HasPuzzle);
        QuestProgress.ForRun(quest.QuestId).Accept();
        FindFirstObjectByType<CampusEncounterDirector>().ActivateAvailableRound();
        yield return service.Flush();
        var first=Read();
        Check(first.coins==137 && first.vitals.BonusMaxHealth==25 && first.weapons.weapons[0].bonus==7,"coins and upgrades captured together");
        string json=JsonUtility.ToJson(first);
        Check(SaveGameService.Decode(json.Replace("\"schemaVersion\":1","\"schemaVersion\":99"),out _)==null,"unknown schema rejected");
        Check(SaveGameService.Decode("broken",out _)==null,"corrupt JSON rejected");
        service.EndRun(); service.Continue();
        yield return null; while(SaveGameService.Restoring) yield return null;
        player=FindFirstObjectByType<PlayerInputReader>();
        Check(player.GetComponent<PlayerWallet>().Coins==137 && player.GetComponent<HealthShield>().BonusMaxHealth==25,"continue restores player upgrades once");
        Check(FindFirstObjectByType<TutorialPresenter>().CurrentScreen=="Pause","continue waits behind pause overlay");
        var enemies=FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
        Check(enemies.Length==first.campus.enemies.Count(e=>e!=null && e.spawned && !e.Dead),"campus roster restored without duplicate spawns");
        Check(enemies.All(e=>e.IsAlive && e.HealthShield.MaxHealth>0),"spawn-frame checkpoint restores initialized enemy health");
        var system=FindFirstObjectByType<EnemySystem>();
        var dropMethod=typeof(EnemySystem).GetMethod("DropCoins",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        dropMethod.Invoke(system,new object[] {player.transform.position+Vector3.forward*8,29});
        var queued=system.CaptureState();
        Check(queued.drops.Sum(d=>d.total)==29,"pending coin queue captured before spawning");
        system.RestoreState(queued);
        Check(system.CaptureState().drops.Sum(d=>d.total)==29,"pending coin queue restored without duplicate reward");
        var enemy=enemies[0];
        enemy.HealthShield.ApplyResolvedDamage(new DamageResult(enemy.HealthShield.CurrentShield,enemy.HealthShield.CurrentHealth));
        yield return service.Flush();
        var defeated=Read();
        Check(defeated.campus.enemies.Count(e=>e!=null && e.spawned && e.Dead)==1,"dead enemy and its drop captured together");
        yield return service.Flush();
        service.EndRun(); service.Continue(); yield return null; while(SaveGameService.Restoring) yield return null;
        Check(FindFirstObjectByType<PlayerInputReader>().GetComponent<HealthShield>().BonusMaxHealth==25,"second continue does not stack upgrade");
        for(int index=1; index<=3; index++)
        {
            player=FindFirstObjectByType<PlayerInputReader>(); PuzzleRunState.TryJumpTo(index,player);
            yield return null; while(SaveGameService.Travelling) yield return null;
            yield return null; yield return null;
            var puzzle=FindFirstObjectByType<TimelinePuzzleArea>();
            Check(puzzle!=null,"entered puzzle " + index);
            var state=puzzle.CaptureState(); state.first=true; state.era=TimelineEra.Future;
            state.firstPosition=puzzle.firstTargetLocalPosition; puzzle.RestoreState(state);
            yield return service.Flush(); service.EndRun(); service.Continue();
            yield return null; while(SaveGameService.Restoring) yield return null;
            puzzle=FindFirstObjectByType<TimelinePuzzleArea>();
            Check(puzzle.FirstChanged && puzzle.Era==TimelineEra.Future,"puzzle " + index + " restores era and block lock");
        }
        player=FindFirstObjectByType<PlayerInputReader>(); PuzzleRunState.TryJumpTo(4,player);
        yield return null; while(SaveGameService.Travelling) yield return null;
        yield return null; yield return null;
        var boss=BossController.Current; var bossState=boss.CaptureState();
        typeof(BossController).GetMethod("SpawnMinion",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(boss,new object[] {
            ((EnemyController[])typeof(BossController).GetField("minionPrefabs",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(boss))[0] });
        Check(boss.CaptureState().minions.Length==1,"spawn-frame Boss summon is included before Start");
        yield return null; yield return null;
        bossState=boss.CaptureState();
        Check(bossState.minions.Length==1,"Boss live summon captured with stable ID");
        bossState.active=true; bossState.broken=true; bossState.vitals.CurrentShield=0;
        yield return boss.RestoreState(bossState); yield return service.Flush();
        service.EndRun(); service.Continue(); yield return null; while(SaveGameService.Restoring) yield return null;
        Check(BossController.Current.IsVulnerable,"Boss broken shield survives continue");
        Check(BossController.Current.CaptureState().minions.Length==1,"Boss summon restored once");
        player=FindFirstObjectByType<PlayerInputReader>();
        var health=player.GetComponent<HealthShield>(); var death=health.CaptureState(); death.CurrentHealth=0; death.IsDead=true;
        health.RestoreState(death); player.GetComponent<PlayerDeathController>().RestoreDeathState();
        yield return service.Flush(); service.EndRun(); service.Continue(); yield return null; while(SaveGameService.Restoring) yield return null;
        Check(FindFirstObjectByType<TutorialPresenter>().CurrentScreen=="GameOver","death remains death after continue");
        FindFirstObjectByType<TutorialPresenter>().Respawn();
        Check(!FindFirstObjectByType<PlayerInputReader>().GetComponent<HealthShield>().IsDead,"restored death can respawn");
        yield return service.Flush();
        Check(Read()!=null,"final respawn save readable");
        // Recreate the durable source checkpoint from a puzzle-to-campus transition.
        // Reading this checkpoint must finish the trip, rather than strand the player in a completed puzzle.
        player=FindFirstObjectByType<PlayerInputReader>(); PuzzleRunState.TryJumpTo(1,player);
        yield return null; while(SaveGameService.Travelling) yield return null;
        yield return null; yield return null;
        var solved=FindFirstObjectByType<TimelinePuzzleArea>();
        var solvedState=solved.CaptureState(); solvedState.completed=true; solved.RestoreState(solvedState);
        var pendingField=typeof(SaveGameService).GetField("destinationScene",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        pendingField.SetValue(service,"StartScene");
        yield return service.Flush();
        Check(Read().destinationScene=="StartScene","outgoing checkpoint records destination scene");
        pendingField.SetValue(service,null);
        service.EndRun(); service.Continue(); yield return null; while(SaveGameService.Restoring) yield return null;
        Check(SceneManager.GetActiveScene().name=="StartScene","interrupted puzzle return continues on campus");
        Check(FindFirstObjectByType<TimelinePuzzleArea>()==null,"source puzzle state is not applied to destination");
        Debug.Log("SAVE VALIDATION COMPLETE: " + results.Count);
    }
    public static void BuildWeb()
    {
        var scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray();
        var report=BuildPipeline.BuildPlayer(scenes,"Builds/SaveValidation/WebGL",BuildTarget.WebGL,BuildOptions.Development);
        File.WriteAllText("Library/SaveValidation/build.txt",report.summary.result+" errors="+report.summary.totalErrors);
        Debug.Log("SAVE WEB BUILD " + report.summary.result);
    }
}

#endif
