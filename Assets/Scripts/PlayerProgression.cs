using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum PlayerSide { Hero, Villain }
[Serializable] public sealed class PowerOwnership { public string Id; public int Tier; }
/// Per-mode personal records (e.g. Endless Fight best score). Saves written before this field existed load with an empty list.
[Serializable] public sealed class ModeRecord { public string Id; public int BestScore, BestWave, Runs; }
[Serializable] public sealed class ProgressSave
{
    public int Version = 1, Level = 1, Xp, Points;
    public PlayerSide Side;
    public List<PowerOwnership> Powers = new List<PowerOwnership>();
    public List<string> Rooftops = new List<string>();
    public int SessionsPlayed, SessionsWon, BestSessionScore, LastSessionXp;
    public string LastModeId;
    public List<ModeRecord> ModeRecords = new List<ModeRecord>();
}
public sealed class PlayerProgression : MonoBehaviour
{
    public ProgressSave Data { get; private set; } = new ProgressSave();
    public string SavePath { get; private set; }
    public string LastError { get; private set; }
    public float SwitchRemaining { get; private set; }
    public event Action Changed;
    public event Action<int> XpAwarded;
    public bool SideLocked { get; private set; }
    ProgressionSettings config;
    PowerDefinition[] definitions;
    public int RequiredXp => Mathf.Max(1, Mathf.RoundToInt(config.BaseLevelXp * Mathf.Pow(config.LevelXpGrowth, Data.Level - 1)));
    public void Initialize(ProgressionSettings settings, PowerDefinition[] powers, string path = null)
    {
        config = settings; definitions = powers;
        SavePath = path ?? Path.Combine(Application.persistentDataPath, settings.SaveFilename);
        Load();
    }
    public int Tier(PowerDefinition definition) => Data.Powers.Find(p => p.Id == definition.Id)?.Tier ?? -1;
    public bool Owns(PowerDefinition definition) => Tier(definition) >= 0;
    public void AddXp(int amount)
    {
        amount=Mathf.Max(0,amount); Data.Xp += amount;
        while (Data.Xp >= RequiredXp) { Data.Xp -= RequiredXp; Data.Level++; Data.Points += config.PointsPerLevel; }
        Save(); Changed?.Invoke(); XpAwarded?.Invoke(amount);
    }
    public bool Buy(PowerDefinition definition)
    {
        int tier = Tier(definition);
        if (tier >= definition.Upgrades.Length) return false;
        int cost = tier < 0 ? definition.UnlockCost : definition.Upgrades[tier].PointCost;
        if (Data.Points < cost) return false;
        Data.Points -= cost;
        if (tier < 0) Data.Powers.Add(new PowerOwnership { Id = definition.Id });
        else Data.Powers.Find(p => p.Id == definition.Id).Tier++;
        Save(); Changed?.Invoke(); return true;
    }
    public bool SwitchSide()
    {
        if (SideLocked || SwitchRemaining > 0f) return false;
        Data.Side = Data.Side == PlayerSide.Hero ? PlayerSide.Villain : PlayerSide.Hero;
        SwitchRemaining = config.SideSwitchCooldown; Save(); Changed?.Invoke(); return true;
    }
    void Update() { SwitchRemaining = Mathf.Max(0f, SwitchRemaining - Time.deltaTime); }
    public void SetModeSide(PlayerSide side) { SetModeSide(side,true); }
    /// locked=false lets SwitchSide work during the session (modes with AllowSideSwitch).
    public void SetModeSide(PlayerSide side,bool locked) { SideLocked=locked; Data.Side=side; Save(); Changed?.Invoke(); }
    public void RecordSession(string mode,bool won,int score,int xp) { RecordSession(mode,won,score,xp,0); }
    /// BestSessionScore keeps its global meaning; the per-mode record is kept alongside it.
    public void RecordSession(string mode,bool won,int score,int xp,int wave)
    {
        Data.SessionsPlayed++; if(won) Data.SessionsWon++;
        Data.BestSessionScore=Mathf.Max(Data.BestSessionScore,score); Data.LastSessionXp=xp; Data.LastModeId=mode;
        if(!string.IsNullOrEmpty(mode))
        {
            var record=Record(mode); if(record==null) Data.ModeRecords.Add(record=new ModeRecord{Id=mode});
            record.Runs++; record.BestScore=Mathf.Max(record.BestScore,score); record.BestWave=Mathf.Max(record.BestWave,wave);
        }
        Save();
    }
    public ModeRecord Record(string mode) => Data.ModeRecords.Find(r => r.Id == mode);
    public int BestScore(string mode) => Record(mode)?.BestScore ?? 0;
    public bool ClaimRoof(string id)
    {
        if (Data.Rooftops.Contains(id)) return false;
        Data.Rooftops.Add(id); AddXp(config.RooftopXp); return true;
    }
    public void Save()
    {
        if (string.IsNullOrEmpty(SavePath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
            string temp = SavePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(Data, true));
            if (File.Exists(SavePath)) File.Replace(temp, SavePath, SavePath + ".bak");
            else File.Move(temp, SavePath);
            LastError = null;
        }
        catch (Exception e) { LastError = e.Message; Debug.LogError("Progression save failed: " + e.Message); }
    }
    public void Load()
    {
        Data = new ProgressSave();
        if (File.Exists(SavePath))
        {
            try
            {
                var loaded = JsonUtility.FromJson<ProgressSave>(File.ReadAllText(SavePath));
                if (loaded == null || loaded.Version != 1 || loaded.Level < 1 || loaded.Xp < 0 || loaded.Points < 0 || loaded.Powers == null || loaded.Rooftops == null)
                    throw new InvalidDataException("Invalid progression save.");
                if (loaded.ModeRecords == null) loaded.ModeRecords = new List<ModeRecord>();
                Data = loaded;
            }
            catch (Exception e) { LastError = e.Message; Debug.LogWarning("Save unreadable; fresh progression in memory: " + e.Message); }
        }
        foreach (var d in definitions)
            if (d.InitiallyUnlocked && !Owns(d)) Data.Powers.Add(new PowerOwnership { Id = d.Id });
        foreach (var p in Data.Powers)
        {
            var d = Array.Find(definitions, v => v.Id == p.Id);
            if (d != null) p.Tier = Mathf.Clamp(p.Tier, 0, d.Upgrades.Length);
        }
        Changed?.Invoke();
    }
    void OnApplicationQuit() { Save(); }
}
