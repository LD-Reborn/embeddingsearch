using Microsoft.Extensions.Diagnostics.HealthChecks;
using Indexer.Models;
using Indexer.Exceptions;
using Quartz;
using Quartz.Impl;
public class RunOnceCall : ICall
{
    public ILogger _logger;
    public bool IsEnabled { get; set; }
    public bool IsExecuting { get; set; }
    public Worker Worker { get; }
    public CallConfig CallConfig { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    public RunOnceCall(Worker worker, ILogger logger, CallConfig callConfig)
    {
        Worker = worker;
        _logger = logger;
        CallConfig = callConfig;
        IsEnabled = true;
        IsExecuting = false;
        IndexAsync();
    }

    public void Start()
    {
        IndexAsync();
        IsEnabled = true;
    }

    public void Stop()
    {
        IsEnabled = false;
    }

    private async void IndexAsync()
    {
        try
        {
            DateTime beforeExecution = DateTime.Now;
            IsExecuting = true;
            try
            {
                await Task.Run(() => Worker.Scriptable.Update(new RunOnceCallbackInfos()));
            }
            finally
            {
                IsExecuting = false;
                LastExecution = beforeExecution;
                Worker.LastExecution = beforeExecution;
            }
            DateTime afterExecution = DateTime.Now;
            WorkerManager.UpdateCallAndWorkerTimestamps(this, Worker, beforeExecution, afterExecution);
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception occurred in a Call of Worker \"{name}\": \"{ex}\"", Worker.Name, ex.Message);
        }
    }

    public HealthCheckResult HealthCheck()
    {
        return HealthCheckResult.Healthy(); // TODO implement proper healthcheck
    }

}

public class IntervalCall : ICall
{
    public System.Timers.Timer Timer;
    public IScriptable Scriptable;
    public ILogger _logger;
    public bool IsEnabled { get; set; }
    public bool IsExecuting { get; set; }
    public CallConfig CallConfig { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    public IntervalCall(Worker worker, ILogger logger, CallConfig callConfig)
    {
        Scriptable = worker.Scriptable;
        _logger = logger;
        CallConfig = callConfig;
        IsEnabled = true;
        IsExecuting = false;
        if (callConfig.Interval is null)
        {
            _logger.LogError("Interval not set for a Call in Worker \"{Name}\"", worker.Name);
            throw new IndexerConfigurationException($"Interval not set for a Call in Worker \"{worker.Name}\"");
        }

        Timer = new System.Timers.Timer((double)callConfig.Interval)
        {
            AutoReset = true,
            Enabled = true
        };
        DateTime now = DateTime.Now;
        Timer.Elapsed += (sender, e) =>
        {
            try
            {
                DateTime beforeExecution = DateTime.Now;
                IsExecuting = true;
                try
                {
                    worker.Scriptable.Update(new IntervalCallbackInfos() { sender = sender, e = e });
                }
                finally
                {
                    IsExecuting = false;
                    LastExecution = beforeExecution;
                    worker.LastExecution = beforeExecution;
                }
                DateTime afterExecution = DateTime.Now;
                WorkerManager.UpdateCallAndWorkerTimestamps(this, worker, beforeExecution, afterExecution);
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception occurred in a Call of Worker \"{name}\": \"{ex}\"", worker.Name, ex.Message);
            }
        };
    }

    public void Start()
    {
        Timer.Start();
        IsEnabled = true;
    }

    public void Stop()
    {
        Scriptable.Stop();
        Timer.Stop();
        IsEnabled = false;
    }

    public HealthCheckResult HealthCheck()
    {
        if (!Scriptable.UpdateInfo.Successful)
        {
            _logger.LogWarning("HealthCheck revealed: The last execution of \"{name}\" was not successful", Scriptable.ToolSet.FilePath);
            return HealthCheckResult.Unhealthy($"HealthCheck revealed: The last execution of \"{Scriptable.ToolSet.FilePath}\" was not successful");
        }
        double timerInterval = Timer.Interval; // In ms
        DateTime lastRunDateTime = Scriptable.UpdateInfo.DateTime;
        DateTime now = DateTime.Now;
        double millisecondsSinceLastExecution = now.Subtract(lastRunDateTime).TotalMilliseconds;
        if (millisecondsSinceLastExecution >= 2 * timerInterval)
        {
            _logger.LogWarning("HealthCheck revealed: Since the last execution of \"{name}\" more than twice the interval has passed", Scriptable.ToolSet.FilePath);
            return HealthCheckResult.Unhealthy($"HealthCheck revealed: Since the last execution of \"{Scriptable.ToolSet.FilePath}\" more than twice the interval has passed");
        }
        return HealthCheckResult.Healthy();
    }

}

public class ScheduleCall : ICall
{
    public bool IsEnabled { get; set; }
    public bool IsExecuting { get; set; }
    public Worker Worker { get; }
    public JobKey JobKey { get; }
    public JobDataMap JobDataMap { get; }
    public CallConfig CallConfig { get; set; }
    private ILogger _logger { get; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }
    private StdSchedulerFactory SchedulerFactory { get; }
    private IScheduler Scheduler { get; }

    public ScheduleCall(Worker worker, CallConfig callConfig, ILogger logger)
    {
        Worker = worker;
        CallConfig = callConfig;
        _logger = logger;
        IsEnabled = false;
        IsExecuting = false;
        JobKey = new(worker.Name);
        SchedulerFactory = new();
        Scheduler = SchedulerFactory.GetScheduler(CancellationToken.None).Result;
        JobDataMap = [];
        JobDataMap["action"] = () =>
        {
            try
            {
                DateTime beforeExecution = DateTime.Now;
                IsExecuting = true;
                try
                {
                    worker.Scriptable.Update(new ScheduleCallbackInfos());
                }
                finally
                {
                    IsExecuting = false;
                    LastExecution = beforeExecution;
                    worker.LastExecution = beforeExecution;
                }
                DateTime afterExecution = DateTime.Now;
                WorkerManager.UpdateCallAndWorkerTimestamps(this, worker, beforeExecution, afterExecution);
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception occurred in a Call of Worker \"{name}\": \"{ex}\"", worker.Name, ex.Message);
            }
        };
        CreateJob().Wait();
        Start();
    }

    public void Start()
    {
        if (!IsEnabled)
        {
            Scheduler.Start(CancellationToken.None).Wait();
            IsEnabled = true;
        }
    }

    public void Stop()
    {
        Scheduler.PauseAll();
        IsEnabled = false;
    }


    private async Task CreateJob()
    {
        if (CallConfig.Schedule is null)
        {
            throw new IndexerConfigurationException($"Interval not set for a Call in Worker \"{Worker.Name}\"");
        }
        try
        {

            await Scheduler.ScheduleJob(
                JobBuilder.Create<ActionJob>()
                .WithIdentity(JobKey)
                .Build(),
                TriggerBuilder.Create()
                .ForJob(JobKey)
                .WithIdentity(Worker.Name + "-trigger")
                .UsingJobData(JobDataMap)
                .WithCronSchedule(CallConfig.Schedule)
                .Build(),
                CancellationToken.None);
        }
        catch (FormatException)
        {
            throw new IndexerConfigurationException($"Quartz Cron expression invalid in Worker \"{Worker.Name}\" - Quartz syntax differs from classic cron");
        }
    }

    public HealthCheckResult HealthCheck()
    {
        return HealthCheckResult.Unhealthy(); // Not implemented yet
    }
}

public class FileUpdateCall : ICall
{
    public bool IsEnabled { get; set; }
    public bool IsExecuting { get; set; }
    public Worker Worker { get; }
    public CallConfig CallConfig { get; set; }
    private ILogger _logger { get; }
    private FileSystemWatcher _watcher { get; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastSuccessfulExecution { get; set; }

    public FileUpdateCall(Worker worker, CallConfig callConfig, ILogger logger)
    {
        Worker = worker;
        CallConfig = callConfig;
        _logger = logger;
        IsEnabled = true;
        IsExecuting = false;
        if (CallConfig.Path is null)
        {
            throw new IndexerConfigurationException($"Path not set for a Call in Worker \"{Worker.Name}\"");
        }

        List<string> events = callConfig.Events ?? [];
        bool allEvents = events.Count == 0;
        List<string> filters = callConfig.Filters ?? [];
        bool includeSubdirectories = callConfig.IncludeSubdirectories ?? false;

        _watcher = new FileSystemWatcher(CallConfig.Path);
        if (allEvents || events.Contains("Created")) _watcher.Created += OnFileChanged;
        if (allEvents || events.Contains("Changed")) _watcher.Changed += OnFileChanged;
        if (allEvents || events.Contains("Deleted")) _watcher.Deleted += OnFileChanged;
        if (allEvents || events.Contains("Renamed")) _watcher.Renamed += OnFileChanged;
        foreach (string filter in filters)
        {
            _watcher.Filters.Add(filter);
        }
        _watcher.IncludeSubdirectories = includeSubdirectories;
        _watcher.EnableRaisingEvents = true;
    }

    public void Start()
    {
        if (!IsEnabled)
        {
            IsEnabled = true;
            _watcher.EnableRaisingEvents = true;
            Index();
        }
    }

    public void Stop()
    {
        if (IsEnabled)
        {
            IsEnabled = false;
            _watcher.EnableRaisingEvents = false;
        }
    }


    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }
        Index(sender, e);
    }

    private void Index(object? sender, FileSystemEventArgs? e)
    {
        try
        {
            DateTime beforeExecution = DateTime.Now;
            IsExecuting = true;
            try
            {
                Worker.Scriptable.Update(new FileUpdateCallbackInfos() {sender = sender, e = e});
            }
            finally
            {
                IsExecuting = false;
                LastExecution = beforeExecution;
                Worker.LastExecution = beforeExecution;
            }
            DateTime afterExecution = DateTime.Now;
            WorkerManager.UpdateCallAndWorkerTimestamps(this, Worker, beforeExecution, afterExecution);
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception occurred in a Call of Worker \"{name}\": \"{ex}\"", Worker.Name, ex.Message);
        }
    }

    private void Index()
    {
        Index(null, null);
    }

    public HealthCheckResult HealthCheck()
    {
        return HealthCheckResult.Unhealthy(); // Not implemented yet
    }
}