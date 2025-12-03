using _progressionTracker.Patches;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace _progressionTracker;

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader)]
public class PatchManager : IOnLoad
{
    public Task OnLoad()
    {
        new AcceptQuestPatch().Enable();
        new CompleteQuestPatch().Enable();

        return Task.CompletedTask;
    }
}