using System.Reflection;
using _progressionTracker.Globals;
using _progressionTracker.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Json;
using SPTarkov.Server.Web;

namespace _progressionTracker;

public record ModMetadata : AbstractModMetadata, IModWebMetadata
{
    public override string ModGuid { get; init; } = "com.acidphantasm.progressiontracker";
    public override string Name { get; init; } = "Progression Tracker";
    public override string Author { get; init; } = "acidphantasm";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.3");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string? License { get; init; } = "MIT";
}

[Injectable(InjectionType = InjectionType.Singleton, TypePriority = OnLoadOrder.PostSptModLoader)]
public class ProgressionTracker(
    ISptLogger<ProgressionTracker> logger, 
    DatabaseService databaseService,
    ProfileHelper profileHelper,
    ItemHelper itemHelper,
    ModHelper modHelper,
    LocaleService localeService)
    : IOnLoad, IOnUpdate
{
    public string ModPath = string.Empty;
    
    public event Action? OnProgressionUpdated;
    public Dictionary<string, string> ProfilesIdToName = new();

    private const string CollectorId = "5c51aac186f77432ea65c552";

    private readonly Dictionary<string, Dictionary<string, (int total, int fir)>> _profileItemCollection = new();
    public readonly Dictionary<string, ProfileHideoutProgress> ProfileHideoutProgressData = new();
    
    public readonly Dictionary<string, string> RequiredCollectorQuests = new();
    public readonly Dictionary<string, string> RequiredCollectorItems = new();
    public readonly Dictionary<string, List<QuestDisplayInfo>> CollectorInProgressDisplay = new();
    
    private readonly Dictionary<string, string> _cachedHideoutItemNameMap = new();

    public readonly Dictionary<string, Dictionary<string, bool>> ProfileCollectorQuestsInProgressStatus = new();
    public readonly Dictionary<string, Dictionary<string, bool>> ProfileCollectorQuestsCompletedStatus = new();
    
    public readonly Dictionary<string, Dictionary<string, bool>> ProfileCollectorItemsCollected = new();
    
    public readonly Dictionary<string, Dictionary<string, int>> ProfileTraderLoyaltyStatus = new();

    private const long FirstServerStartupValue = 1767419877;
    public DateTime LastUpdateCheck = DateTime.Now;
    public DateTime LastUpdateCheckCollectorQuests = DateTime.Now;
    
    public Task OnLoad()
    {
        ModPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        
        GetCollectorRequirements();
        GetProfileStatusInformation();
        CacheHideoutItemRequirementNames();
        return Task.CompletedTask;
    }
    
    public Task<bool> OnUpdate(long timeSinceLastRun)
    {
        if (timeSinceLastRun < ModConfig.Config.ConfigAppSettings.UpdateTimer) return Task.FromResult(false);
        LastUpdateCheck = DateTime.Now;
        GetProfileStatusInformation();
        OnProgressionUpdated?.Invoke();

        if (ModConfig.Config.ConfigAppSettings.LogUpdatesInConsole)
        {
            var lastRunText = timeSinceLastRun == FirstServerStartupValue
                ? "Now"
                : LastUpdateCheck.ToString("g");
            logger.Success($"[ProgressionTracker] Updating Profile Data... Last Update Time : {lastRunText}");
        }

        return Task.FromResult(true);
    }

    
    private void CacheHideoutItemRequirementNames()
    {
        var hideoutAreas = databaseService.GetHideout().Areas;

        var itemTemplateIds =
            hideoutAreas
                .SelectMany(area => area.Stages.Values)
                .Where(stage => stage.Requirements != null)
                .SelectMany(stage => stage.Requirements)
                .Where(req => req.Type == "Item")
                .Select(req => req.TemplateId)
                .Distinct();

        foreach (var tpl in itemTemplateIds)
        {
            if (!_cachedHideoutItemNameMap.ContainsKey(tpl))
            {
                _cachedHideoutItemNameMap[tpl] = itemHelper.GetItemName(tpl);
            }
        }
    }

    private void GetCollectorRequirements()
    {
        var allQuests = databaseService.GetQuests();
        var collector = allQuests[CollectorId];

        if (collector.Conditions.AvailableForFinish is not null)
        {
            foreach (var requiredCondition in collector.Conditions.AvailableForFinish)
            {
                if (requiredCondition.ConditionType != "HandoverItem") continue;
                var itemId = FirstOrDefault(requiredCondition.Target);
                if (itemId is null) continue;
                var itemName = itemHelper.GetItemName(itemId);

                /* Only use this when you need to get new images
                 * GetImageUrlAsync(itemId);
                 */
                RequiredCollectorItems[itemId] = itemName;
            }
        }

        if (collector.Conditions.AvailableForStart is not null)
        {
            foreach (var requiredQuest in collector.Conditions.AvailableForStart)
            {
                if (requiredQuest.ConditionType != "Quest") continue;
                var questId = FirstOrDefault(requiredQuest.Target);
                if (questId is null) continue;
                if (!allQuests.TryGetValue(questId, out var quest)) continue;
                    
                var questName = quest.QuestName;

                RequiredCollectorQuests[questId] = questName;
            }
        }
    }
    
    private List<QuestDisplayInfo> GetActiveQuestDisplayData(string profileId)
    {
        var result = new List<QuestDisplayInfo>();

        if (!ProfileCollectorQuestsInProgressStatus.TryGetValue(profileId, out var questsDictionary))
            return result;

        var questDb = databaseService.GetQuests();
        var localeDb = localeService.GetLocaleDb();

        foreach (var (questId, inProgress) in questsDictionary)
        {
            if (!inProgress || !questDb.TryGetValue(questId, out var questData))
                continue;

            var questInfo = new QuestDisplayInfo
            {
                QuestName = questData.QuestName ?? "Unknown Quest"
            };

            foreach (var condition in questData.Conditions.AvailableForFinish ?? [])
            {
                localeDb.TryGetValue(condition.Id, out var localeText);
                localeText ??= "(Missing Condition Text)";

                var questConditionDisplay = new QuestConditionDisplay()
                {
                    Text = localeText,
                    Completed = false
                };

                var taskConditionCounters = profileHelper.GetProfileByPmcId(profileId)?.TaskConditionCounters;
                if (taskConditionCounters is not null)
                {
                    if (taskConditionCounters.TryGetValue(condition.Id, out var taskConditions) && taskConditions.Value >= condition.Value)
                    {
                        questConditionDisplay.Completed = true;
                    }
                }

                questInfo.Conditions.Add(questConditionDisplay);
            }

            result.Add(questInfo);
        }

        return result;
    }
    
    private string? FirstOrDefault(ListOrT<string>? input)
    {
        if (input?.IsList == true && input.List?.Count > 0)
            return input.List[0];

        if (input?.IsItem == true)
            return input.Item;

        return null;
    }

    private void GetProfileStatusInformation()
    {
        var profiles = profileHelper.GetProfiles();

        foreach (var kvp in profiles)
        {
            var profileId = kvp.Key;
            var profile = kvp.Value;
            var profileName = profile.CharacterData?.PmcData?.Info?.Nickname;
            if (profileName is null || profileName.Contains("headless_")) continue;
            
            ProfilesIdToName[profileId] = profileName;
            
            // profile quest handling data
            var profileQuestData = profile.CharacterData?.PmcData?.Quests;
            if (profileQuestData is null) continue;
            if (!ProfileCollectorQuestsInProgressStatus.ContainsKey(profileId)) ProfileCollectorQuestsInProgressStatus[profileId] = new Dictionary<string, bool>();
            if (!ProfileCollectorQuestsCompletedStatus.ContainsKey(profileId)) ProfileCollectorQuestsCompletedStatus[profileId] = new Dictionary<string, bool>();
            
            foreach (var quest in profileQuestData)
            {
                if (RequiredCollectorQuests.ContainsKey(quest.QId))
                {
                    if (quest.Status == QuestStatusEnum.Started || quest.Status == QuestStatusEnum.AvailableForFinish)
                    {
                        ProfileCollectorQuestsInProgressStatus[profileId][quest.QId] = true;
                        ProfileCollectorQuestsCompletedStatus[profileId][quest.QId] = false;
                        continue;
                    }

                    if (quest.Status == QuestStatusEnum.Success)
                    {
                        ProfileCollectorQuestsInProgressStatus[profileId][quest.QId] = false;
                        ProfileCollectorQuestsCompletedStatus[profileId][quest.QId] = true;
                        continue;
                    }
                    
                    ProfileCollectorQuestsInProgressStatus[profileId][quest.QId] = false;
                    ProfileCollectorQuestsCompletedStatus[profileId][quest.QId] = false;
                }
            }
            
            CollectorInProgressDisplay[profileId] = GetActiveQuestDisplayData(profileId);
            
            // hideout
            UpdateRequiredHideoutItemsForProfile(profileId);

            // profile item handling data
            if (!ProfileCollectorItemsCollected.ContainsKey(profileId)) ProfileCollectorItemsCollected[profileId] = new Dictionary<string, bool>();
            if (!_profileItemCollection.ContainsKey(profileId)) _profileItemCollection[profileId] = new Dictionary<string, (int total, int fir)>();
            UpdateInventoryData(profileId);
            
            // traders
            if (!ProfileTraderLoyaltyStatus.ContainsKey(profileId)) ProfileTraderLoyaltyStatus[profileId] = new Dictionary<string, int>();
            var traders = profile.CharacterData?.PmcData?.TradersInfo;
            if (traders is null) continue;
            foreach (var traderKvp in traders)
            {
                var traderId = traderKvp.Key;
                var loyaltyLevel = traderKvp.Value.LoyaltyLevel ?? 0;
                ProfileTraderLoyaltyStatus[profileId][traderId] = loyaltyLevel;
            }
        }
    }

    private void UpdateRequiredHideoutItemsForProfile(string profileId)
    {
        if (!ProfileHideoutProgressData.ContainsKey(profileId))
            ProfileHideoutProgressData[profileId] = new ProfileHideoutProgress();

        var progressData = ProfileHideoutProgressData[profileId];
        progressData.ItemsNeededByArea.Clear();
        progressData.TraderLoyaltyNeededByArea.Clear();
        progressData.AreasNeededByArea.Clear();

        var hideout = profileHelper.GetFullProfile(profileId).CharacterData?.PmcData?.Hideout;
        var hideoutAreas = hideout?.Areas;
        if (hideoutAreas is null) return;

        var profileAreaLevels = hideoutAreas.ToDictionary(x => x.Type, x => x.Level);

        foreach (var area in hideoutAreas)
        {
            var databaseAreaData = databaseService.GetHideout().Areas.FirstOrDefault(x => x.Type == area.Type);
            if (databaseAreaData is null) continue;

            var nextStageKey = (area.Level + 1).ToString();
            if (databaseAreaData.Stages is null || nextStageKey is null) continue;
            if (!databaseAreaData.Stages.TryGetValue(nextStageKey, out var nextStage)) continue;
            
            if (!progressData.ItemsNeededByArea.ContainsKey(area.Type))
                progressData.ItemsNeededByArea[area.Type] = new List<HideoutItemRequirement>();

            foreach (var stageRequirement in nextStage.Requirements ?? [])
            {
                switch (stageRequirement.Type)
                {
                    case "Item":
                        var existingItem = progressData.ItemsNeededByArea[area.Type]
                            .FirstOrDefault(x => x.TemplateId == stageRequirement.TemplateId);

                        if (existingItem == null)
                        {
                            if (!_cachedHideoutItemNameMap.TryGetValue(stageRequirement.TemplateId, out var itemName))
                                itemName = itemHelper.GetItemName(stageRequirement.TemplateId);
                            
                            progressData.ItemsNeededByArea[area.Type].Add(new HideoutItemRequirement
                            {
                                TemplateId = stageRequirement.TemplateId,
                                ItemName = itemName,
                                RequiresFoundInRaid = stageRequirement.IsSpawnedInSession == true,
                                CountNeeded = stageRequirement.Count,
                                CountOwned = 0,
                            });
                        }
                        else
                        {
                            existingItem.CountNeeded += stageRequirement.Count;
                        }
                        break;

                    case "TraderLoyalty":
                        if (!progressData.TraderLoyaltyNeededByArea.TryGetValue(area.Type, out var areaTraders))
                        {
                            areaTraders = new Dictionary<string, int?>();
                            progressData.TraderLoyaltyNeededByArea[area.Type] = areaTraders;
                        }
                        areaTraders[stageRequirement.TraderId] = stageRequirement.LoyaltyLevel;
                        break;

                    case "Area":
                        if (!Enum.IsDefined(typeof(HideoutAreas), stageRequirement.AreaType)) break;
                        
                        var requiredArea = (HideoutAreas)stageRequirement.AreaType;
                        if (requiredArea == area.Type) break;
                        profileAreaLevels.TryGetValue(requiredArea, out var level);
                        
                        var currentLevel = level ?? 0;
                        var requiredLevel = stageRequirement.RequiredLevel ?? 0;

                        if (!progressData.AreasNeededByArea.TryGetValue(area.Type, out var neededAreas))
                        {
                            neededAreas = new Dictionary<HideoutAreas, (int currentLevel, int requiredLevel)>();
                            progressData.AreasNeededByArea[area.Type] = neededAreas;
                        }
                        neededAreas[requiredArea] = (currentLevel, requiredLevel);

                        break;
                }
            }
            
            if (area.Constructing == true)
            {
                DateTime completeTime = area.CompleteTime.HasValue 
                    ? DateTimeOffset.FromUnixTimeSeconds(area.CompleteTime.Value).UtcDateTime 
                    : DateTime.UtcNow;

                var isComplete = area.CompleteTime.HasValue && completeTime <= DateTime.UtcNow;

                progressData.AreasInConstruction[area.Type] = new AreaConstructionProgress
                {
                    Completed = isComplete,
                    CompleteTime = completeTime,
                    TotalConstructionTime = nextStage.ConstructionTime,
                };
            }
        }
    }

    private void UpdateInventoryData(string profileId)
    {
        var profileInventoryData = profileHelper.GetFullProfile(profileId).CharacterData?.PmcData?.Inventory?.Items;
        if (profileInventoryData is null) return;
        
        _profileItemCollection[profileId].Clear();
        foreach (var item in profileInventoryData)
        {
            var tpl = item.Template;
            if (!_profileItemCollection[profileId].ContainsKey(tpl)) _profileItemCollection[profileId][tpl] = (0, 0);

            int countToAdd;

            if (itemHelper.IsOfBaseclass(tpl, BaseClasses.MONEY)) countToAdd = (int)(item.Upd?.StackObjectsCount ?? 0);
            else countToAdd = 1;

            var isFoundInRaid = item.Upd?.SpawnedInSession == true;

            var current = _profileItemCollection[profileId][tpl];
            current.total += countToAdd;

            if (isFoundInRaid) current.fir += countToAdd;

            _profileItemCollection[profileId][tpl] = current;

            if (RequiredCollectorItems.ContainsKey(tpl) && isFoundInRaid)
            {
                ProfileCollectorItemsCollected[profileId][tpl] = true;
            }
        }
        
        foreach (var requiredItem in RequiredCollectorItems.Keys)
        {
            if (!ProfileCollectorItemsCollected[profileId].ContainsKey(requiredItem))
            {
                ProfileCollectorItemsCollected[profileId][requiredItem] = false;
            }
        }
        
        UpdateHideoutItemCounts(profileId);
    }
    
    private void UpdateHideoutItemCounts(string profileId)
    {
        if (!ProfileHideoutProgressData.TryGetValue(profileId, out var progress))
            return;

        if (!_profileItemCollection.TryGetValue(profileId, out var ownedItems))
            return;

        foreach (var areaItems in progress.ItemsNeededByArea.Values)
        {
            foreach (var requirement in areaItems)
            {
                if (ownedItems.TryGetValue(requirement.TemplateId, out var counts))
                {
                    requirement.CountOwned = requirement.RequiresFoundInRaid
                        ? counts.fir
                        : counts.total;
                }
                else
                {
                    requirement.CountOwned = 0;
                }
            }
        }
    }
    
    public void UpdateQuestStatus(string profileId, string questId, bool inProgress, bool completed)
    {
        if (!ProfileCollectorQuestsInProgressStatus.ContainsKey(profileId)) ProfileCollectorQuestsInProgressStatus[profileId] = new Dictionary<string, bool>();
        if (!ProfileCollectorQuestsCompletedStatus.ContainsKey(profileId)) ProfileCollectorQuestsCompletedStatus[profileId] = new Dictionary<string, bool>();
        
        ProfileCollectorQuestsInProgressStatus[profileId][questId] = inProgress;
        ProfileCollectorQuestsCompletedStatus[profileId][questId] = completed;
        
        LastUpdateCheckCollectorQuests = DateTime.Now;

        OnProgressionUpdated?.Invoke();
    }
}
