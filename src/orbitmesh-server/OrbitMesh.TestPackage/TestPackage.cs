using OrbitMesh.Package;

namespace OrbitMesh.TestPackage;

/// <summary>
/// Minimal sanity-check package: exercises PackageHost's connect/settings/telemetry/message-handler
/// surface end-to-end against the modernized server.
/// </summary>
public sealed class TestPackage : IPackage
{
    private Timer? _timer;
    private int _counter;

    [TelemetryItemLink("TestPackage", "Counter", RequestValueOnInit = false)]
    public TelemetryItemNotifier? CounterNotifier { get; set; }

    public void OnStart()
    {
        PackageHost.WriteInfo("TestPackage starting. Greeting setting = '{0}'", PackageHost.GetSettingValue<string>("Greeting") ?? "(none)");

        if (CounterNotifier != null)
        {
            CounterNotifier.ValueChanged += (_, e) => PackageHost.WriteInfo("CounterNotifier saw an update: {0}", e.NewState?.GetValue<int>() ?? 0);
        }

        _timer = new Timer(_ =>
        {
            _counter++;
            PackageHost.WriteInfo("Pushing Counter = {0}", _counter);
            PackageHost.PushTelemetryItem("Counter", _counter);
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

        PackageHost.WriteInfo("TestPackage started");
    }

    public void OnPreShutdown() => PackageHost.WriteInfo("TestPackage stopping...");

    public void OnShutdown()
    {
        _timer?.Dispose();
        PackageHost.WriteInfo("TestPackage stopped");
    }

    [MessageHandler("Ping", Description = "Replies with Pong and the current counter value.")]
    public string Ping()
    {
        PackageHost.WriteInfo("Ping received");
        return $"Pong ({_counter})";
    }

    [MessageHandler("Add", Description = "Adds two integers and returns the sum.")]
    public int Add(int a, int b)
    {
        PackageHost.WriteInfo("Add({0}, {1}) received", a, b);
        return a + b;
    }

    [MessageHandler("Greet", Description = "Logs a greeting for the given name (no response).")]
    public void Greet(string name) => PackageHost.WriteInfo("Hello, {0}!", name);
}
