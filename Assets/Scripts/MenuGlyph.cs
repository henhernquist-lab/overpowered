using UnityEngine;
using UnityEngine.UIElements;

public enum MenuGlyph { Shield, Flame, Wing, Fist, Orbit, Crystal, Star, Chevron }

/// Original polygons drawn in code: no traced, downloaded or licensed logo artwork.
public sealed class MenuIcon : VisualElement
{
    public MenuGlyph Glyph;public Color Ink;
    public MenuIcon(MenuGlyph glyph,Color ink){Glyph=glyph;Ink=ink;pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;}
    void Draw(MeshGenerationContext context)
    {
        var p=context.painter2D;float size=Mathf.Min(contentRect.width,contentRect.height);Vector2 origin=new Vector2((contentRect.width-size)*.5f,(contentRect.height-size)*.5f);
        Vector2 V(float x,float y)=>origin+new Vector2(x,y)*size;
        void Shape(bool fill,params Vector2[] points){p.BeginPath();p.MoveTo(points[0]);for(int i=1;i<points.Length;i++)p.LineTo(points[i]);p.ClosePath();if(fill)p.Fill();else p.Stroke();}
        void Line(float x,float y,float a,float b){p.BeginPath();p.MoveTo(V(x,y));p.LineTo(V(a,b));p.Stroke();}
        p.strokeColor=Ink;p.fillColor=Ink;p.lineWidth=size*.035f;
        switch(Glyph)
        {
            case MenuGlyph.Shield:
                Shape(false,V(.18f,.14f),V(.82f,.14f),V(.82f,.53f),V(.68f,.73f),V(.5f,.86f),V(.32f,.73f),V(.18f,.53f));
                Shape(true,V(.32f,.56f),V(.32f,.36f),V(.43f,.36f),V(.43f,.56f));Shape(true,V(.47f,.56f),V(.47f,.25f),V(.58f,.25f),V(.58f,.56f));Shape(true,V(.62f,.56f),V(.62f,.42f),V(.7f,.42f),V(.7f,.56f));Line(.3f,.63f,.7f,.63f);break;
            case MenuGlyph.Flame:
                Shape(false,V(.52f,.08f),V(.62f,.36f),V(.78f,.26f),V(.82f,.55f),V(.73f,.76f),V(.53f,.88f),V(.3f,.8f),V(.17f,.6f),V(.29f,.29f),V(.35f,.51f));
                Shape(true,V(.49f,.43f),V(.64f,.62f),V(.56f,.76f),V(.39f,.7f),V(.37f,.58f));break;
            case MenuGlyph.Wing:Shape(false,V(.12f,.68f),V(.4f,.3f),V(.87f,.17f),V(.6f,.47f),V(.78f,.42f),V(.52f,.67f),V(.29f,.78f));break;
            case MenuGlyph.Fist:Shape(false,V(.22f,.65f),V(.22f,.36f),V(.38f,.34f),V(.41f,.23f),V(.57f,.23f),V(.6f,.32f),V(.78f,.32f),V(.82f,.61f),V(.64f,.82f),V(.4f,.82f));Line(.36f,.45f,.69f,.45f);Line(.43f,.3f,.43f,.45f);Line(.59f,.3f,.59f,.45f);break;
            case MenuGlyph.Orbit:Shape(false,V(.14f,.5f),V(.32f,.28f),V(.66f,.2f),V(.87f,.4f),V(.73f,.7f),V(.34f,.81f));Shape(true,V(.5f,.34f),V(.66f,.5f),V(.5f,.66f),V(.34f,.5f));break;
            case MenuGlyph.Crystal:Shape(false,V(.5f,.12f),V(.79f,.36f),V(.7f,.78f),V(.31f,.78f),V(.2f,.36f));Line(.5f,.12f,.5f,.87f);Line(.2f,.36f,.79f,.36f);Line(.2f,.36f,.5f,.87f);Line(.79f,.36f,.5f,.87f);break;
            case MenuGlyph.Chevron:Line(.3f,.2f,.68f,.5f);Line(.68f,.5f,.3f,.8f);break;
            default:Shape(false,V(.5f,.13f),V(.61f,.37f),V(.88f,.4f),V(.68f,.58f),V(.73f,.86f),V(.5f,.73f),V(.27f,.86f),V(.32f,.58f),V(.12f,.4f),V(.39f,.37f));break;
        }
    }
}

public sealed class MenuMotes : VisualElement
{
    readonly CityPalette palette;readonly MenuPresentationTuning tuning;
    public float Clock;
    public MenuMotes(CityPalette p,MenuPresentationTuning t){palette=p;tuning=t;pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;}
    void Draw(MeshGenerationContext context)
    {
        float w=contentRect.width,h=contentRect.height;if(w<=0||h<=0)return;
        var p=context.painter2D;
        for(int i=0;i<tuning.AmbientCount;i++)
        {
            bool hero=i%2==0;float seed=i*1.6180339f;
            float x=(Mathf.Repeat(seed,1)*.42f+(hero?.04f:.54f))*w+Mathf.Sin(Clock*.3f+seed)*tuning.AmbientSway;
            float y=Mathf.Repeat(seed*.37f*h-Clock*tuning.AmbientDrift*(hero?.6f:1),h);
            Color ink=palette.Colors[(int)(hero?CityColor.HeroAccent:CityColor.VillainAccent)];ink.a=.2f+.22f*Mathf.Sin(seed+Clock*.4f)*Mathf.Sin(seed+Clock*.4f);
            p.fillColor=ink;float s=tuning.AmbientSize*(.7f+Mathf.Repeat(seed*.71f,1));
            p.BeginPath();p.MoveTo(new Vector2(x,y-s));p.LineTo(new Vector2(x+s,y));p.LineTo(new Vector2(x,y+s*(hero?1:2)));p.LineTo(new Vector2(x-s,y));p.ClosePath();p.Fill();
        }
    }
}
