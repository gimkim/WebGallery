using WebGallery.Services;
static TaskCompletionSource Signal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));var ct=timeout.Token;
var settings=new ThumbnailQueueSettings(1);settings.SetBackgroundWorkers(1);
using var queue=new ThumbnailWorkQueue(settings);await queue.StartAsync(ct);
var started=Signal();
var background=queue.EnqueueAsync(async token=>{started.SetResult();await Task.Delay(Timeout.Infinite,token);return 1;},ThumbnailPriority.Background,ct);
await started.Task.WaitAsync(ct);
var demand=queue.EnqueueAsync(_=>Task.FromResult(2),ThumbnailPriority.Visible,ct,medium:true);
Check(await demand==2,"On-demand medium preempts active background small");
try{await background;throw new Exception("Background should be preempted");}catch(BackgroundThumbnailPreemptedException){Console.WriteLine("PASS explicit preemption, not generation failure");}
var release=Signal();started=Signal();
var blocker=queue.EnqueueAsync(async token=>{started.SetResult();await release.Task.WaitAsync(token);return 0;},ThumbnailPriority.Visible,ct);
await started.Task.WaitAsync(ct);
var order=new List<string>();
var bg=queue.EnqueueAsync(_=>{order.Add("background");return Task.FromResult(0);},ThumbnailPriority.Background,ct);
var medium=queue.EnqueueAsync(_=>{order.Add("medium");return Task.FromResult(0);},ThumbnailPriority.Visible,ct,true);
var small=queue.EnqueueAsync(_=>{order.Add("small");return Task.FromResult(0);},ThumbnailPriority.Normal,ct);
release.SetResult();await Task.WhenAll(blocker,bg,medium,small);
Check(string.Join(',',order)=="small,medium,background","On-demand small then medium precedes background");
settings.Update(2);release=Signal();started=Signal();var mediumStarted=false;
small=queue.EnqueueAsync(async token=>{started.SetResult();await release.Task.WaitAsync(token);return 0;},ThumbnailPriority.Visible,ct);
await started.Task.WaitAsync(ct);
medium=queue.EnqueueAsync(_=>{mediumStarted=true;return Task.FromResult(0);},ThumbnailPriority.Visible,ct,true);
await Task.Delay(100,ct);Check(!mediumStarted,"Active on-demand small completes before medium starts even with a free slot");
release.SetResult();await Task.WhenAll(small,medium);
using(var pause=queue.SuspendBackground()) {
    var ran=false;bg=queue.EnqueueAsync(_=>{ran=true;return Task.FromResult(0);},ThumbnailPriority.Background,ct);
    await Task.Delay(100,ct);Check(!ran,"Cross-queue foreground reservation keeps background paused");
}
await bg.WaitAsync(ct);
Task<int>[] backlog;
using(var pause=queue.SuspendBackground()) {
    backlog=Enumerable.Range(0,ThumbnailWorkQueue.MaximumPendingJobs).Select(_=>queue.EnqueueAsync(_=>Task.FromResult(0),ThumbnailPriority.Background,ct)).ToArray();
    Check(await queue.EnqueueAsync(_=>Task.FromResult(7),ThumbnailPriority.Visible,ct)==7,"On-demand displaces background in a full bounded queue");
}
try{await Task.WhenAll(backlog);}catch(BackgroundThumbnailPreemptedException){}
Check(backlog.Count(t=>t.IsFaulted)==1,"Only one queued background item displaced for foreground admission");
await queue.StopAsync(ct);
foreach(var slots in new[]{1,16}) {
    var config=new ThumbnailQueueSettings(slots);config.SetBackgroundWorkers(slots);
    using var pipelineQueue=new ThumbnailWorkQueue(config);await pipelineQueue.StartAsync(ct);
    var completedWrites=Signal();var replenished=Signal();var starts=0;var live=0;
    var pipeline=Parallel.ForEachAsync(Enumerable.Range(0,slots*4),new ParallelOptions{MaxDegreeOfParallelism=slots*2,CancellationToken=ct},async (i,token)=>{
        await pipelineQueue.EnqueueAsync(async jobToken=>{
            if(Interlocked.Increment(ref live)>slots)throw new Exception("Generation exceeded configured slots");
            if(Interlocked.Increment(ref starts)>slots)replenished.TrySetResult();
            try{await Task.Delay(5,jobToken);return 0;}finally{Interlocked.Decrement(ref live);}
        },ThumbnailPriority.Background,token);
        if(i<slots)await completedWrites.Task.WaitAsync(token);
    });
    try { await replenished.Task.WaitAsync(ct);Check(true,$"{slots} generation slots refill while previous readiness writes remain blocked"); }
    finally{completedWrites.TrySetResult();}
    await pipeline;await pipelineQueue.StopAsync(ct);
}
