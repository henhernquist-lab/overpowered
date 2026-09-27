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
    public HeroLoadout Loadout;
    /// First-time control prompts the player has already acted on (HUD). Saves written before this field existed load with an empty list.
    public List<string> SeenHints = new List<string>();
    public bool FirstPerson;
}
/// One XP grant as the player received it: the amount actually added, where it happened (if anywhere) and why.
public readonly struct XpGrant
{
    public readonly int Amount; public readonly bool HasPosition; public readonly Vector3 Position; public readonly string Reason;
    public XpGrant(int amount, bool hasPosition, Vector3 position, string reason) { Amount = amount; HasPosition = hasPosition; Position = position; Reason = reason; }
}
public sealed class PlayerProgression : MonoBehaviour
{
    public ProgressSave Data { get; private set; } = new ProgressSave();
    public string SavePath { get; private set; }
    public string LastError { get; private set; }
    public float SwitchRemaining { get; private set; }
    public event Action Changed;
    public event Action<int> XpAwarded;
    /// Raised for EVERY grant (both AddXp overloads), right after XpAwarded, with the same amount (HUD popups).
    public event Action<XpGrant> XpGranted;
    /// Raised when a grant raises the level: (level before, level after). Raised after XpGranted.
    public event Action<int, int> LevelUp;
    public bool SideLocked { get; private set; }
    ProgressionSettings config;
    PowerDefinition[] definitions;
    /// Levels are clamped here on load and by grants (a huge saved level would otherwise overflow the XP curve).
    public const int MaxLevel = 9999;
    /// Same curve as before for every reachable level; a non-finite or out-of-range value (absurd level) saturates instead of
    /// wrapping to a negative number (which made every XP grant level up once per point).
    public int RequiredXp
    {
        get
        {
            float raw = config.BaseLevelXp * Mathf.Pow(config.LevelXpGrowth, Data.Level - 1);
            return float.IsNaN(raw) || raw >= int.MaxValue ? int.MaxValue : Mathf.Max(1, Mathf.RoundToInt(raw));
        }
    }
    /// Which file the last Load() actually used when the save itself was missing or unreadable (".bak" / ".tmp"), else null.
    public string RecoveredFrom { get; private set; }
    /// What Load() repaired in the file it read (duplicates merged, lists created, values clamped); empty = nothing.
    public readonly List<string> Repairs = new List<string>();
    public void Initialize(ProgressionSettings settings, PowerDefinition[] powers, string path = null)
    {
        config = settings; definitions = powers;
        // Batch mode is only ever automated verification: if a harness failed to set its sandbox path (e.g. a Reload whose
        // Run aborted before writing its save pointer), use a throwaway file, never the player's real progression save.
        if (path == null && Application.isBatchMode)
        {
            path = Path.Combine(Application.temporaryCachePath, "batch-unsandboxed-" + Guid.NewGuid().ToString("N") + ".json");
            Debug.LogWarning("PlayerProgression: batch mode without a verification save path; using throwaway " + path);
        }
        SavePath = path ?? Path.Combine(Application.persistentDataPath, settings.SaveFilename);
        Load();
    }
    public int Tier(PowerDefinition definition) => Data.Powers.Find(p => p.Id == definition.Id)?.Tier ?? -1;
    public bool Owns(PowerDefinition definition) => Tier(definition) >= 0;
    public void SetFirstPerson(bool enabled) { Data.FirstPerson = enabled; Save(); }
    public HeroDefinition SelectedHero => Resources.Load<ForgeCatalog>("ForgeCatalog")?.Hero(Data.Loadout?.HeroId);
    public PowerDefinition EquippedA => Array.Find(definitions,p=>p.Id==Data.Loadout?.PowerA);
    public PowerDefinition EquippedB => Array.Find(definitions,p=>p.Id==Data.Loadout?.PowerB);
    public bool SetLoadout(HeroDefinition hero,PowerDefinition a,PowerDefinition b,CityColor primary,CityColor secondary)
    {
        var forge=Resources.Load<ForgeCatalog>("ForgeCatalog");
        if(forge==null||Array.IndexOf(forge.Heroes,hero)<0||a==b||!forge.Allowed(hero,a)||!forge.Allowed(hero,b)||!Owns(a)||!Owns(b))return false;
        // Loadouts are chosen before play; changing a live loadout would reset ongoing resource/ability state.
        if(WorldSession.Instance!=null&&WorldSession.Instance.Progression==this)return false;
        Data.Loadout=new HeroLoadout{HeroId=hero.Id,PowerA=a.Id,PowerB=b.Id,Primary=forge.ColorOrDefault(primary,hero.Primary),Secondary=forge.ColorOrDefault(secondary,hero.Secondary)};
        Save();Changed?.Invoke();return true;
    }
    void ValidateLoadout()
    {
        var forge=Resources.Load<ForgeCatalog>("ForgeCatalog");if(forge==null||forge.Heroes.Length==0)return;
        var hero=forge.Hero(Data.Loadout?.HeroId);
        var available=Array.FindAll(hero.AvailablePowers,p=>p!=null&&Owns(p));
        if(available.Length<2)return;
        var a=Array.Find(available,p=>p.Id==Data.Loadout?.PowerA)??(Owns(hero.DefaultA)?hero.DefaultA:available[0]);
        var b=Array.Find(available,p=>p.Id==Data.Loadout?.PowerB&&p!=a)??Array.Find(available,p=>p==hero.DefaultB&&p!=a)??Array.Find(available,p=>p!=a);
        Data.Loadout=new HeroLoadout{HeroId=hero.Id,PowerA=a.Id,PowerB=b.Id,
            Primary=forge.ColorOrDefault(Data.Loadout?.Primary??hero.Primary,hero.Primary),
            Secondary=forge.ColorOrDefault(Data.Loadout?.Secondary??hero.Secondary,hero.Secondary)};
    }
    public void AddXp(int amount) { Grant(amount, false, default, null); }
    /// Same grant as AddXp(amount), additionally telling listeners where it happened and why (HUD "+XP" popups).
    public void AddXp(int amount, Vector3 where, string reason) { Grant(amount, true, where, reason); }
    void Grant(int amount, bool hasPosition, Vector3 where, string reason)
    {
        amount=Mathf.Max(0,amount); Data.Xp = (int)Math.Min((long)Data.Xp + amount, int.MaxValue); int levelBefore = Data.Level;
        while (Data.Level < MaxLevel && Data.Xp >= RequiredXp) { Data.Xp -= RequiredXp; Data.Level++; Data.Points = (int)Math.Min((long)Data.Points + config.PointsPerLevel, int.MaxValue); }
        Save(); Changed?.Invoke(); XpAwarded?.Invoke(amount);
        XpGranted?.Invoke(new XpGrant(amount, hasPosition, where, reason));
        if (Data.Level > levelBefore) LevelUp?.Invoke(levelBefore, Data.Level);
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
    public bool HintSeen(string id) => Data.SeenHints != null && Data.SeenHints.Contains(id);
    /// Records that the player performed a prompted action; saves immediately. False if already recorded.
    public bool MarkHintSeen(string id)
    {
        if (string.IsNullOrEmpty(id) || HintSeen(id)) return false;
        if (Data.SeenHints == null) Data.SeenHints = new List<string>();
        Data.SeenHints.Add(id); Save(); return true;
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
        Data = new ProgressSave(); RecoveredFrom = null; Repairs.Clear();
        var loaded = Read(SavePath, out string error);
        if (loaded == null)
        {
            if (error != null)
            {
                LastError = error;
                // Keep the unreadable file: the next Save() moves the current file to .bak and would otherwise lose it.
                try { File.Copy(SavePath, SavePath + ".corrupt", true); } catch (Exception copy) { Debug.LogWarning("Could not preserve the unreadable save: " + copy.Message); }
            }
            // The save is missing or unreadable: fall back to the newest readable previous version (the .bak File.Replace
            // keeps, or a complete .tmp left by an interrupted first save). A leftover .tmp next to a READABLE save is ignored.
            DateTime newest = DateTime.MinValue;
            foreach (var candidate in new[] { SavePath + ".bak", SavePath + ".tmp" })
            {
                var copy = Read(candidate, out _); if (copy == null) continue;
                var when = File.GetLastWriteTimeUtc(candidate);
                if (loaded == null || when > newest) { loaded = copy; newest = when; RecoveredFrom = candidate; }
            }
            if (loaded == null && error != null) Debug.LogWarning("Save unreadable and no readable backup; fresh progression in memory: " + error);
            else if (RecoveredFrom != null) Debug.LogWarning("Progression recovered from " + RecoveredFrom + (error != null ? " (save unreadable: " + error + ")" : " (save missing)"));
        }
        if (loaded != null) Data = Repair(loaded);
        foreach (var d in definitions)
            if (d.InitiallyUnlocked && !Owns(d)) Data.Powers.Add(new PowerOwnership { Id = d.Id });
        foreach (var p in Data.Powers)
        {
            var d = Array.Find(definitions, v => v.Id == p.Id);
            if (d != null) p.Tier = Mathf.Clamp(p.Tier, 0, d.Upgrades.Length);
        }
        ValidateLoadout();
        Changed?.Invoke();
    }
    void OnApplicationQuit() { Save(); }
    /// Parses one save file; null when it is missing (error null) or unreadable / invalid (error set). The validity policy is
    /// unchanged: Version must be 1 and Level / Xp / Points must not be negative (such a file is treated as unreadable).
    static ProgressSave Read(string path, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var loaded = JsonUtility.FromJson<ProgressSave>(File.ReadAllText(path));
            if (loaded == null || loaded.Version != 1 || loaded.Level < 1 || loaded.Xp < 0 || loaded.Points < 0) throw new InvalidDataException("Invalid progression save.");
            return loaded;
        }
        catch (Exception e) { error = e.Message; return null; }
    }
    /// Repairs a readable save in place: lists that are missing are created, duplicate entries merged (highest tier / best
    /// record wins), empty ids dropped, counters clamped. Unknown ids (powers or modes from another build) are KEPT untouched.
    ProgressSave Repair(ProgressSave d)
    {
        void Note(string what) { Repairs.Add(what); }
        if (d.Powers == null) { d.Powers = new List<PowerOwnership>(); Note("Powers list missing"); }
        if (d.Rooftops == null) { d.Rooftops = new List<string>(); Note("Rooftops list missing"); }
        if (d.ModeRecords == null) d.ModeRecords = new List<ModeRecord>();
        if (d.SeenHints == null) d.SeenHints = new List<string>();
        var powers = new List<PowerOwnership>();
        foreach (var p in d.Powers)
        {
            if (p == null || string.IsNullOrEmpty(p.Id)) { Note("empty power entry"); continue; }
            var same = powers.Find(x => x.Id == p.Id);
            if (same == null) powers.Add(p); else { same.Tier = Mathf.Max(same.Tier, p.Tier); Note("duplicate power " + p.Id); }
        }
        d.Powers = powers;
        d.Rooftops = Distinct(d.Rooftops, "rooftop", Note); d.SeenHints = Distinct(d.SeenHints, "hint", Note);
        var records = new List<ModeRecord>();
        foreach (var r in d.ModeRecords)
        {
            if (r == null || string.IsNullOrEmpty(r.Id)) { Note("empty mode record"); continue; }
            r.BestScore = Mathf.Max(0, r.BestScore); r.BestWave = Mathf.Max(0, r.BestWave); r.Runs = Mathf.Max(0, r.Runs);
            var same = records.Find(x => x.Id == r.Id);
            if (same == null) records.Add(r);
            else { same.BestScore = Mathf.Max(same.BestScore, r.BestScore); same.BestWave = Mathf.Max(same.BestWave, r.BestWave); same.Runs = Mathf.Max(same.Runs, r.Runs); Note("duplicate mode record " + r.Id); }
        }
        d.ModeRecords = records;
        if (d.Level > MaxLevel) { d.Level = MaxLevel; Note("level clamped"); }
        d.SessionsPlayed = Mathf.Max(0, d.SessionsPlayed); d.SessionsWon = Mathf.Clamp(d.SessionsWon, 0, d.SessionsPlayed);
        d.BestSessionScore = Mathf.Max(0, d.BestSessionScore); d.LastSessionXp = Mathf.Max(0, d.LastSessionXp);
        return d;
    }
    static List<string> Distinct(List<string> list, string what, Action<string> note)
    {
        var result = new List<string>();
        foreach (var id in list) { if (string.IsNullOrEmpty(id) || result.Contains(id)) { note("duplicate/empty " + what); continue; } result.Add(id); }
        return result;
    }
}
