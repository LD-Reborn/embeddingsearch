using Quartz;
public class ActionJob : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        var action = (Action)context.MergedJobDataMap["action"];
        action?.Invoke();
        return Task.CompletedTask;
    }
}