using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Overpowered/Parkour/Catalog")]
public sealed class ParkourCatalog : ScriptableObject
{
    public bool Enabled = true;
    public ParkourCourseDefinition[] Courses = new ParkourCourseDefinition[0];

    public static ParkourCatalog Current => Resources.Load<ParkourCatalog>("ParkourCatalog");

    public ParkourCourseDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id) || Courses == null) return null;
        for (int i = 0; i < Courses.Length; i++)
        {
            var c = Courses[i];
            if (c != null && c.Id == id) return c;
        }
        return null;
    }

    public List<ParkourCourseDefinition> ValidCourses()
    {
        var list = new List<ParkourCourseDefinition>();
        if (!Enabled || Courses == null) return list;
        var seen = new HashSet<string>();
        for (int i = 0; i < Courses.Length; i++)
        {
            var c = Courses[i];
            if (c == null || !c.IsValid(out _)) continue;
            if (!seen.Add(c.Id)) continue;
            list.Add(c);
        }
        return list;
    }
}
