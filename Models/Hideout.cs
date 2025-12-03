using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums.Hideout;

namespace _progressionTracker.Models;

public class ProfileHideoutProgress
{
    public Dictionary<HideoutAreas, List<HideoutItemRequirement>> ItemsNeededByArea { get; set; } = new();
    public Dictionary<HideoutAreas, Dictionary<string, int?>> TraderLoyaltyNeededByArea { get; set; } = new();
    public Dictionary<HideoutAreas, Dictionary<HideoutAreas, (int currentLevel, int requiredLevel)>> AreasNeededByArea { get; set; } = new();
    public Dictionary<HideoutAreas, AreaConstructionProgress> AreasInConstruction { get; set; } = new();
}

public class HideoutItemRequirement
{
    public string TemplateId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public bool RequiresFoundInRaid { get; set; }
    public int? CountNeeded { get; set; }
    public int CountOwned { get; set; }
    public bool Complete => CountOwned >= CountNeeded;
}

public class AreaConstructionProgress
{
    public bool Completed { get; set; }
    public DateTime CompleteTime { get; set; }
    public double? TotalConstructionTime { get; set; }
}