using System.Diagnostics.Metrics;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Replaces the original Windows-only <c>System.Diagnostics.PerformanceCounter</c>-based counters with
/// <see cref="System.Diagnostics.Metrics"/> (cross-platform, native to .NET), exported via OpenTelemetry's
/// Prometheus exporter on <c>/metrics</c>.
/// </summary>
public sealed class OrbitMeshMetrics : IDisposable
{
    public const string MeterName = "OrbitMesh.Server";

    private readonly Meter _meter = new(MeterName, "1.0");

    private readonly Counter<long> _packageConnections;
    private readonly Counter<long> _packageDisconnections;
    private readonly Counter<long> _edgeConnections;
    private readonly Counter<long> _edgeDisconnections;
    private readonly Counter<long> _consumerConnections;
    private readonly Counter<long> _consumerDisconnections;
    private readonly Counter<long> _pushTelemetryItem;
    private readonly Counter<long> _requestTelemetryItems;
    private readonly Counter<long> _updateTelemetryItem;
    private readonly Counter<long> _sendMessage;
    private readonly Counter<long> _receiveMessage;
    private readonly Counter<long> _writeLog;
    private readonly Counter<long> _accessGranted;
    private readonly Counter<long> _accessDenied;
    private readonly Counter<long> _accessChecked;
    private readonly Counter<long> _subscribeTelemetryItems;

    private long _telemetryItemsCount;
    private long _edgesConnected;
    private long _packagesConnected;
    private long _consumersConnected;

    public OrbitMeshMetrics()
    {
        _packageConnections = _meter.CreateCounter<long>("orbitmesh.package.connections");
        _packageDisconnections = _meter.CreateCounter<long>("orbitmesh.package.disconnections");
        _edgeConnections = _meter.CreateCounter<long>("orbitmesh.edge.connections");
        _edgeDisconnections = _meter.CreateCounter<long>("orbitmesh.edge.disconnections");
        _consumerConnections = _meter.CreateCounter<long>("orbitmesh.consumer.connections");
        _consumerDisconnections = _meter.CreateCounter<long>("orbitmesh.consumer.disconnections");
        _pushTelemetryItem = _meter.CreateCounter<long>("orbitmesh.telemetry_item.pushed");
        _requestTelemetryItems = _meter.CreateCounter<long>("orbitmesh.telemetry_item.requested");
        _updateTelemetryItem = _meter.CreateCounter<long>("orbitmesh.telemetry_item.updates_sent");
        _sendMessage = _meter.CreateCounter<long>("orbitmesh.message.sent");
        _receiveMessage = _meter.CreateCounter<long>("orbitmesh.message.delivered");
        _writeLog = _meter.CreateCounter<long>("orbitmesh.log.written");
        _accessGranted = _meter.CreateCounter<long>("orbitmesh.access.granted");
        _accessDenied = _meter.CreateCounter<long>("orbitmesh.access.denied");
        _accessChecked = _meter.CreateCounter<long>("orbitmesh.access.checked");
        _subscribeTelemetryItems = _meter.CreateCounter<long>("orbitmesh.telemetry_item.subscriptions");

        _meter.CreateObservableGauge("orbitmesh.telemetry_items.count", () => _telemetryItemsCount);
        _meter.CreateObservableGauge("orbitmesh.edges.connected", () => _edgesConnected);
        _meter.CreateObservableGauge("orbitmesh.packages.connected", () => _packagesConnected);
        _meter.CreateObservableGauge("orbitmesh.consumers.connected", () => _consumersConnected);
    }

    public void PackageConnected() => _packageConnections.Add(1);
    public void PackageDisconnected() => _packageDisconnections.Add(1);
    public void EdgeConnected() => _edgeConnections.Add(1);
    public void EdgeDisconnected() => _edgeDisconnections.Add(1);
    public void ConsumerConnected() { _consumerConnections.Add(1); Interlocked.Increment(ref _consumersConnected); }
    public void ConsumerDisconnected() { _consumerDisconnections.Add(1); if (Interlocked.Read(ref _consumersConnected) > 0) Interlocked.Decrement(ref _consumersConnected); }
    public void PushTelemetryItem() => _pushTelemetryItem.Add(1);
    public void RequestTelemetryItems() => _requestTelemetryItems.Add(1);
    public void UpdateTelemetryItemSent(long count = 1) => _updateTelemetryItem.Add(count);
    public void SendMessage() => _sendMessage.Add(1);
    public void MessageDelivered(long count = 1) => _receiveMessage.Add(count);
    public void WriteLog() => _writeLog.Add(1);
    public void AccessGranted() => _accessGranted.Add(1);
    public void AccessDenied() => _accessDenied.Add(1);
    public void AccessChecked() => _accessChecked.Add(1);
    public void SubscribeTelemetryItems() => _subscribeTelemetryItems.Add(1);

    public void SetTelemetryItemsCount(long count) => Interlocked.Exchange(ref _telemetryItemsCount, count);
    public void SetEdgesConnected(long count) => Interlocked.Exchange(ref _edgesConnected, count);
    public void SetPackagesConnected(long count) => Interlocked.Exchange(ref _packagesConnected, count);
    public long ConsumersConnected => Interlocked.Read(ref _consumersConnected);

    public void Dispose() => _meter.Dispose();
}
