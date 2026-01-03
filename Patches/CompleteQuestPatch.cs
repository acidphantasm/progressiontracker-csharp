using System.Reflection;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Quests;

namespace _progressionTracker.Patches;


public class CompleteQuestPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(QuestController).GetMethod(nameof(QuestController.CompleteQuest));
    }
    
    [PatchPostfix]
    public static void Postfix(PmcData pmcData, CompleteQuestRequestData request, MongoId sessionId)
    {
        var progressionTracker = ServiceLocator.ServiceProvider.GetService<ProgressionTracker>();
        
        var profileId = sessionId;
        var questId = request.QuestId;
        
        if (progressionTracker.RequiredCollectorQuests.ContainsKey(questId))
        {
            Console.WriteLine("Quest is valid, Updating");
            progressionTracker.UpdateQuestStatus(profileId, questId, false, true);
        }
    }
}