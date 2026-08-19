using System.ComponentModel;

namespace OrbitMesh.Package;

/// <summary>A single slot in a <see cref="TelemetryItemCollectionNotifier"/> - the current value of one
/// tracked telemetry item, with change notification for data-binding (<see cref="PropertyChanged"/>)
/// or imperative code (<see cref="ValueChanged"/>).</summary>
public class TelemetryItemNotifier : INotifyPropertyChanged
{
    private TelemetryItem? _value;

    /// <summary>The item's value, dynamically typed for convenient access without casting.</summary>
    public dynamic? DynamicValue => HasValue ? _value!.Value : null;

    /// <summary>True if a value has actually been received yet.</summary>
    public bool HasValue => _value?.HasValue ?? false;

    /// <summary>The tracked telemetry item's current full state.</summary>
    public TelemetryItem? Value
    {
        get => _value;
        set
        {
            var oldState = _value;
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DynamicValue)));
            ValueChanged?.Invoke(this, new TelemetryItemChangedEventArgs { OldState = oldState, NewState = value });
        }
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised whenever <see cref="Value"/> changes.</summary>
    public event EventHandler<TelemetryItemChangedEventArgs>? ValueChanged;
}
