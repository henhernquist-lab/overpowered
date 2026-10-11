#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// Explicit one-time importer for the nine user-supplied X Bot motions. This only updates their
/// ModelImporter settings; it does not rebuild the shared controller or modify any model prefab.
public static class MixamoClipSetup
{
    [MenuItem("Overpowered/Animation/Import new Mixamo clips as Humanoid")]
    public static void Import()
    {
        Directory.CreateDirectory("Verification/AnimationIntegration");
        var lines=new List<string>();
        foreach(var name in MixamoClipAudit.Names)
        {
            string path="Assets/Animations/"+name+".fbx";
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            if(importer==null)throw new Exception("Missing supplied Mixamo FBX: "+path);
            importer.animationType=ModelImporterAnimationType.Human;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation=true;importer.optimizeGameObjects=false;
            importer.importCameras=false;importer.importLights=false;importer.addCollider=false;
            var clips=importer.defaultClipAnimations;
            if(clips.Length==0)throw new Exception("No source animation take: "+path);
            foreach(var clip in clips)
            {
                clip.name=name.Substring("X Bot@".Length);
                bool loop=name=="X Bot@Climbing Ladder"||name=="X Bot@Falling";
                // The 11.37 s Falling take ends in a ground collapse and stand-up. Only its
                // first three seconds depict sustained airborne falling; gameplay owns landing.
                if(name=="X Bot@Falling")clip.lastFrame=90;
                clip.loopTime=loop;clip.loopPose=loop;
                // Bake all FBX root translation and rotation into the presentation pose. The
                // CharacterController and NavMeshAgent remain the only world-position owners.
                clip.lockRootRotation=true;clip.keepOriginalOrientation=true;
                clip.lockRootPositionXZ=true;clip.keepOriginalPositionXZ=true;
                clip.lockRootHeightY=true;clip.keepOriginalPositionY=true;
            }
            importer.clipAnimations=clips;importer.SaveAndReimport();
            var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            var motion=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c=>!c.name.StartsWith("__preview__"));
            if(avatar==null||!avatar.isValid||!avatar.isHuman||motion==null||!motion.humanMotion)
                throw new Exception("Humanoid import or Avatar invalid: "+path);
            lines.Add($"{path}: Humanoid={avatar.isHuman} AvatarValid={avatar.isValid} clip={motion.name} duration={motion.length:F4}s loop={motion.isLooping} root-baked=true");
        }
        AssetDatabase.SaveAssets();
        File.WriteAllLines("Verification/AnimationIntegration/import-settings.txt",lines);
        Debug.Log(string.Join("\n",lines));
    }
    public static void ImportBatch()
    {
        try{Import();EditorApplication.Exit(0);}
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
#endif
