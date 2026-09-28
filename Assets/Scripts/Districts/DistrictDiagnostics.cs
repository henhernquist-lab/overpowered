using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// OFF by default. Per-district play diagnostics for LOCAL runs: WorldSession attaches it only when Enabled (editor menu
/// Overpowered/Diagnostics toggle, the -districtDiagnostics command-line flag, or a verifier calling Attach). Nothing is
/// logged per frame: population, encounters, stages, Heat and pursuit are SAMPLED every SampleSeconds, props every
/// PropSampleSeconds and renderers every RendererSampleSeconds (plus once at the start and end); discrete facts (hits, player
/// damage / defeats, outcomes, style, XP, Heat gained, civilian outcomes, pursuits) are counted from existing events. All of
/// it is aggregated into BucketSeconds time buckets per district (-1 = outside every district) plus whole-session totals,
/// and written as CSV + JSON when the session object is destroyed (or when Write is called). Read-only: it never changes
/// gameplay, saves or tuning. The time spent sampling is measured and written too.
public sealed class DistrictDiagnostics : MonoBehaviour
{
    public static bool Enabled;
    public const string CommandLineFlag = "-districtDiagnostics";
    public float SampleSeconds = 1f, PropSampleSeconds = 5f, RendererSampleSeconds = 30f, BucketSeconds = 60f, NearbyRadius = 40f;
    [Tooltip("Write the files when the session object is destroyed (scene change / session end).")] public bool AutoWrite = true;
    public string Folder;
    public static string DefaultFolder => Application.isEditor ? "Verification/DistrictDiagnostics/" : Path.Combine(Application.persistentDataPath, "DistrictDiagnostics");
    public static bool Requested() => Enabled || Array.IndexOf(Environment.GetCommandLineArgs(), CommandLineFlag) >= 0;
    public static DistrictDiagnostics Attach(WorldSession world)
    {
        var d = world.GetComponent<DistrictDiagnostics>(); if (d == null) d = world.gameObject.AddComponent<DistrictDiagnostics>();
        d.Begin(world); return d;
    }

    public sealed class Stats
    {
        public int Bucket, District;
        public float Seconds; public int Samples, HeatSamples, PropSamples, RendererSamples, Entries;
        public int CiviliansSum, CiviliansMax, HostilesSum, HostilesMax, NearbySum, NearbyMax, EncountersSum, EncountersMax;
        public int PropsSum, PropsMax, RenderersLast, VisibleRenderersLast;
        public float HeatSum, HeatMax, HeatGained;
        public readonly float[] PursuitSeconds = new float[5];
        public int PursuitsStarted, EncountersSpawned, PlayerHits, Kills, PlayerDamageEvents, PlayerDefeats, Successes, Failures, Style, Xp;
        public readonly int[] CivilianOutcomes = new int[7];
        public readonly SortedDictionary<string, int> StageSamples = new SortedDictionary<string, int>(StringComparer.Ordinal);
    }
    public WorldSession World { get; private set; }
    public int TotalSamples { get; private set; }
    public int PropSamples { get; private set; }
    public int RendererSamples { get; private set; }
    public double SamplingMilliseconds { get; private set; }
    public int FramesObserved { get; private set; }
    public float StartedAt { get; private set; }
    public string[] LastWritten { get; private set; }
    public int DistrictCount => names != null ? names.Length : World != null ? World.City.DistrictDefinitions.Count : 0;
    readonly Dictionary<long, Stats> buckets = new Dictionary<long, Stats>();
    readonly Dictionary<int, Stats> totals = new Dictionary<int, Stats>();
    int[] civ, hostile, nearby, encounters, props, renderers, visible;
    float nextSample, nextProps, nextRenderers, lastSample; int playerDistrict = int.MinValue; bool begun, written;
    GameModeSession mode; StyleScoreTracker style; PowerUser powers; PlayerProgression progression; string[] names; string modeId, side;

    public void Begin(WorldSession world)
    {
        if (begun) return; begun = true; World = world;
        int n = DistrictCount + 1;   // slot 0 = outside (-1)
        civ = new int[n]; hostile = new int[n]; nearby = new int[n]; encounters = new int[n]; props = new int[n]; renderers = new int[n]; visible = new int[n];
        StartedAt = lastSample = Time.time; nextSample = Time.time; nextProps = Time.time; nextRenderers = Time.time + RendererSampleSeconds;
        powers = world.Powers; progression = world.Progression; mode = world.Mode; style = mode != null ? mode.Style : null;
        names = new string[n - 1]; for (int i = 0; i < names.Length; i++) names[i] = world.City.DistrictDefinitions[i].Name;
        modeId = mode != null ? mode.Definition.Id : "free-roam"; side = progression != null ? progression.Data.Side.ToString() : "";
        if (powers != null) powers.Hit += OnHit;
        if (progression != null) progression.XpGranted += OnXp;
        if (mode != null) mode.EncounterResolved += OnResolved;
        if (style != null) style.Awarded += OnStyle;
        world.PlayerDamaged += OnPlayerDamaged; world.HeatAdded += OnHeat; world.EncounterSpawned += OnEncounterSpawned;
        if (world.Pursuit != null) world.Pursuit.StateChanged += OnPursuit;
        if (world.Civilians != null) world.Civilians.Recorded += OnCivilian;
        SampleRenderers();
    }
    void Unsubscribe()
    {
        if (ReferenceEquals(World, null)) return;
        if (powers != null) powers.Hit -= OnHit;
        if (progression != null) progression.XpGranted -= OnXp;
        if (mode != null) mode.EncounterResolved -= OnResolved;
        if (style != null) style.Awarded -= OnStyle;
        World.PlayerDamaged -= OnPlayerDamaged; World.HeatAdded -= OnHeat; World.EncounterSpawned -= OnEncounterSpawned;
        if (World.Pursuit != null) World.Pursuit.StateChanged -= OnPursuit;
        if (World.Civilians != null) World.Civilians.Recorded -= OnCivilian;
    }
    bool Running => begun && World != null && World.Hero != null && (World.Mode == null || (!World.Mode.Ended && !World.Mode.Paused));
    void Update()
    {
        if (!Running) { lastSample = Time.time; return; }
        FramesObserved++;
        if (Time.time < nextSample) return;
        nextSample = Time.time + Mathf.Max(.05f, SampleSeconds);
        Sample();
    }
    /// One sample now (verification: deterministic sampling without waiting).
    public void Sample()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        float now = Time.time, dt = Mathf.Max(0f, now - lastSample); lastSample = now;
        Vector3 player = World.Hero.transform.position; int here = World.City.DistrictAt(player);
        var p = Get(here);
        p.Seconds += dt; Total(here).Seconds += dt;
        int state = World.Pursuit != null ? (int)World.Pursuit.State : 0;
        p.PursuitSeconds[state] += dt; Total(here).PursuitSeconds[state] += dt;
        if (here != playerDistrict) { if (playerDistrict != int.MinValue) { p.Entries++; Total(here).Entries++; } playerDistrict = here; }
        p.HeatSamples++; p.HeatSum += World.Heat; p.HeatMax = Mathf.Max(p.HeatMax, World.Heat);
        var t = Total(here); t.HeatSamples++; t.HeatSum += World.Heat; t.HeatMax = Mathf.Max(t.HeatMax, World.Heat);
        Array.Clear(civ, 0, civ.Length); Array.Clear(hostile, 0, hostile.Length); Array.Clear(nearby, 0, nearby.Length); Array.Clear(encounters, 0, encounters.Length);
        float near = NearbyRadius * NearbyRadius;
        foreach (var npc in World.Npcs)
        {
            if (npc == null || npc.Dead || !npc.gameObject.activeInHierarchy) continue;
            var at = npc.transform.position; int s = World.City.DistrictAt(at) + 1;
            if (npc.Role == NpcRole.Civilian) civ[s]++;
            if (npc.Hostile) hostile[s]++;
            if ((at - player).sqrMagnitude <= near) nearby[s]++;
        }
        foreach (var crime in World.Crimes)
        {
            if (crime == null || crime.Resolved || crime.Encounter == null) continue;
            int d = World.City.DistrictAt(crime.Encounter.Site); encounters[d + 1]++;
            if (crime.Encounter.Scenario is StagedState staged && staged.Stage != null)
            {
                string key = staged.Stage.Kind + (string.IsNullOrEmpty(staged.Stage.Label) ? "" : " " + staged.Stage.Label);
                Add(Get(d).StageSamples, key); Add(Total(d).StageSamples, key);
            }
        }
        bool doProps = now >= nextProps;
        if (doProps) { nextProps = now + Mathf.Max(SampleSeconds, PropSampleSeconds); CountProps(); PropSamples++; }
        if (RendererSampleSeconds > 0f && now >= nextRenderers) SampleRenderers();
        for (int s = 0; s < civ.Length; s++) { Accumulate(Get(s - 1), s, doProps); Accumulate(Total(s - 1), s, doProps); }
        TotalSamples++;
        SamplingMilliseconds += watch.Elapsed.TotalMilliseconds;
    }
    void Accumulate(Stats st, int s, bool doProps)
    {
        st.Samples++;
        st.CiviliansSum += civ[s]; st.CiviliansMax = Mathf.Max(st.CiviliansMax, civ[s]);
        st.HostilesSum += hostile[s]; st.HostilesMax = Mathf.Max(st.HostilesMax, hostile[s]);
        st.NearbySum += nearby[s]; st.NearbyMax = Mathf.Max(st.NearbyMax, nearby[s]);
        st.EncountersSum += encounters[s]; st.EncountersMax = Mathf.Max(st.EncountersMax, encounters[s]);
        if (doProps) { st.PropSamples++; st.PropsSum += props[s]; st.PropsMax = Mathf.Max(st.PropsMax, props[s]); }
    }
    void CountProps()
    {
        Array.Clear(props, 0, props.Length);
        foreach (var prop in FindObjectsByType<BreakableProp>()) if (prop != null && prop.isActiveAndEnabled) props[World.City.DistrictAt(prop.transform.position) + 1]++;
    }
    /// Enabled renderers (and those visible to any camera) by bounds centre. A full scene scan, so it is rare.
    public void SampleRenderers()
    {
        if (World == null || renderers == null) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        nextRenderers = Time.time + Mathf.Max(SampleSeconds, RendererSampleSeconds);
        Array.Clear(renderers, 0, renderers.Length); Array.Clear(visible, 0, visible.Length);
        foreach (var r in FindObjectsByType<Renderer>())
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            int s = World.City.DistrictAt(r.bounds.center) + 1; renderers[s]++; if (r.isVisible) visible[s]++;
        }
        long bucket = BucketOf(Time.time);
        for (int s = 0; s < renderers.Length; s++) { Renderers(Get(s - 1, bucket), s); Renderers(Total(s - 1), s); }
        RendererSamples++;
        SamplingMilliseconds += watch.Elapsed.TotalMilliseconds;
    }
    void Renderers(Stats st, int s) { st.RenderersLast = renderers[s]; st.VisibleRenderersLast = visible[s]; st.RendererSamples++; }
    static void Add(SortedDictionary<string, int> map, string key) { map.TryGetValue(key, out int v); map[key] = v + 1; }
    long BucketOf(float time) => (long)Mathf.Floor(Mathf.Max(0f, time - StartedAt) / Mathf.Max(1f, BucketSeconds));
    Stats Get(int district) => Get(district, BucketOf(Time.time));
    Stats Get(int district, long bucket)
    {
        long key = bucket * 1024 + (district + 1);
        if (!buckets.TryGetValue(key, out var s)) buckets[key] = s = new Stats { Bucket = (int)bucket, District = district };
        return s;
    }
    Stats Total(int district) { if (!totals.TryGetValue(district, out var s)) totals[district] = s = new Stats { Bucket = -1, District = district }; return s; }
    /// Session totals for a district (-1 = outside); a fresh empty record if nothing was seen there.
    public Stats TotalsFor(int district) => totals.TryGetValue(district, out var s) ? s : new Stats { Bucket = -1, District = district };
    public IEnumerable<Stats> Buckets => buckets.Values;
    int PlayerDistrictNow => World.City.DistrictAt(World.Hero.transform.position);
    void Count(int district, Action<Stats> add) { if (!begun || World == null || World.Hero == null) return; add(Get(district)); add(Total(district)); }

    // ---- events (discrete facts, counted once each where they happen)
    void OnHit(PowerHit hit)
    {
        if (hit.Npc == null || !(hit.Dealt > 0f)) return;
        Count(World.City.DistrictAt(hit.Npc.transform.position), s => { s.PlayerHits++; if (hit.Killed) s.Kills++; });
    }
    void OnPlayerDamaged(bool dead) => Count(PlayerDistrictNow, s => { s.PlayerDamageEvents++; if (dead) s.PlayerDefeats++; });
    void OnResolved(EncounterOutcome o) => Count(World.City.DistrictAt(o.Site), s => { if (o.Success) s.Successes++; else s.Failures++; });
    void OnStyle(StyleAward a) => Count(PlayerDistrictNow, s => s.Style += a.Points);
    void OnXp(XpGrant g) => Count(g.HasPosition ? World.City.DistrictAt(g.Position) : PlayerDistrictNow, s => s.Xp += g.Amount);
    void OnHeat(float delta) { if (delta > 0f) Count(PlayerDistrictNow, s => s.HeatGained += delta); }
    void OnEncounterSpawned(CrimeEncounter e) { if (e != null) Count(World.City.DistrictAt(e.Site), s => s.EncountersSpawned++); }
    void OnPursuit(PursuitState from, PursuitState to) { if (to == PursuitState.Pursued && from != PursuitState.Pursued) Count(PlayerDistrictNow, s => s.PursuitsStarted++); }
    void OnCivilian(CivilianOutcome outcome, CityNpc npc) { if (npc != null) Count(World.City.DistrictAt(npc.transform.position), s => s.CivilianOutcomes[(int)outcome]++); }

    // ---- output
    void OnDestroy()
    {
        if (begun && AutoWrite && !written)
        {
            try { Write(string.IsNullOrEmpty(Folder) ? DefaultFolder : Folder); }
            catch (Exception e) { Debug.LogWarning("District diagnostics not written: " + e.Message); }
        }
        Unsubscribe();
    }
    string DistrictName(int d) => d < 0 ? "(outside)" : d < names.Length ? names[d] : "district " + d;
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string Avg(int sum, int samples) => samples > 0 ? F(sum / (float)samples) : "";
    static string Stages(Stats s) { var parts = new List<string>(); foreach (var p in s.StageSamples) parts.Add(p.Key.Replace(';', ',') + ":" + p.Value); return string.Join(";", parts); }
    static string Csv(string v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
    public const string CsvHeader = "bucket,bucket_start_s,district_index,district,player_seconds,entries,samples,civilians_avg,civilians_max,hostiles_avg,hostiles_max,nearby_npcs_avg,nearby_npcs_max,encounters_avg,encounters_max,encounters_spawned,stage_samples,props_avg,props_max,renderers,visible_renderers,heat_avg,heat_max,heat_gained,pursuit_clear_s,pursuit_alerted_s,pursuit_pursued_s,pursuit_searching_s,pursuit_escaped_s,pursuits_started,player_hits,kills,player_damage_events,player_defeats,successes,failures,style,xp,civ_rescued,civ_harmed_hostile,civ_harmed_player,civ_harmed_environment,civ_lost_in_mission,civ_escorted,civ_killed";
    string Row(Stats s) => string.Join(",", s.Bucket, s.Bucket < 0 ? "" : F(s.Bucket * Mathf.Max(1f, BucketSeconds)), s.District, Csv(DistrictName(s.District)), F(s.Seconds), s.Entries, s.Samples,
        Avg(s.CiviliansSum, s.Samples), s.CiviliansMax, Avg(s.HostilesSum, s.Samples), s.HostilesMax, Avg(s.NearbySum, s.Samples), s.NearbyMax, Avg(s.EncountersSum, s.Samples), s.EncountersMax, s.EncountersSpawned, Csv(Stages(s)),
        Avg(s.PropsSum, s.PropSamples), s.PropsMax, s.RendererSamples > 0 ? s.RenderersLast.ToString() : "", s.RendererSamples > 0 ? s.VisibleRenderersLast.ToString() : "",
        s.HeatSamples > 0 ? F(s.HeatSum / s.HeatSamples) : "", F(s.HeatMax), F(s.HeatGained),
        F(s.PursuitSeconds[0]), F(s.PursuitSeconds[1]), F(s.PursuitSeconds[2]), F(s.PursuitSeconds[3]), F(s.PursuitSeconds[4]), s.PursuitsStarted,
        s.PlayerHits, s.Kills, s.PlayerDamageEvents, s.PlayerDefeats, s.Successes, s.Failures, s.Style, s.Xp,
        s.CivilianOutcomes[0], s.CivilianOutcomes[1], s.CivilianOutcomes[2], s.CivilianOutcomes[3], s.CivilianOutcomes[4], s.CivilianOutcomes[5], s.CivilianOutcomes[6]);
    /// Writes <stem>-buckets.csv, <stem>-totals.csv and <stem>.json into `folder`; returns the three paths.
    public string[] Write(string folder, string stem = null)
    {
        if (Running) SampleRenderers();   // end-of-session renderer snapshot (skipped while the scene is being torn down)
        Directory.CreateDirectory(folder);
        stem ??= "district-diagnostics-" + modeId + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var ordered = new List<Stats>(buckets.Values); ordered.Sort((a, b) => a.Bucket != b.Bucket ? a.Bucket.CompareTo(b.Bucket) : a.District.CompareTo(b.District));
        var csv = new StringBuilder(CsvHeader).Append('\n'); foreach (var s in ordered) csv.Append(Row(s)).Append('\n');
        var totalRows = new List<Stats>(totals.Values); totalRows.Sort((a, b) => a.District.CompareTo(b.District));
        var tcsv = new StringBuilder(CsvHeader).Append('\n'); foreach (var s in totalRows) tcsv.Append(Row(s)).Append('\n');
        var json = new StringBuilder("{\n");
        json.Append($"  \"mode\": \"{modeId}\",\n  \"side\": \"{side}\",\n");
        json.Append($"  \"sessionSeconds\": {F(Time.time - StartedAt)},\n  \"sampleSeconds\": {F(SampleSeconds)},\n  \"propSampleSeconds\": {F(PropSampleSeconds)},\n  \"rendererSampleSeconds\": {F(RendererSampleSeconds)},\n  \"bucketSeconds\": {F(BucketSeconds)},\n");
        json.Append($"  \"samples\": {TotalSamples},\n  \"propSamples\": {PropSamples},\n  \"rendererSamples\": {RendererSamples},\n  \"framesObserved\": {FramesObserved},\n  \"samplingMilliseconds\": {SamplingMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)},\n");
        var districts = ReferenceEquals(World, null) ? null : World.Districts; var ledger = ReferenceEquals(World, null) ? null : World.Civilians;
        json.Append($"  \"districtLayerActive\": {(!ReferenceEquals(districts, null) && districts.Active ? "true" : "false")},\n");
        json.Append($"  \"civilians\": \"{(!ReferenceEquals(ledger, null) ? ledger.Summary.ToString() : "")}\",\n  \"districts\": [\n");
        for (int i = 0; i < totalRows.Count; i++)
        {
            var s = totalRows[i]; var cols = CsvHeader.Split(','); var vals = SplitCsv(Row(s));
            json.Append("    {");
            for (int c = 0; c < cols.Length; c++)
            {
                string v = vals[c]; bool number = v.Length > 0 && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                json.Append(c > 0 ? ", " : "").Append('"').Append(cols[c]).Append("\": ").Append(number ? v : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
            }
            json.Append(i < totalRows.Count - 1 ? "},\n" : "}\n");
        }
        json.Append("  ]\n}\n");
        var paths = new[] { Path.Combine(folder, stem + "-buckets.csv"), Path.Combine(folder, stem + "-totals.csv"), Path.Combine(folder, stem + ".json") };
        File.WriteAllText(paths[0], csv.ToString()); File.WriteAllText(paths[1], tcsv.ToString()); File.WriteAllText(paths[2], json.ToString());
        written = true; LastWritten = paths;
        return paths;
    }
    /// Minimal CSV splitter for the rows this class writes (quoted fields may contain commas; "" escapes a quote).
    public static string[] SplitCsv(string line)
    {
        var result = new List<string>(); var cur = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else quoted = false; } else cur.Append(c); }
            else if (c == '"') quoted = true;
            else if (c == ',') { result.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        result.Add(cur.ToString()); return result.ToArray();
    }
}
