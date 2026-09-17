using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public sealed class BuildingPlacement { public Vector3 Position, Size; public bool RooftopReward; }
[CreateAssetMenu(menuName = "Overpowered/City Layout")]
public sealed class CityLayout : ScriptableObject
{
    public bool UseAuthoredBuildings;
    public List<BuildingPlacement> Buildings = new List<BuildingPlacement>();
    public List<BuildingPlacement> Generate(CitySettings config)
    {
        if (UseAuthoredBuildings) return new List<BuildingPlacement>(Buildings);
        var random = new System.Random(config.Seed);
        var output = new List<BuildingPlacement>();
        float pitch = config.BlockSize + config.StreetWidth;
        for (int x = 0; x < config.Blocks; x++) for (int z = 0; z < config.Blocks; z++)
        for (int corner = 0; corner < 4; corner++)
        {
            float height = Mathf.Lerp(config.BuildingHeight.x, config.BuildingHeight.y, (float)random.NextDouble());
            if (x == z && corner == 0) height = config.LandmarkHeight;
            output.Add(new BuildingPlacement
            {
                Position = new Vector3((x - (config.Blocks - 1) * .5f) * pitch + (corner % 2 == 0 ? -1 : 1) * config.BlockSize * .25f,
                    height * .5f + config.SidewalkHeight,
                    (z - (config.Blocks - 1) * .5f) * pitch + (corner < 2 ? -1 : 1) * config.BlockSize * .25f),
                Size = new Vector3(config.BuildingWidth, height, config.BuildingDepth),
                RooftopReward = output.Count < config.RooftopPickups
            });
        }
        return output;
    }
}
