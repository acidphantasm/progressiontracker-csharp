using System.Reflection;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Utils;

namespace _progressionTracker.Patches;


public class AcceptQuestPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(QuestController).GetMethod(nameof(QuestController.AcceptQuest));
    }
    
    [PatchPostfix]
    public static void Postfix(PmcData pmcData, AcceptQuestRequestData acceptedQuest, MongoId sessionID)
    {
        var progressionTracker = ServiceLocator.ServiceProvider.GetService<ProgressionTracker>();
        
        var profileId = sessionID;
        var questId = acceptedQuest.QuestId;

        if (progressionTracker.RequiredCollectorQuests.ContainsKey(questId))
        {
            Console.WriteLine("Quest is valid, Updating");
            progressionTracker.UpdateQuestStatus(profileId, questId, true, false);
        }
    }
}