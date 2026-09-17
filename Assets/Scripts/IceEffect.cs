using UnityEngine;
[CreateAssetMenu(menuName = "Overpowered/Effects/Freeze")]
public sealed class IceEffect : PowerEffect
{
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        if (!user.FindTarget(user.Stats(power).Range, out RaycastHit hit)) return false;
        var npc = hit.collider.GetComponentInParent<CityNpc>();
        if (npc != null) { npc.Freeze(user.Stats(power).Duration); npc.Damage(user.Stats(power).Damage, user); return true; }
        if (hit.rigidbody == null || hit.rigidbody.isKinematic) return false;
        var frozen = hit.rigidbody.GetComponent<FrozenBody>();
        if (frozen == null) frozen = hit.rigidbody.gameObject.AddComponent<FrozenBody>();
        frozen.Apply(user.Stats(power).Duration);
        return true;
    }
}
public sealed class FrozenBody : MonoBehaviour
{
    Rigidbody body; RigidbodyConstraints prior; float until; bool applied;
    public void Apply(float seconds)
    {
        if (!applied) { body = GetComponent<Rigidbody>(); prior = body.constraints; applied = true; }
        body.constraints = RigidbodyConstraints.FreezeAll; until = Time.time + seconds;
    }
    void Update() { if (Time.time >= until) Destroy(this); }
    void OnDisable() { if (body != null && applied) body.constraints = prior; }
}
