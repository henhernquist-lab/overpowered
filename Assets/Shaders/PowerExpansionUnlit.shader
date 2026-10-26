Shader "Overpowered/Expansion Unlit"
{
 Properties { _Color("Palette tint",Color)=(.3,.8,1,.6) _Hologram("Hologram shimmer",Range(0,1))=0 }
 SubShader
 {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" }
  Blend SrcAlpha OneMinusSrcAlpha
  ZWrite Off Cull Off
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct appdata { float4 vertex:POSITION; };
   struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; };
   fixed4 _Color;float _Hologram;
   v2f vert(appdata v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;return o;}
   fixed4 frag(v2f i):SV_Target{fixed4 c=_Color;c.a*=lerp(1,.65+.35*sin(i.world.y*45+_Time.y*14),_Hologram);return c;}
   ENDCG
  }
 }
}
