using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class BreakableProp : MonoBehaviour
{
    public static event System.Action<Vector3> Destroyed;
    [SerializeField] int hitsToBreak = 3;
    int hits;
    PropSettings config = new PropSettings();
    float health;
    bool broken;
    public void Configure(PropSettings settings) { config = settings; hitsToBreak = settings.HitsToBreak; health = settings.Health; }
    void Awake() { health = config.Health; }
    void Update() { if (transform.position.y < config.CleanupBelow) Destroy(gameObject); }
    public void TakeDamage(float damage, PowerUser source)
    {
        if (broken) return;
        health -= damage; hits++;
        if (health <= 0 || hits >= hitsToBreak) Break();
    }
    public void HitByPunch()
    {
        hits++;
        if (hits < hitsToBreak) return;
        Break();
    }
    void Break()
    {
        if (broken) return; broken = true;
        Destroyed?.Invoke(transform.position);
        GetComponent<EncounterProp>()?.Broken();
        WorldSession.Instance?.OnDestruction(transform.position);
        GetComponent<Collider>().enabled = false;
        Rigidbody original = GetComponent<Rigidbody>();
        for (int i = 0; i < config.ShardCount; i++)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.GetComponent<Renderer>().sharedMaterial=GetComponentInChildren<Renderer>()?.sharedMaterial ?? CityMaterials.Get(CityColor.Wood);
            shard.transform.position = transform.position + Random.insideUnitSphere * config.ShardSpread;
            shard.transform.localScale = Vector3.one * config.ShardSize;
            Rigidbody rb = shard.AddComponent<Rigidbody>(); rb.mass = config.ShardMass; rb.linearVelocity = original.linearVelocity;
            rb.AddExplosionForce(PrototypeTuning.PunchForce * config.ShardImpulseFraction, transform.position - transform.forward, config.ShardRadius, config.ShardLift, ForceMode.Impulse);
            Destroy(shard, config.ShardLifetime);
        }
        Destroy(gameObject);
    }
}
