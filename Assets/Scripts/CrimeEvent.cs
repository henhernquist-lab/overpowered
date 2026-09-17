using UnityEngine;
public enum CrimeKind { Mugging, Robbery, Fire }
public sealed class CrimeEvent : MonoBehaviour
{
    public CrimeKind Kind;
    public bool Resolved { get; private set; }
    public CityNpc Criminal;
    public float Suppression { get; private set; }
    public string Prompt => Kind==CrimeKind.Fire ? "Hold R: extinguish (Hero) / fuel fire (Villain)" :
        "R: rescue/report (Hero) / join crime (Villain); or defeat criminal as Hero";
    void Update()
    {
        var w=WorldSession.Instance; if (w==null || Resolved || w.MenuOpen || w.PlayerDead) return;
        if (Vector3.Distance(w.Hero.transform.position,transform.position-Vector3.up*w.Tuning.Crimes.MarkerHeight)>w.Tuning.Crimes.ResolveRadius) return;
        if (Kind==CrimeKind.Fire)
        {
            if (Input.GetKey(KeyCode.R)) { Suppression+=Time.deltaTime; if (Suppression>=w.Tuning.Crimes.FireResolutionSeconds) Resolve(); }
            else Suppression=0;
        }
        else if (Input.GetKeyDown(KeyCode.R)) Resolve();
    }
    public bool Resolve()
    {
        if (Resolved) return false;
        Resolved=true;
        var world=WorldSession.Instance;
        world.ResolveCrime(this);
        if (Criminal!=null && !Criminal.Dead) Destroy(Criminal.gameObject);
        Destroy(gameObject); return true;
    }
    public void CriminalDefeated()
    {
        if (WorldSession.Instance.Progression.Data.Side==PlayerSide.Hero) Resolve();
    }
}
