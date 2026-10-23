#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class VectorAssetInspection
{
    public static void Run()
    {
        var log=new StringBuilder();int exit=0;
        try
        {
            var suit=Resources.Load<ForgeCatalog>("ForgeCatalog").Hero("vector").Suit;
            var names=Regex.Matches(File.ReadAllText("Assets/Synty/SidekickCharacters/Characters/Starter/Starter_03/Starter_03.sk"),@"^- Name: (.+)$",RegexOptions.Multiline).Cast<Match>().Select(m=>m.Groups[1].Value.Trim()).ToArray();
            var files=Directory.GetFiles("Assets/Synty/SidekickCharacters/Resources/Meshes","*.fbx",SearchOption.AllDirectories);
            var parts=names.Select(n=>AssetDatabase.LoadAssetAtPath<GameObject>(files.Single(f=>Path.GetFileNameWithoutExtension(f)==n)).GetComponentInChildren<SkinnedMeshRenderer>()).OrderBy(r=>r.name,StringComparer.Ordinal).ToArray();
            int offset=0;var uv=suit.Body.uv;
            foreach(var p in parts)
            {
                var u=p.sharedMesh.uv;int errors=0;
                for(int i=0;i<u.Length;i++)if(offset+i>=uv.Length||(u[i]-uv[offset+i]).sqrMagnitude>1e-10f)errors++;
                log.AppendLine($"PART {p.name} offset={offset} vertices={u.Length} UV mismatches={errors}");offset+=u.Length;
            }
            log.AppendLine($"TOTAL parts={offset} combined={uv.Length}");
            if(offset!=uv.Length)throw new Exception("Vertex count mismatch");
        }
        catch(Exception e){log.AppendLine(e.ToString());exit=1;}
        Directory.CreateDirectory("Verification/VectorCustomization");File.WriteAllText("Verification/VectorCustomization/asset-inspection.txt",log.ToString());Debug.Log(log.ToString());EditorApplication.Exit(exit);
    }
}
#endif
