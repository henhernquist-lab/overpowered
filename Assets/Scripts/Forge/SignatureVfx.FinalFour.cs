using UnityEngine;
using UnityEngine.Rendering;

// Extends the existing session pool; every construct is collider-free and shares the built-in cube mesh.
public sealed partial class SignatureVfx
{
    readonly LineRenderer[] prismCore = new LineRenderer[8], prismGlow = new LineRenderer[8];
    float prismUntil;
    Transform meteorRoot, abyssRoot, prismFocus;
    readonly Transform[] meteorChunks = new Transform[9];
    readonly Transform[,] tendrils = new Transform[8, 3];
    LineRenderer psychicRing, meteorShock, abyssRim, abyssCore;
    float meteorUntil, abyssUntil;
    public bool MeteorVisible => meteorRoot != null && meteorRoot.gameObject.activeSelf;
    public bool AbyssVisible => abyssRoot != null && abyssRoot.gameObject.activeSelf;
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
        prismFocus = Solid("Prism refracting focus", transform, cube, CityColor.Cream);
        prismFocus.GetComponent<MeshRenderer>().sharedMaterial = CityMaterials.GetVfx(CityColor.Cream);
        prismFocus.gameObject.SetActive(false);
        meteorRoot = new GameObject("Meteor bounded debris").transform; meteorRoot.SetParent(transform, false);
        for (int i = 0; i < meteorChunks.Length; i++) meteorChunks[i] = Solid("Meteor chunk " + i, meteorRoot, cube, i % 3 == 0 ? CityColor.Metal : CityColor.Slate);
        meteorRoot.gameObject.SetActive(false);
        psychicRing = MakeLine("Meteor psychic containment", CityColor.UiPurple, RingSegments, true, .2f);
        meteorShock = MakeLine("Meteor ground shock", CityColor.Sand, RingSegments, true, .2f);
        abyssRoot = new GameObject("Abyss solid tendrils").transform; abyssRoot.SetParent(transform, false);
        for (int t = 0; t < 8; t++) for (int part = 0; part < 3; part++) { tendrils[t, part] = Solid("Shadow tendril " + t + " segment " + part, abyssRoot, cube, part == 1 ? CityColor.UiPurple : CityColor.UiNavy); tendrils[t, part].gameObject.SetActive(false); }
        abyssRoot.gameObject.SetActive(false);
        abyssRim = MakeLine("Abyss spiral rim", CityColor.UiPurple, RingSegments, true, .4f);
        abyssCore = MakeLine("Abyss readable center", CityColor.UiNavy, RingSegments, true, .7f);
        for (int i = 0; i < prismCore.Length; i++)
        {
            prismCore[i] = MakeLine("Prism optical core " + i, i == 0 ? CityColor.Cream : CityColor.Cyan, 2, false, i == 0 ? .055f : .035f);
            prismGlow[i] = MakeLine("Prism optical afterglow " + i, i % 2 == 0 ? CityColor.Cyan : CityColor.Amber, 2, false, i == 0 ? .12f : .07f);
        }
        for (int b = 0; b < bloomRoots.Length; b++)
        {
            var root = new GameObject("Plague bloom " + b).transform; root.SetParent(transform, false); bloomRoots[b] = root;
            for (int p = 0; p < 7; p++) petals[b, p] = Solid(p == 6 ? "Toxic glowing heart" : "Toxic petal " + p, root, cube, p == 6 ? CityColor.Leaf : CityColor.Lawn);
            petals[b, 6].GetComponent<MeshRenderer>().sharedMaterial = CityMaterials.GetVfx(CityColor.Leaf);
            sporeRings[b] = MakeLine("Plague spore radius " + b, CityColor.Leaf, RingSegments, true, .1f);
            root.gameObject.SetActive(false);
        }
    }
    public void ShowPrism(int index, Vector3 from, Vector3 to, bool main, float seconds)
    {
        if (main) { prismFocus.position = to; prismFocus.localRotation = Quaternion.Euler(45, Time.time * 60, 45); prismFocus.localScale = Vector3.one * .28f; prismFocus.gameObject.SetActive(true); }
        index = Mathf.Clamp(index, 0, 7); prismUntil = Mathf.Max(prismUntil, Time.time + seconds);
        prismCore[index].SetPosition(0, from); prismCore[index].SetPosition(1, to);
        prismGlow[index].SetPosition(0, from); prismGlow[index].SetPosition(1, to);
        prismCore[index].gameObject.SetActive(true); prismGlow[index].gameObject.SetActive(true);
    }
    public void HidePrism()
    {
        if (prismFocus != null) prismFocus.gameObject.SetActive(false);
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
    public void ShowMeteor(Vector3 center, float height, float assembly, float fall, float burst, float radius)
    {
        meteorUntil = Time.time + .3f; meteorRoot.position = center; meteorRoot.gameObject.SetActive(true);
        float y = Mathf.Lerp(height, 1.4f, fall * fall);
        for (int i = 0; i < meteorChunks.Length; i++)
        {
            float a = i * 2.4f;
            Vector3 offset = new Vector3(Mathf.Cos(a), Mathf.Sin(i * 1.7f) * .6f, Mathf.Sin(a));
            float spread = burst < 0 ? Mathf.Lerp(4.5f, 1f, assembly) : 1f + burst * 5;
            var chunk = meteorChunks[i];
            chunk.localPosition = offset * spread + Vector3.up * (burst < 0 ? y : Mathf.Lerp(1.4f, .1f, burst));
            chunk.localRotation = Quaternion.Euler(i * 33 + Time.time * 14, i * 51, i * 17);
            chunk.localScale = Vector3.one * (i == 0 ? 2.6f : 1.3f) * (burst < 0 ? 1 : 1 - burst);
        }
        SetCircle(psychicRing, center + Vector3.up * y, 2.5f, 0, .08f, Time.time * 4);
        psychicRing.gameObject.SetActive(burst < 0);
        SetCircle(meteorShock, center, burst < 0 ? radius : Mathf.Lerp(.8f, radius, burst), .1f, .03f, Time.time);
        meteorShock.gameObject.SetActive(true);
    }
    public void HideMeteor()
    {
        if (meteorRoot != null) meteorRoot.gameObject.SetActive(false);
        if (psychicRing != null) psychicRing.gameObject.SetActive(false);
        if (meteorShock != null) meteorShock.gameObject.SetActive(false);
    }
    public void ShowAbyss(Vector3 center, float radius, float phase)
    {
        abyssUntil = Time.time + .3f; abyssRoot.gameObject.SetActive(true);
        SetCircle(abyssRim, center, radius, .12f, .08f, phase * 24);
        SetCircle(abyssCore, center, 1.2f + Mathf.Sin(phase * 15) * .2f, .16f, .2f, phase * 36);
        abyssRim.gameObject.SetActive(true); abyssCore.gameObject.SetActive(true);
    }
    public void ShowTendril(int index, Vector3 center, Vector3 target, float phase)
    {
        index = Mathf.Clamp(index, 0, 7); Vector3 from = center + Vector3.up * .2f;
        for (int part = 0; part < 3; part++)
        {
            float u = (part + 1) / 3f;
            Vector3 to = Vector3.Lerp(center, target, u) + Vector3.up * (.2f + Mathf.Sin(u * Mathf.PI) * (.7f + .15f * Mathf.Sin(phase * 5 + index)));
            var segment = tendrils[index, part]; segment.gameObject.SetActive(true);
            segment.position = (from + to) * .5f; segment.rotation = Quaternion.LookRotation(to - from);
            segment.localScale = new Vector3(.32f - part * .065f, .32f - part * .065f, Mathf.Max(.01f, Vector3.Distance(from, to)));
            from = to;
        }
    }
    public void HideTendril(int index) { for (int part = 0; part < 3; part++) tendrils[index, part].gameObject.SetActive(false); }
    public void HideAbyss()
    {
        if (abyssRoot != null) { abyssRoot.gameObject.SetActive(false); for (int i = 0; i < 8; i++) HideTendril(i); }
        if (abyssRim != null) abyssRim.gameObject.SetActive(false);
        if (abyssCore != null) abyssCore.gameObject.SetActive(false);
    }
    void HideFinalFour() { HidePrism(); HideBlooms(); HideMeteor(); HideAbyss(); }
    void UpdateFinalFour()
    {
        if (Time.time >= meteorUntil) HideMeteor();
        if (Time.time >= abyssUntil) HideAbyss();
        if (Time.time >= prismUntil) HidePrism();
        for (int b = 0; b < bloomRoots.Length; b++) if (Time.time >= bloomUntil[b]) HideBloom(b);
    }
}
