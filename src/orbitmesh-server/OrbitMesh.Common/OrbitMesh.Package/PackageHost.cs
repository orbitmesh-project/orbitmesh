using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml;
using OrbitMesh.Deployment;
using OrbitMesh.Utils;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace OrbitMesh.Package;

/// <summary>
/// Entry point and runtime facade used by package authors: connects to the OrbitMesh server,
/// dispatches messages/telemetry items to attribute-decorated members, and exposes settings/logging helpers.
/// </summary>
public static class PackageHost
{
    private const int RetryConnectionIntervalMs = 3000;

    private static readonly Dictionary<string, object> EmptyMetadatas = new();
    private static readonly List<string> MessageGroups = new();
    private static readonly List<(string Edge, string Package, string Name, string Type)> TelemetryItemSubscriptions = new();
    private static readonly List<MethodInfo> MessageHandlersAttached = new();
    private static readonly Dictionary<string, List<Action<object?>>> MessageHandlers = new();
    private static readonly TaskCompletionSource ShutdownSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Type? _packageType;
    private static HubConnection? _connection;
    private static bool _handlersRegistered;

    /// <summary>The running <see cref="IPackage"/> instance, or null before <see cref="Start(Type,string[])"/>/after <see cref="Shutdown"/>.</summary>
    public static IPackage? Package { get; private set; }

    /// <summary>The manifest read from this package's own <c>PackageInfo.xml</c>, if it shipped one.</summary>
    public static PackageManifest? PackageManifest { get; private set; }

    /// <summary>The package's name - from the manifest if present, otherwise the entry assembly's name.</summary>
    public static string PackageName { get; private set; } = string.Empty;

    /// <summary>The name this specific instance was deployed under (an Edge can run more than one
    /// instance of the same package). Equal to <see cref="PackageName"/> unless the Edge overrode it.</summary>
    public static string PackageInstanceName { get; private set; } = string.Empty;

    /// <summary>The entry assembly's own version, used as a fallback for <see cref="PackageVersion"/>
    /// when there's no manifest.</summary>
    public static string? PackageAssemblyVersion { get; private set; }

    /// <summary>The package's effective version: the manifest's if present, else <see cref="PackageAssemblyVersion"/>, else "0.0.0".</summary>
    public static string PackageVersion => PackageManifest?.Version ?? PackageAssemblyVersion ?? "0.0.0";

    /// <summary>The name of the Edge this package is running under, or null in standalone/developer mode.</summary>
    public static string? EdgeName { get; private set; }

    /// <summary>This SDK's own assembly version, sent to the Server on connect.</summary>
    public static string OrbitMeshClientVersion { get; private set; } = string.Empty;

    /// <summary>True from <see cref="Start(Type,string[])"/> until <see cref="Shutdown"/> - independent
    /// of <see cref="IsConnected"/>, which tracks the live connection specifically.</summary>
    public static bool IsRunning { get; private set; }

    /// <summary>True while the SignalR connection to the Server is up. False in standalone mode, where
    /// there is no server connection at all.</summary>
    public static bool IsConnected { get; private set; }

    /// <summary>Whether a dropped connection is retried automatically. True by default.</summary>
    public static bool AutoReconnect { get; set; } = true;

    /// <summary>True when running without a Server connection - either launched with no connection
    /// arguments, or explicitly as the developer edge (see <c>OrbitMeshDefaultNames.DeveloperEdgeName</c>).
    /// Settings then come from the local <c>settings.json</c>/manifest defaults instead of the Server.</summary>
    public static bool IsStandAlone { get; private set; }

    /// <summary>The package's current settings (raw string values - see <see cref="GetSettingValue{T}"/>
    /// and friends for typed access), or null until the first settings response/local load completes.</summary>
    public static Dictionary<string, string>? Settings { get; private set; }

    /// <summary>The reflection-derived description of this package's message handlers/telemetry types,
    /// sent to the Server so the Console can show what the package exposes. Built automatically by
    /// <see cref="RegisterMessageHandlers"/>/<see cref="DescribeTelemetryItemTypes"/>.</summary>
    public static PackageDescriptor? PackageDescriptor { get; private set; }

    /// <summary>Raised whenever <see cref="Settings"/> is replaced with a new value (initial load or a
    /// live push from the Console).</summary>
    public static event EventHandler? SettingsUpdated;

    /// <summary>Raised when a subscribed telemetry item's value updates - see
    /// <see cref="RegisterTelemetryItemCallback"/>/<see cref="SubscribeTelemetryItems"/>.</summary>
    public static event EventHandler<TelemetryItemUpdatedEventArgs>? TelemetryItemUpdated;

    /// <summary>Raised in response to <see cref="RequestTelemetryItems"/>, with the current values of
    /// every item matching the request.</summary>
    public static event EventHandler<TelemetryItemsListEventArgs>? LastTelemetryItemsReceived;

    /// <summary>Raised whenever <see cref="IsConnected"/> changes (connected, reconnecting, reconnected).</summary>
    public static event EventHandler? ConnectionStateChanged;

    /// <summary>Starts the package and blocks the calling thread until <see cref="Shutdown"/> is called
    /// - the usual call from <c>Main</c>. <paramref name="args"/> is the process's own command-line
    /// args, forwarded as-is: the Edge launches a package with the connection info
    /// (server URI, edge name, package name, access key, parent process id) as positional arguments.</summary>
    public static void Start<TPackage>(string[]? args = null) where TPackage : IPackage, new() => Start(typeof(TPackage), args);

    /// <summary>Non-generic version of <see cref="Start{TPackage}"/>, for when the package type isn't
    /// known at compile time.</summary>
    public static void Start(Type packageType, string[]? args = null) => StartAsync(packageType, args).GetAwaiter().GetResult();

    /// <summary>Async version of <see cref="Start{TPackage}"/> - awaits instead of blocking the calling
    /// thread until <see cref="Shutdown"/> is called.</summary>
    public static Task StartAsync<TPackage>(string[]? args = null, CancellationToken cancellationToken = default) where TPackage : IPackage, new() =>
        StartAsync(typeof(TPackage), args, cancellationToken);

    /// <summary>Non-generic version of <see cref="StartAsync{TPackage}"/>, for when the package type
    /// isn't known at compile time.</summary>
    public static async Task StartAsync(Type packageType, string[]? args = null, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("The package is already started!");
        }

        _packageType = packageType;
        PackageManifest = PackageManifestHelper.CurrentPackageManifest;
        PackageName = PackageInstanceName = !string.IsNullOrEmpty(PackageManifest?.Name) ? PackageManifest!.Name : packageType.Assembly.GetName().Name ?? packageType.Name;
        PackageAssemblyVersion = packageType.Assembly.GetName().Version?.ToString();
        OrbitMeshClientVersion = typeof(PackageHost).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        PackageDescriptor = new PackageDescriptor { PackageName = PackageName };

        try
        {
            Package = Activator.CreateInstance(packageType) as IPackage
                ?? throw new InvalidCastException("The package type is not an IPackage. You must implement the IPackage interface on your package.");

            DescribeTelemetryItemTypesFromAssembly();
            var knownTypesAttribute = packageType.GetCustomAttribute<TelemetryItemKnownTypesAttribute>();
            if (knownTypesAttribute is { TelemetryItemKnownTypes.Length: > 0 })
            {
                DescribeTelemetryItemTypes(knownTypesAttribute.TelemetryItemKnownTypes);
            }

            string? orbitmeshUri = args is { Length: > 0 } ? args[0] : null;
            string? edgeName = args is { Length: > 1 } ? args[1] : null;
            string? packageName = args is { Length: > 2 } ? args[2] : null;
            string? accessKey = args is { Length: > 3 } ? args[3] : null;
            int? parentProcessId = args is { Length: > 4 } && int.TryParse(args[4], out var pid) ? pid : null;

            if (!string.IsNullOrEmpty(orbitmeshUri) && !string.IsNullOrEmpty(edgeName) && !string.IsNullOrEmpty(packageName))
            {
                EdgeName = edgeName;
                PackageInstanceName = packageName;
                IsStandAlone = edgeName == OrbitMeshDefaultNames.DeveloperEdgeName;
                if (!IsStandAlone && parentProcessId.HasValue)
                {
                    AttachToParent(parentProcessId.Value);
                }

                await ConnectAsync(orbitmeshUri, edgeName, packageName, accessKey, cancellationToken);
            }
            else
            {
                IsStandAlone = true;
            }

            IsRunning = true;
            await WaitForSettingsAsync();
            WriteInfo("Calling OnStart on {0}", packageType.FullName!);
            Package.OnStart();

            await ShutdownSignal.Task;
        }
        catch (Exception ex)
        {
            var inner = ex is AggregateException { InnerException: not null } agg ? agg.InnerException! : ex;
            WriteLog(LogLevel.Fatal, "Critical Error : " + inner, awaitCompletion: true, catchException: true);
            if (IsStandAlone)
            {
                throw inner;
            }
        }
    }

    private static async Task ConnectAsync(string orbitmeshUri, string edgeName, string packageName, string? accessKey, CancellationToken cancellationToken)
    {
        IDictionary<string, string>? connectionHeaders = null;
        _connection = new HubConnectionBuilder()
            .WithUrl($"{orbitmeshUri.TrimEnd('/')}/signalr/hubs/{OrbitMeshHubNames.OrbitMesh}", options =>
            {
                options.Headers.Add(OrbitMeshHeaderNames.OrbitMeshClientVersion, OrbitMeshClientVersion);
                options.Headers.Add(OrbitMeshHeaderNames.OrbitMeshClientType, "net");
                options.Headers.Add(OrbitMeshHeaderNames.PackageVersion, PackageVersion);
                options.Headers.Add(OrbitMeshHeaderNames.EdgeName, edgeName);
                options.Headers.Add(OrbitMeshHeaderNames.PackageName, packageName);
                // Flipped to true right after the first successful connect below - every negotiate
                // from then on (SignalR's own automatic reconnect, or our manual retry in Closed)
                // reads this same live dictionary, so OrbitMeshHub knows not to purge telemetry for
                // what is, from the server's perspective, the same logical session resuming.
                options.Headers.Add(OrbitMeshHeaderNames.IsReconnection, bool.FalseString);
                if (!string.IsNullOrEmpty(accessKey))
                {
                    options.Headers.Add(OrbitMeshHeaderNames.AccessKey, accessKey);
                }
                connectionHeaders = options.Headers;
            })
            .WithOrbitMeshDefaults()
            .Build();

        AttachHubEvents(_connection);

        _connection.Reconnected += _ =>
        {
            IsConnected = true;
            OnConnectedOrReconnected();
            ConnectionStateChanged?.Invoke(null, EventArgs.Empty);
            return Task.CompletedTask;
        };
        _connection.Reconnecting += _ =>
        {
            IsConnected = false;
            ConnectionStateChanged?.Invoke(null, EventArgs.Empty);
            return Task.CompletedTask;
        };
        _connection.Closed += async ex =>
        {
            IsConnected = false;
            ConnectionStateChanged?.Invoke(null, EventArgs.Empty);
            if (AutoReconnect && IsRunning)
            {
                WriteLog(LogLevel.Warn, $"Disconnected from the hub! Trying to reconnect in {RetryConnectionIntervalMs / 1000} seconds ....", awaitCompletion: false, catchException: true);
                await Task.Delay(RetryConnectionIntervalMs);
                try
                {
                    await _connection.StartAsync();
                }
                catch (Exception reconnectEx)
                {
                    WriteError("Unable to reconnect : {0}", reconnectEx.Message);
                }
            }
        };

        await _connection.StartAsync(cancellationToken);
        IsConnected = true;
        connectionHeaders![OrbitMeshHeaderNames.IsReconnection] = bool.TrueString;
        OnConnectedOrReconnected();
    }

    private static void OnConnectedOrReconnected()
    {
        if (!_handlersRegistered)
        {
            RegisterMessageHandlers(Package!);
            RegisterTelemetryItemLinks(Package!);
            _handlersRegistered = true;
        }
        else
        {
            foreach (var group in MessageGroups)
            {
                _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.SubscribeMessages, group);
            }
            foreach (var (edge, package, name, type) in TelemetryItemSubscriptions)
            {
                _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.RequestTelemetryItems, edge, package, name, type);
                _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.SubscribeTelemetryItems, edge, package, name, type);
            }
        }
        DeclarePackageDescriptor();
    }

    private sealed class ReceivedMessage
    {
        public string Key { get; set; } = string.Empty;
        public MessageScope Scope { get; set; } = new(MessageScope.ScopeType.None);
        public MessageSender Sender { get; set; } = new();
        public object? Data { get; set; }
    }

    private static void AttachHubEvents(HubConnection connection)
    {
        connection.On<PackageControlAction>(EdgeClientMethodNames.PackageControlAction, action =>
        {
            if (action is PackageControlAction.Stop or PackageControlAction.Restart or PackageControlAction.Reload)
            {
                Shutdown();
            }
        });
        connection.On<Dictionary<string, string>>(OrbitMeshClientMethodNames.UpdateSettings, config =>
        {
            LoadSettingsFromLocalConfigurationFile(config);
            CheckSettingsFromManifest(config);
            Settings = config;
            SettingsUpdated?.Invoke(null, EventArgs.Empty);
        });
        connection.On<List<TelemetryItem>>(OrbitMeshClientMethodNames.ReceiveLastTelemetryItems, telemetryItems =>
        {
            LastTelemetryItemsReceived?.Invoke(null, new TelemetryItemsListEventArgs { TelemetryItems = telemetryItems });
        });
        connection.On<TelemetryItem>(OrbitMeshClientMethodNames.UpdateTelemetryItem, so =>
        {
            TelemetryItemUpdated?.Invoke(null, new TelemetryItemUpdatedEventArgs { TelemetryItem = so });
        });
        connection.On<ReceivedMessage>(OrbitMeshClientMethodNames.ReceiveMessage, message =>
        {
            if (!MessageHandlers.TryGetValue(message.Key, out var callbacks))
            {
                return;
            }
            foreach (var callback in callbacks.ToArray())
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        MessageContext.Current = new MessageContext { Scope = message.Scope, Sender = message.Sender };
                        callback(message.Data);
                    }
                    catch (Exception ex)
                    {
                        WriteError("Unable to dispatch the message {0} : {1}", message.Key, ex);
                    }
                });
            }
        });
    }

    /// <summary>Wires up every <see cref="MessageHandlerAttribute"/>-decorated method on
    /// <paramref name="source"/> (typically <c>this</c>, called once from <see cref="IPackage.OnStart"/>)
    /// so it can be invoked remotely by key. Also builds the reflection-derived <see cref="PackageDescriptor"/>
    /// entries the Console uses to list available handlers. Safe to call more than once - already-attached
    /// methods are skipped.</summary>
    public static void RegisterMessageHandlers(object source)
    {
        var methods = source.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
        foreach (var method in methods)
        {
            var attribute = method.GetCustomAttribute<MessageHandlerAttribute>();
            if (attribute == null || MessageHandlersAttached.Contains(method))
            {
                continue;
            }
            var messageKey = string.IsNullOrEmpty(attribute.Key) ? method.Name : attribute.Key;
            var parameters = method.GetParameters();
            RegisterMessageHandler(messageKey, data =>
            {
                object? result = null;
                try
                {
                    result = method.Invoke(source, GetMessageArguments(data, parameters));
                    if (result is Task task)
                    {
                        // method.Invoke on an async method returns the (still-running) Task itself -
                        // without this, the saga response would be sent immediately with the Task
                        // object instead of its awaited result, and any exception thrown after the
                        // method's first "await" would go unobserved (no error log, no response).
                        task.GetAwaiter().GetResult();
                        result = task.GetType().GetProperty("Result")?.GetValue(task);
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    WriteError("Unable to dispath the message " + messageKey + " : invalid argument count");
                }
                catch (Exception ex)
                {
                    WriteError("Unable to dispath the message " + messageKey + " : " + ex.Message);
                }
                if (result != null && MessageContext.Current.IsSaga)
                {
                    MessageContext.Current.SendResponse(result);
                }
            }, attribute.Shared);
            if (!attribute.IsHidden)
            {
                var descriptor = new PackageDescriptor.MessageHandlerDescriptor
                {
                    // The actual key callers must use - namespaced under PackageName unless Shared.
                    MessageKey = QualifyKey(messageKey, attribute.Shared),
                    Description = attribute.Description ?? string.Empty,
                    Parameters = GetParametersDescription(parameters, PackageDescriptor!.MessageHandlerTypes)
                };
                XmlDocumentationReader.AddXmlComment(method, descriptor);
                // Unwrap Task/Task<T> so the descriptor advertises the awaited result type, not the Task itself.
                var returnType = method.ReturnType;
                if (typeof(Task).IsAssignableFrom(returnType))
                {
                    returnType = returnType.IsGenericType ? returnType.GetGenericArguments()[0] : typeof(void);
                }
                if (attribute.ResponseType != null)
                {
                    descriptor.ResponseType = DescribeType(attribute.ResponseType, PackageDescriptor.MessageHandlerTypes);
                }
                else if (returnType != typeof(void))
                {
                    descriptor.ResponseType = DescribeType(returnType, PackageDescriptor.MessageHandlerTypes);
                }
                PackageDescriptor.MessageHandlers.Add(descriptor);
            }
            MessageHandlersAttached.Add(method);
        }
    }

    /// <summary>Invokes <paramref name="callback"/> whenever a telemetry item matching the given
    /// edge/package/name/type ("*" matches anything) updates - the imperative alternative to
    /// <see cref="TelemetryItemLinkAttribute"/>. Also subscribes to future updates for that filter, and
    /// (if <paramref name="requestValueOnInit"/>) requests the current value immediately.</summary>
    public static void RegisterTelemetryItemCallback(Action<TelemetryItem> callback, string edge = "*", string package = "*", string name = "*", string type = "*", bool requestValueOnInit = true)
    {
        TelemetryItemUpdated += (_, e) =>
        {
            if ((edge == "*" || edge.Equals(e.TelemetryItem.EdgeName, StringComparison.OrdinalIgnoreCase))
                && (package == "*" || package.Equals(e.TelemetryItem.PackageName, StringComparison.OrdinalIgnoreCase))
                && (name == "*" || name.Equals(e.TelemetryItem.Name, StringComparison.OrdinalIgnoreCase))
                && (type == "*" || type.Equals(e.TelemetryItem.Type, StringComparison.OrdinalIgnoreCase)))
            {
                callback(e.TelemetryItem);
            }
        };
        if (requestValueOnInit)
        {
            RequestTelemetryItems(edge, package, name, type);
        }
        SubscribeTelemetryItems(edge, package, name, type);
    }

    /// <summary>Wires up every <see cref="TelemetryItemLinkAttribute"/>-decorated property on
    /// <paramref name="instance"/> - each becomes a live mirror of the telemetry item it targets. A
    /// no-op if <paramref name="instance"/> is null.</summary>
    public static void RegisterTelemetryItemLinks(object? instance)
    {
        if (instance != null)
        {
            RegisterTelemetryItemLinks(instance.GetType(), instance);
        }
    }

    /// <summary>Wires up every <see cref="TelemetryItemLinkAttribute"/>-decorated property declared on
    /// <typeparamref name="T"/> - pass <paramref name="instance"/> for instance properties, or omit it
    /// for static ones.</summary>
    public static void RegisterTelemetryItemLinks<T>(object? instance = null) where T : class => RegisterTelemetryItemLinks(typeof(T), instance);

    /// <summary>Non-generic version of <see cref="RegisterTelemetryItemLinks{T}"/>, for when the type
    /// isn't known at compile time.</summary>
    public static void RegisterTelemetryItemLinks(Type type, object? instance = null)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
        foreach (var prop in properties)
        {
            try
            {
                var attribute = prop.GetCustomAttribute<TelemetryItemLinkAttribute>();
                if (attribute == null || prop.GetValue(instance, null) != null)
                {
                    continue;
                }
                if (prop.PropertyType == typeof(TelemetryItemNotifier) || prop.PropertyType.IsSubclassOf(typeof(TelemetryItemNotifier)))
                {
                    var notifier = (TelemetryItemNotifier)Activator.CreateInstance(prop.PropertyType)!;
                    prop.SetValue(instance, notifier, null);
                    RegisterTelemetryItemCallback(so => notifier.Value = so, attribute.Edge, attribute.Package, attribute.Name, attribute.Type, attribute.RequestValueOnInit);
                }
                else if (prop.PropertyType == typeof(TelemetryItemCollectionNotifier) || prop.PropertyType.IsSubclassOf(typeof(TelemetryItemCollectionNotifier)))
                {
                    var collection = (TelemetryItemCollectionNotifier)Activator.CreateInstance(prop.PropertyType)!;
                    prop.SetValue(instance, collection, null);
                    RegisterTelemetryItemCallback(so => collection.AddOrUpdate(so), attribute.Edge, attribute.Package, attribute.Name, attribute.Type, attribute.RequestValueOnInit);
                }
                else
                {
                    RegisterTelemetryItemCallback(so => prop.SetValue(instance, prop.PropertyType == typeof(TelemetryItem) ? so : so.DynamicValue, null),
                        attribute.Edge, attribute.Package, attribute.Name, attribute.Type, attribute.RequestValueOnInit);
                }
            }
            catch (Exception ex)
            {
                WriteError("Unable to initialize the TelemetryItemLink on {0}.{1} : {2}", type.FullName!, prop.Name, ex.Message);
            }
        }
    }

    /// <summary>Adds the given types to the package's <see cref="PackageDescriptor"/> as known
    /// telemetry item shapes, so the Console can render their structure. Called automatically for any
    /// type from <see cref="DescribeTelemetryItemTypesFromAssembly"/> and for
    /// <see cref="TelemetryItemKnownTypesAttribute"/> on the package type itself - call this directly
    /// only for additional types that aren't already covered by either.</summary>
    public static void DescribeTelemetryItemTypes(params Type[] telemetryItemTypes)
    {
        foreach (var type in telemetryItemTypes)
        {
            DescribeType(type, PackageDescriptor!.TelemetryItemTypes);
        }
    }

    /// <summary>Scans <paramref name="assembly"/> (the entry assembly by default) for every
    /// <see cref="TelemetryItemAttribute"/>-decorated type and describes each via
    /// <see cref="DescribeTelemetryItemTypes"/>. Called automatically during <see cref="StartAsync(Type,string[],CancellationToken)"/>.</summary>
    public static void DescribeTelemetryItemTypesFromAssembly(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetEntryAssembly();
        if (assembly == null)
        {
            return;
        }
        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<TelemetryItemAttribute>() != null)
            {
                DescribeType(type, PackageDescriptor!.TelemetryItemTypes);
            }
        }
    }

    /// <summary>Sends the current <see cref="PackageDescriptor"/> to the Server. Called automatically
    /// on connect/reconnect - only needed directly if the descriptor changes at runtime.</summary>
    public static void DeclarePackageDescriptor()
    {
        if (PackageDescriptor != null && CheckOrbitMeshConnection())
        {
            _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.DeclarePackageDescriptor, PackageDescriptor);
        }
    }

    /// <summary>Registers <paramref name="onResponse"/> to fire when a response tagged with
    /// <paramref name="sagaId"/> comes back - the lower-level primitive <see cref="SendMessageAsync{TResponse}"/>
    /// and <see cref="MessageExtension.OnSagaResponse{TResponse}"/> build on.</summary>
    public static void RegisterSagaResponseCallback<TResponse>(string sagaId, Action<TResponse?> onResponse) =>
        // shared: true - __Response is a fixed system key correlated by SagaId, not by package identity;
        // namespacing it would break responses arriving from a different package than the requester.
        RegisterMessageHandler(OrbitMeshDefaultNames.SagaResponseMessageKey, data =>
        {
            if (MessageContext.Current.SagaId == sagaId)
            {
                onResponse(ObjectConverter.ConvertToObject<TResponse>(data));
            }
        }, shared: true);

    /// <summary>Lower-level alternative to <see cref="MessageHandlerAttribute"/>/<see cref="RegisterMessageHandlers"/>
    /// for registering a single handler imperatively, with the incoming payload deserialized to
    /// <typeparamref name="TMessage"/>.</summary>
    public static void RegisterMessageHandler<TMessage>(string key, Action<TMessage?> onData, bool shared = false) =>
        RegisterMessageHandler(key, data => onData(ObjectConverter.ConvertToObject<TMessage>(data)), shared);

    /// <summary>Lower-level alternative to <see cref="MessageHandlerAttribute"/>/<see cref="RegisterMessageHandlers"/>
    /// for registering a single handler imperatively, with the raw (untyped) payload. By default the key
    /// is namespaced under this package's own name - see <see cref="MessageHandlerAttribute.Shared"/>.</summary>
    public static void RegisterMessageHandler(string key, Action<object?> onData, bool shared = false)
    {
        var qualifiedKey = QualifyKey(key, shared);
        if (!MessageHandlers.TryGetValue(qualifiedKey, out var list))
        {
            MessageHandlers[qualifiedKey] = list = new List<Action<object?>>();
        }
        list.Add(onData);
    }

    /// <summary>Namespaces a message key under this package's own name, so an unrelated package's
    /// same-named key/handler can't collide with it - see <see cref="MessageHandlerAttribute.Shared"/>.</summary>
    private static string QualifyKey(string key, bool shared) => shared ? key : $"{PackageName}/{key}";

    /// <summary>Sends a message and asynchronously awaits a saga response of the given type.</summary>
    public static async Task<TResponse?> SendMessageAsync<TResponse>(MessageScope scope, string key, object? data, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        scope.WithSaga();
        var tcs = new TaskCompletionSource<TResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisterSagaResponseCallback<TResponse>(scope.SagaId!, response => tcs.TrySetResult(response));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        await using (cts.Token.Register(() => tcs.TrySetCanceled()).ConfigureAwait(false))
        {
            SendMessage(scope, key, data);
            return await tcs.Task;
        }
    }

    /// <summary>True if a value is currently set for this setting key.</summary>
    public static bool ContainsSetting(string key) => Settings?.ContainsKey(key) ?? false;

    /// <summary>The raw string value for this setting key. Throws if unset - see <see cref="ContainsSetting"/>
    /// or <see cref="TryGetSettingValue{T}"/> to check first.</summary>
    public static string GetSettingValue(string key) => Settings![key];

    /// <summary>The setting's value, converted to <typeparamref name="T"/>. Decimal separators are
    /// normalized to "." for floating-point types unless <paramref name="provider"/> is given
    /// explicitly, since settings are typically authored without locale in mind.</summary>
    public static T? GetSettingValue<T>(string key, IFormatProvider? provider = null)
    {
        var text = Settings![key];
        var typeCode = Type.GetTypeCode(typeof(T));
        if (provider == null && typeCode is TypeCode.Decimal or TypeCode.Double or TypeCode.Single)
        {
            text = text.Replace(",", ".");
            provider = System.Globalization.CultureInfo.InvariantCulture;
        }
        return (T?)Convert.ChangeType(text, typeof(T), provider ?? System.Globalization.CultureInfo.CurrentCulture);
    }

    /// <summary>Like <see cref="GetSettingValue{T}"/>, but returns <paramref name="defaultValue"/> and
    /// false (instead of throwing) if the key is unset or the conversion fails.</summary>
    public static bool TryGetSettingValue<T>(string key, out T? result, T? defaultValue = default)
    {
        try
        {
            result = GetSettingValue<T>(key);
            return true;
        }
        catch
        {
            result = defaultValue;
            return false;
        }
    }

    /// <summary>Parses a <c>JsonObject</c>-typed setting into a <see cref="JsonNode"/> for manual
    /// navigation. Returns null on missing/invalid JSON unless <paramref name="throwException"/> is set.</summary>
    public static JsonNode? GetSettingAsJson(string key, bool throwException = false)
    {
        try
        {
            return JsonNode.Parse(GetSettingValue<string>(key)!);
        }
        catch when (!throwException)
        {
            return null;
        }
    }

    /// <summary>Deserializes a <c>JsonObject</c>-typed setting directly into <typeparamref name="T"/>.
    /// Returns default on missing/invalid JSON unless <paramref name="throwException"/> is set.</summary>
    public static T? GetSettingAsJson<T>(string key, bool throwException = false)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(GetSettingValue<string>(key)!, ObjectConverter.DefaultOptions);
        }
        catch when (!throwException)
        {
            return default;
        }
    }

    /// <summary>Parses an <c>XmlDocument</c>-typed setting. Returns null on missing/invalid XML unless
    /// <paramref name="throwException"/> is set.</summary>
    public static XmlDocument? GetSettingAsXmlDocument(string key, bool throwException = false)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(GetSettingValue<string>(key)!);
            return doc;
        }
        catch when (!throwException)
        {
            return null;
        }
    }

    /// <summary>Asks the Server to (re-)send this package's settings, refreshing <see cref="Settings"/>
    /// and raising <see cref="SettingsUpdated"/> when they arrive. False if not connected. Called
    /// automatically on startup - only needed directly to force a refresh.</summary>
    public static bool RequestSettings()
    {
        if (!CheckOrbitMeshConnection())
        {
            return false;
        }
        _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.RequestSettings);
        return true;
    }

    /// <summary>Sends a one-way, fire-and-forget message - see <see cref="SendMessageAsync{TResponse}"/>
    /// if you need a response.</summary>
    public static void SendMessage<TMessage>(MessageScope scope, string key, TMessage value, bool shared = false) => SendMessage(scope, key, (object?)value, shared);

    /// <summary>Sends a one-way, fire-and-forget message with an untyped payload. By default the key is
    /// namespaced under this package's own name - see <see cref="MessageHandlerAttribute.Shared"/>.</summary>
    public static void SendMessage(MessageScope scope, string key, object? value, bool shared = false)
    {
        if (CheckOrbitMeshConnection())
        {
            _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.SendMessage, scope, QualifyKey(key, shared), value ?? string.Empty);
        }
    }

    /// <summary>Joins a named message group (see <see cref="MessageScope.ScopeType.Group"/>) - lets
    /// this package receive messages addressed to the group instead of only to itself by name.</summary>
    public static void SubscribeMessages(string groupName)
    {
        if (CheckOrbitMeshConnection())
        {
            MessageGroups.Add(groupName);
            _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.SubscribeMessages, groupName);
        }
    }

    /// <summary>Leaves a message group previously joined with <see cref="SubscribeMessages"/>.</summary>
    public static void UnSubscribeMessages(string groupName)
    {
        if (CheckOrbitMeshConnection())
        {
            MessageGroups.Remove(groupName);
            _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.UnSubscribeMessages, groupName);
        }
    }

    /// <summary>Publishes a telemetry item value, typed. <paramref name="type"/> defaults to the
    /// value's own type name.</summary>
    public static void PushTelemetryItem<TTelemetryItem>(string name, TTelemetryItem value, string type = "", Dictionary<string, object>? metadatas = null, int lifetime = 0) =>
        PushTelemetryItem(name, (object?)value, type, metadatas, lifetime);

    /// <summary>Publishes a telemetry item value with an untyped payload. <paramref name="lifetime"/>
    /// is how many seconds the value stays valid before being considered expired (0 = never).</summary>
    public static void PushTelemetryItem(string name, object? value, string type = "", Dictionary<string, object>? metadatas = null, int lifetime = 0)
    {
        if (CheckOrbitMeshConnection())
        {
            _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.PushTelemetryItem, name, value ?? string.Empty,
                string.IsNullOrEmpty(type) ? GetTelemetryItemTypeName(value) : type, metadatas ?? EmptyMetadatas, lifetime);
        }
    }

    /// <summary>Deletes telemetry items matching name/type ("*" matches anything) that this package
    /// previously published.</summary>
    public static void PurgeTelemetryItems(string name = "*", string type = "*")
    {
        if (CheckOrbitMeshConnection())
        {
            _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.PurgeTelemetryItems, name, type);
        }
    }

    /// <summary>Asks the Server for the current value of every telemetry item matching
    /// edge/package/name/type ("*" matches anything) - results arrive via
    /// <see cref="LastTelemetryItemsReceived"/>.</summary>
    public static void RequestTelemetryItems(string edge = "*", string package = "*", string name = "*", string type = "*")
    {
        if (CheckOrbitMeshConnection())
        {
            _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.RequestTelemetryItems, edge, package, name, type);
        }
    }

    /// <summary>Subscribes to future updates for telemetry items matching edge/package/name/type ("*"
    /// matches anything) - updates arrive via <see cref="TelemetryItemUpdated"/>. Lower-level than
    /// <see cref="RegisterTelemetryItemCallback"/>, which wraps this with the callback dispatch too.</summary>
    public static void SubscribeTelemetryItems(string edge = "*", string package = "*", string name = "*", string type = "*")
    {
        if (!CheckOrbitMeshConnection())
        {
            return;
        }
        _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.SubscribeTelemetryItems, edge, package, name, type);
        if (!TelemetryItemSubscriptions.Any(t => t.Edge.Equals(edge, StringComparison.OrdinalIgnoreCase) && t.Package.Equals(package, StringComparison.OrdinalIgnoreCase) && t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.Type.Equals(type, StringComparison.OrdinalIgnoreCase)))
        {
            TelemetryItemSubscriptions.Add((edge, package, name, type));
        }
    }

    /// <summary>Undoes a previous <see cref="SubscribeTelemetryItems"/> call with the same filter.</summary>
    public static void UnSubscribeTelemetryItems(string edge = "*", string package = "*", string name = "*", string type = "*")
    {
        if (!CheckOrbitMeshConnection())
        {
            return;
        }
        TelemetryItemSubscriptions.RemoveAll(t => t.Edge.Equals(edge, StringComparison.OrdinalIgnoreCase) && t.Package.Equals(package, StringComparison.OrdinalIgnoreCase) && t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        _ = _connection!.InvokeAsync(OrbitMeshServerMethodNames.UnSubscribeTelemetryItems, edge, package, name, type);
    }

    /// <summary>Logs at <see cref="LogLevel.Debug"/> - console/local only, never sent to the Server.</summary>
    public static void WriteDebug(object? value) => WriteLog(LogLevel.Debug, value?.ToString() ?? "null");

    /// <summary>Logs at <see cref="LogLevel.Debug"/> with <see cref="string.Format(string,object[])"/>-style
    /// arguments - console/local only, never sent to the Server.</summary>
    public static void WriteDebug(string format, params object[] args) => WriteLog(LogLevel.Debug, string.Format(format, args));

    /// <summary>Logs at <see cref="LogLevel.Error"/>, sent to the Server (surfaced in the Console's
    /// Console log page, scoped to this Edge/package).</summary>
    public static void WriteError(object? value) => WriteLog(LogLevel.Error, value?.ToString() ?? "null");

    /// <summary>Logs at <see cref="LogLevel.Error"/> with <see cref="string.Format(string,object[])"/>-style
    /// arguments, sent to the Server.</summary>
    public static void WriteError(string format, params object[] args) => WriteLog(LogLevel.Error, string.Format(format, args));

    /// <summary>Logs at <see cref="LogLevel.Info"/>, sent to the Server.</summary>
    public static void WriteInfo(object? value) => WriteLog(LogLevel.Info, value?.ToString() ?? "null");

    /// <summary>Logs at <see cref="LogLevel.Info"/> with <see cref="string.Format(string,object[])"/>-style
    /// arguments, sent to the Server.</summary>
    public static void WriteInfo(string format, params object[] args) => WriteLog(LogLevel.Info, string.Format(format, args));

    /// <summary>Logs at <see cref="LogLevel.Warn"/>, sent to the Server.</summary>
    public static void WriteWarn(object? value) => WriteLog(LogLevel.Warn, value?.ToString() ?? "null");

    /// <summary>Logs at <see cref="LogLevel.Warn"/> with <see cref="string.Format(string,object[])"/>-style
    /// arguments, sent to the Server.</summary>
    public static void WriteWarn(string format, params object[] args) => WriteLog(LogLevel.Warn, string.Format(format, args));

    /// <summary>The primitive every <c>Write*</c> helper calls into - also writes to the console
    /// directly regardless of level. <paramref name="awaitCompletion"/> blocks until the Server has
    /// acknowledged the log (useful right before a crash/shutdown); <paramref name="catchException"/>
    /// swallows a failed send instead of throwing (the message still reaches the local console either way).</summary>
    public static void WriteLog(LogLevel level, string message, bool awaitCompletion = false, bool catchException = false)
    {
        try
        {
            if (level != LogLevel.Debug && CheckOrbitMeshConnection())
            {
                var task = _connection!.InvokeAsync(EdgeServerMethodNames.WriteLog, message, level);
                if (awaitCompletion)
                {
                    task.GetAwaiter().GetResult();
                }
            }
        }
        catch (Exception ex)
        {
            if (!catchException)
            {
                throw;
            }
            Console.WriteLine("WriteLog error : " + ex);
        }
        Console.WriteLine("[{0}] {1}", level, message);
    }

    /// <summary>Stops the package: calls <see cref="IPackage.OnPreShutdown"/>, closes the connection,
    /// calls <see cref="IPackage.OnShutdown"/>, then releases the <see cref="Start(Type,string[])"/>
    /// call that's blocked waiting for this. Safe to call from within the package itself (e.g. in
    /// response to a fatal error) or from an external signal handler.</summary>
    public static void Shutdown()
    {
        if (!IsRunning)
        {
            return;
        }
        IsRunning = false;
        Package?.OnPreShutdown();
        if (_connection != null)
        {
            _ = _connection.StopAsync();
        }
        SettingsUpdated = null;
        TelemetryItemUpdated = null;
        LastTelemetryItemsReceived = null;
        ConnectionStateChanged = null;
        Settings = null;
        PackageDescriptor = null;
        Package?.OnShutdown();
        Package = null;
        ShutdownSignal.TrySetResult();
    }

    private static bool CheckOrbitMeshConnection(bool throwException = false)
    {
        if (_connection == null || !IsConnected)
        {
            if (throwException)
            {
                throw new InvalidOperationException("You aren't connected to the OrbitMesh !");
            }
            return false;
        }
        return true;
    }

    private static async Task WaitForSettingsAsync()
    {
        if (IsStandAlone)
        {
            WriteDebug("Loading settings from the local configuration file");
            Settings = new Dictionary<string, string>();
            LoadSettingsFromLocalConfigurationFile(Settings);
            CheckSettingsFromManifest(Settings);
        }
        else
        {
            WriteInfo("Waiting for settings ...");
            RequestSettings();
            while (IsRunning && Settings == null)
            {
                await Task.Delay(10);
            }
        }
    }

    private static void LoadSettingsFromLocalConfigurationFile(Dictionary<string, string> settings)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (!File.Exists(path))
            {
                return;
            }
            var local = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), ObjectConverter.DefaultOptions);
            if (local == null)
            {
                return;
            }
            var ignoreLocalValueKeys = PackageManifest?.Settings
                .Where(s => s.IgnoreLocalValue)
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
            foreach (var (key, value) in local)
            {
                if (!settings.ContainsKey(key) && (IsStandAlone || !ignoreLocalValueKeys.Contains(key)))
                {
                    if (!IsStandAlone)
                    {
                        WriteInfo("Loading the setting value from the local configuration file for the setting '{0}'", key);
                    }
                    settings.Add(key, value);
                }
            }
        }
        catch (Exception ex)
        {
            WriteError("Error while loading the local configuration file : {0}", ex.Message);
        }
    }

    private static void CheckSettingsFromManifest(Dictionary<string, string> settings)
    {
        try
        {
            if (PackageManifest == null)
            {
                return;
            }
            foreach (var setting in PackageManifest.Settings)
            {
                if (settings.ContainsKey(setting.Name))
                {
                    continue;
                }
                if (setting.IsRequired)
                {
                    WriteError("Setting '{0}' required but none set", setting.Name);
                }
                else if (!string.IsNullOrEmpty(setting.DefaultSettingValue) && !setting.IgnoreDefaultValue)
                {
                    WriteInfo("Loading the default value from the manifest for the setting '{0}'", setting.Name);
                    settings.Add(setting.Name, setting.DefaultSettingValue);
                }
                else
                {
                    WriteWarn("No value for the setting '{0}'", setting.Name);
                }
            }
        }
        catch (Exception ex)
        {
            WriteError("Error while checking settings from the manifest : {0}", ex.Message);
        }
    }

    private static object?[] GetMessageArguments(object? data, ParameterInfo[] parameters)
    {
        if (parameters.Length == 0)
        {
            return Array.Empty<object?>();
        }
        if (parameters.Length == 1)
        {
            var parameterType = parameters[0].ParameterType;
            if (parameterType == typeof(object))
            {
                return new[] { data };
            }
            try
            {
                return new[] { ObjectConverter.ConvertToObject(data, parameterType) };
            }
            catch when (parameters[0].IsOptional)
            {
                return new[] { GetDefaultValue(parameters[0]) };
            }
        }
        var array = new object?[parameters.Length];
        var elements = data is JsonElement { ValueKind: JsonValueKind.Array } je ? je.EnumerateArray().ToArray() : null;
        for (var i = 0; i < parameters.Length; i++)
        {
            object? element = elements != null ? (i < elements.Length ? elements[i] : null) : data;
            try
            {
                array[i] = ObjectConverter.ConvertToObject(element, parameters[i].ParameterType);
            }
            catch when (parameters[i].IsOptional)
            {
                array[i] = GetDefaultValue(parameters[i]);
            }
        }
        return array;
    }

    private static object? GetDefaultValue(ParameterInfo parameter)
    {
        var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        if (type.IsEnum && parameter.DefaultValue != null)
        {
            return Enum.Parse(type, parameter.DefaultValue.ToString()!);
        }
        return parameter.DefaultValue;
    }

    private static List<PackageDescriptor.MemberDescriptor> GetParametersDescription(ParameterInfo[] parametersInfo, List<PackageDescriptor.TypeDescriptor> knownTypes)
    {
        var list = new List<PackageDescriptor.MemberDescriptor>();
        foreach (var parameterInfo in parametersInfo)
        {
            var descriptor = GetMemberDescriptor(PackageDescriptor.MemberDescriptor.MemberType.MethodParameter, parameterInfo.Name!, parameterInfo.ParameterType, knownTypes);
            if (parameterInfo.IsOptional)
            {
                descriptor.IsOptional = true;
                descriptor.DefaultValue = parameterInfo.DefaultValue;
            }
            list.Add(descriptor);
        }
        return list;
    }

    private static string DescribeType(Type type, List<PackageDescriptor.TypeDescriptor> knownTypes)
    {
        if (!type.IsPrimitive && type != typeof(object) && type != typeof(string) && !knownTypes.Any(t => t.TypeFullname == type.FullName))
        {
            var descriptor = new PackageDescriptor.TypeDescriptor
            {
                IsArray = type.IsArray,
                IsEnum = type.IsEnum,
                IsGeneric = type.IsGenericType,
                TypeName = type.Name,
                TypeFullname = type.FullName ?? type.Name
            };
            XmlDocumentationReader.AddXmlComment(type, descriptor);
            knownTypes.Add(descriptor);
            if (descriptor.IsEnum)
            {
                descriptor.EnumValues = new List<PackageDescriptor.MemberDescriptor>();
                foreach (var name in type.GetEnumNames())
                {
                    var member = GetMemberDescriptor(PackageDescriptor.MemberDescriptor.MemberType.EnumValue, name, type, null);
                    member.DefaultValue = Enum.Parse(type, name).GetHashCode();
                    XmlDocumentationReader.AddEnumXmlComment(name, type, member);
                    descriptor.EnumValues.Add(member);
                }
            }
            else if (descriptor.IsArray)
            {
                descriptor.GenericParameters = new List<string> { DescribeType(type.GetElementType()!, knownTypes) };
            }
            else if (descriptor.IsGeneric)
            {
                descriptor.GenericParameters = type.GetGenericArguments().Select(t => DescribeType(t, knownTypes)).ToList();
            }
            else
            {
                descriptor.Properties = new List<PackageDescriptor.MemberDescriptor>();
                foreach (var propertyInfo in type.GetProperties())
                {
                    if (propertyInfo.GetCustomAttribute<JsonIgnoreAttribute>() == null)
                    {
                        var member = GetMemberDescriptor(PackageDescriptor.MemberDescriptor.MemberType.Property, propertyInfo.Name, propertyInfo.PropertyType, knownTypes);
                        XmlDocumentationReader.AddXmlComment(propertyInfo, member);
                        descriptor.Properties.Add(member);
                    }
                }
            }
        }
        return type.FullName ?? type.Name;
    }

    private static PackageDescriptor.MemberDescriptor GetMemberDescriptor(PackageDescriptor.MemberDescriptor.MemberType memberType, string memberName, Type parameterType, List<PackageDescriptor.TypeDescriptor>? knownTypes)
    {
        if (knownTypes != null)
        {
            DescribeType(parameterType, knownTypes);
        }
        return new PackageDescriptor.MemberDescriptor
        {
            Name = memberName,
            TypeName = parameterType.FullName ?? parameterType.Name,
            Type = memberType
        };
    }

    private static string GetTelemetryItemTypeName(object? telemetryItem)
    {
        var fullName = telemetryItem?.GetType().FullName;
        return fullName != null && !fullName.StartsWith("<>f__AnonymousType") ? fullName : string.Empty;
    }

    private static void AttachToParent(int parentProcessId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var parent = Process.GetProcessById(parentProcessId);
                while (!parent.HasExited)
                {
                    await Task.Delay(1000);
                    parent.Refresh();
                }
            }
            catch (ArgumentException)
            {
                // The parent process is already gone.
            }
            WriteWarn("The local edge '{0}' has stopped working. The package is closing now!", EdgeName ?? "?");
            Shutdown();
        });
    }
}
