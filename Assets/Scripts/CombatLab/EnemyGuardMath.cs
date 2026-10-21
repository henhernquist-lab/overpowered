using System;

/// Pure policy used by authoritative NPC defense and runnable without an Editor.
public static class EnemyGuardMath
{
    public static bool InArc(float facingDot,float totalArcDegrees) =>
        facingDot>=Math.Cos(Math.Max(20,Math.Min(160,totalArcDegrees))*.5*Math.PI/180);
    public static float Block(float damage,float reduction,ref float integrity,out bool broken)
    {
        if(damage<=0){broken=false;return damage;}
        float absorbed=damage*Math.Max(.05f,Math.Min(.9f,reduction));
        integrity-=absorbed;broken=integrity<=0;
        return broken?damage:damage-absorbed;
    }
}
