using UnityEngine;
using UnityEngine.Rendering;

// Extends the existing session pool; every construct is collider-free and shares the built-in cube mesh.
public sealed partial class SignatureVfx
{
    readonly LineRenderer[] prismCore = new LineRenderer[8], prismGlow = new LineRenderer[8];
    float prismUntil;
    readonly Transform[] bloomRoots = new Transform[PlagueBloomEffect.PoolLimit];
    readonly Transform[,] petals = new Transform[PlagueBloomEffect.PoolLimit, 7];
    readonly LineRenderer[] sporeRings = new LineRenderer[PlagueBloomEffect.PoolLimit];
    readonly float[] bloomUntil = new float[PlagueBloomEffect.PoolLimit];
    public int ActiveBlooms { get { int n = 0; foreach (var root in bloomRoots) if (root != null && root.gameObject.activeSelf) n++; return n; } }
    public bool PrismVisible => prismCore[0] != null && prismCore[0].gameObject.activeSelf;

    Transform Solid(string label, Transform parent, Mesh mesh, CityColor color)
    {
        var go = new GameObject(label); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = CityMaterials.Get(color);
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        return go.transform;
    }
    void BuildFinalFour(Mesh cube)
    {
        for (int i = 0; i < prismCore.Length; i++)
        {
            prismCore[i] = MakeLine("Prism optical core " + i, i == 0 ? CityColor.Cream : CityColor.Cyan, 2, false, i == 0 ? .055f : .035f);
            prismGlow[i] = MakeLine("Prism optical afterglow " + i, i % 2 == 0 ? CityColor.Cyan : CityColor.Amber, 2, false, i == 0 ? .12f : .07f);
        }
        for (int b = 0; b < bloomRoots.Length; b++)
        {
            var root = new GameObject("Plague bloom " + b).transform; root.SetParent(transform, false); bloomRoots[b] = root;
            for (int p = 0; p < 7; p++) petals[b, p] = Solid(p == 6 ? "Toxic glowing heart" : "Toxic petal " + p, root, cube, p == 6 ? CityColor.Leaf : CityColor.Lawn);
            sporeRings[b] = MakeLine("Plague spore radius " + b, CityColor.Leaf, RingSegments, true, .1f);
            root.gameObject.SetActive(false);
        }
    }
    public void ShowPrism(int index, Vector3 from, Vector3 to, bool main, float seconds)
    {
        index = Mathf.Clamp(index, 0, 7); prismUntil = Mathf.Max(prismUntil, Time.time + seconds);
        prismCore[index].SetPosition(0, from); prismCore[index].SetPosition(1, to);
        prismGlow[index].SetPosition(0, from); prismGlow[index].SetPosition(1, to);
        prismCore[index].gameObject.SetActive(true); prismGlow[index].gameObject.SetActive(true);
    }
    public void HidePrism()
    {
        for (int i = 0; i < prismCore.Length; i++)
        {
            if (prismCore[i] != null) prismCore[i].gameObject.SetActive(false);
            if (prismGlow[i] != null) prismGlow[i].gameObject.SetActive(false);
        }
    }
    public void ShowBloom(int index, Vector3 center, float radius, float phase, float remaining)
    {
        if (index < 0 || index >= bloomRoots.Length) return;
        bloomUntil[index] = Time.time + remaining + .1f;
        var root = bloomRoots[index]; root.position = center;
        float size = index == 0 ? 1.3f : .7f, pulse = 1f + .09f * Mathf.Sin(phase * Mathf.PI * 2);
        for (int p = 0; p < 6; p++)
        {
            float a = p * 60f * Mathf.Deg2Rad;
            var petal = petals[index, p];
            petal.localPosition = new Vector3(Mathf.Cos(a) * size * .6f, size * .55f, Mathf.Sin(a) * size * .6f);
            petal.localRotation = Quaternion.Euler(24f, -p * 60f, 32f);
            petal.localScale = new Vector3(size * .4f, size * 1.6f * pulse, size * .4f);
        }
        petals[index, 6].localPosition = Vector3.up * size * .75f;
        petals[index, 6].localRotation = Quaternion.Euler(45f, Time.time * 50f, 45f);
        petals[index, 6].localScale = Vector3.one * size * .65f * pulse;
        root.gameObject.SetActive(true);
        SetCircle(sporeRings[index], center, radius * Mathf.Lerp(.85f, 1f, Mathf.Repeat(phase, 1)), .1f, .04f, phase);
        sporeRings[index].gameObject.SetActive(true);
    }
    public void HideBloom(int index)
    {
        if (bloomRoots[index] != null) bloomRoots[index].gameObject.SetActive(false);
        if (sporeRings[index] != null) sporeRings[index].gameObject.SetActive(false);
    }
    public void HideBlooms() { for (int b = 0; b < bloomRoots.Length; b++) HideBloom(b); }
    void HideFinalFour() { HidePrism(); HideBlooms(); }
    void UpdateFinalFour()
    {
        if (Time.time >= prismUntil) HidePrism();
        for (int b = 0; b < bloomRoots.Length; b++) if (Time.time >= bloomUntil[b]) HideBloom(b);
    }
}
