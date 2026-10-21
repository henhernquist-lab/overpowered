using System;

static class CombatGuardChecks
{
    static int checks;
    static void Check(bool ok,string label){Console.WriteLine((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label);checks++;}
    static bool Near(float a,float b)=>Math.Abs(a-b)<.0002f*Math.Max(1,Math.Abs(b));
    static float Dot(float angle)=>(float)Math.Cos(angle*Math.PI/180);
    static void Main()
    {
        Check(EnemyGuardMath.InArc(1,110),"front inside guard arc");
        Check(EnemyGuardMath.InArc(Dot(54.9f),110),"inside half-angle boundary");
        Check(!EnemyGuardMath.InArc(Dot(55.1f),110),"outside half-angle boundary");
        Check(!EnemyGuardMath.InArc(0,110),"flank and overhead outside arc");
        Check(!EnemyGuardMath.InArc(-1,110),"rear outside arc");
        Check(!EnemyGuardMath.InArc(Dot(81),360),"maximum authored arc cannot cover flanks/rear");
        foreach(float incoming in new[]{.1f,1,10,100,10000})
        {
            float integrity=100000;float value=EnemyGuardMath.Block(incoming,.65f,ref integrity,out bool broken);
            Check(Near(value,incoming*.35f)&&!broken,"frontal reduction remains positive: "+incoming);
            Check(Near(integrity,100000-incoming*.65f),"absorbed damage spends integrity once: "+incoming);
        }
        float capacity=75;
        for(int i=0;i<12;i++)
        {
            float value=EnemyGuardMath.Block(10,.65f,ref capacity,out bool broken);
            Check(i<11?!broken&&Near(value,3.5f):broken&&Near(value,10),"repeated attacks deplete finite guard: hit "+(i+1));
        }
        capacity=75;float large=EnemyGuardMath.Block(200,.65f,ref capacity,out bool depleted);
        Check(depleted&&Near(large,200),"guard-depleting blow deals full damage");
        capacity=100;float clamped=EnemyGuardMath.Block(10,1,ref capacity,out _);
        Check(Near(clamped,1),"configuration cannot create absolute immunity");
        float before=capacity;EnemyGuardMath.Block(0,.65f,ref capacity,out bool zero);
        Check(capacity==before&&!zero,"zero hit never spends integrity");
        Console.WriteLine("RESULT "+checks+" PASS, 0 FAIL (pure guard policy only)");
    }
}
