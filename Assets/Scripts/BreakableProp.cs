using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class BreakableProp : MonoBehaviour
{
    [SerializeField] int hitsToBreak = 3;
    int hits;
    public void HitByPunch()
    {
        hits++;
        if (hits < hitsToBreak) return;
        for (int i = 0; i < 5; i++)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.transform.position = transform.position + Random.insideUnitSphere * .35f;
            shard.transform.localScale = Vector3.one * .25f;
            Rigidbody rb = shard.AddComponent<Rigidbody>(); rb.mass = .35f;
            rb.AddExplosionForce(PrototypeTuning.PunchForce * .25f, transform.position - transform.forward, 2.5f, .2f, ForceMode.Impulse);
            Destroy(shard, 7f);
        }
        Destroy(gameObject);
    }
}
