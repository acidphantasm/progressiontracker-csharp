namespace _progressionTracker.Models;

public class QuestConditionDisplay
{
    public string Text { get; set; } = "";
    public bool Completed { get; set; }
}

public class QuestDisplayInfo
{
    public string QuestName { get; set; } = "";
    public List<QuestConditionDisplay> Conditions { get; set; } = new();
}