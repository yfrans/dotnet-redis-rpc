using dotnet_redis_rpc_test;
using RedisRPC_Test;
using StackExchange.Redis;
using System.Diagnostics;
using WebIOS.Packages;
using static RedisRPC_Test.TestModels;

var connection = ConnectionMultiplexer.Connect("localhost:6379");

var contentRpc = new RedisRpc("Content", connection).Start();
var simRpc = new RedisRpc("Simulation", connection).Start();

contentRpc.OnMessage += (sender, rm) => {
    //Console.WriteLine("Got message on content");
};
simRpc.OnMessage += async (sender, rm) => {
    //Console.WriteLine("Got message on simulation");
};

var testErrorCode = 0;
contentRpc.OnError += (sender, rm) => {
    if (testErrorCode > 0 && rm.Error != null && rm.Error.Code == testErrorCode) {
        Console.WriteLine($"OK! E: {rm.Error.Message}, C: {rm.Error.Code}");
        testErrorCode = 0;
    } else {
        Console.WriteLine($"Error on ContentRpc: {rm.Error?.Message} ({rm.Error?.Code})");
    }
};

simRpc.OnError += (sender, rm) => {
    Console.WriteLine($"Error on SimulationRpc: {rm.Error?.Message} ({rm.Error?.Code})");
};

var rnd = new Random();
Console.WriteLine("Started");
simRpc.RegisterController<TestController>();

var testTasks = () => {
    var tasks = new List<Task>();
    for (var i = 0; i < 10; i++) {
        var t = i;
        var r = rnd.Next(0, 100);
        tasks.Add(contentRpc.SendAsync("Simulation", "test/add", r).ContinueWith(v => {
            if (v.Result.DeserializePayload<int>() == r + 10) {
                Console.WriteLine($"{t} = OK");
            } else {
                Console.WriteLine($"{t} = ERROR");
            }
        }));
    }

    Task.WaitAll(tasks.ToArray());
};

var testTwoParam = async () => {
    var n1 = rnd.Next(0, 100);
    var n2 = rnd.Next(0, 100);
    Console.Write($"Test {n1} + {n2} = ");
    var resp = await contentRpc.SendAsync("Simulation", "test/addtwo", new { a = n1, b = n2 });
    var result = resp.DeserializePayload<int>();
    Console.WriteLine($"{result} ({result == n1 + n2})");
};

var testComplexModel = async () => {
    Console.Write($"Test complex model: ");
    var model = new MyModel {
        A = int.MaxValue,
        B = long.MaxValue,
        C = "test1",
        D = new Dictionary<string, List<string>> {
            { "testKey", new List<string> { "a", "b", "c", "d", "e" } }
        },
        E = new SubModel {
            A = "test2"
        }
    };
    var resp = await contentRpc.SendAsync("Simulation", "test/complex", model);
    Console.WriteLine($"{resp.DeserializePayload<bool>()}");
};

var testError = () => {
    Console.Write("Test error: ");
    testErrorCode = rnd.Next(1, 200);
    contentRpc.Send("Simulation", "test/error", testErrorCode);
};

var speedTest = async () => {
    var errorsCount = 0;
    double totalRoundtripTime = 0;
    double totalOneWayTime = 0;
    var count = 1000;
    var parallel = 1;

    Console.WriteLine($"Running speed test with {count} iterations, {parallel} in parallel: ");

    Console.Write("Warmup... ");
    var warmup = await contentRpc.SendAsync("Simulation", "test/timing", DateTime.Now);
    var warmupDes = warmup.DeserializePayload<TimeSpan>();
    Console.WriteLine("OK");

    Console.Write("Progress: ");

    var items = new List<int>();
    for (var i = 0; i < count; i++) {
        items.Add(i);
    }

    var counter = 0;
    var totalsw = Stopwatch.StartNew();
    await Parallel.ForEachAsync(items, new ParallelOptions() { MaxDegreeOfParallelism = parallel }, async (item, ct) => {
        if (counter++ % (count / 10) == 0) {
            Console.Write("#");
        }
        var sw = Stopwatch.StartNew();
        var res = await contentRpc.SendAsync("Simulation", "test/timing", DateTime.UtcNow);
        sw.Stop();
        totalRoundtripTime += sw.ElapsedMilliseconds;
        var des = res.DeserializePayload<TimeSpan>();
        if (des == TimeSpan.Zero) {
            errorsCount++;
        } else {
            totalOneWayTime += des.TotalMilliseconds;
        }
    });
    totalsw.Stop();

    Console.WriteLine();
    Console.WriteLine($"All done in {totalsw.ElapsedMilliseconds}ms.");
    Console.WriteLine($"Total errors: {errorsCount}.");
    Console.WriteLine($"Avg. roundtrip: {totalRoundtripTime / count}ms / call.");
    Console.WriteLine($"Avg. one way: {totalOneWayTime / (count - errorsCount)}");
};

//testTasks();
//await testTwoParam();
//await testComplexModel();
//testError();

await speedTest();

await Task.Delay(5000);

//var resp1 = await contentRpc.SendAsync("Simulation", "test/add", 5);
//var resp2 = await contentRpc.SendAsync("Simulation", "test/sub", 5);
//Console.WriteLine(resp1.Payload);
//Console.WriteLine(resp2.Payload);