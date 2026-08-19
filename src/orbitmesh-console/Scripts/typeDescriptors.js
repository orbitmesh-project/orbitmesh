import { store } from "./store.js";

// PackageDescriptor.MessageHandlerTypes/TelemetryItemTypes are flat lists of every "interesting" type
// touched by that package's message handlers or telemetry items (see PackageHost's descriptor builder
// server-side) - a telemetry item's Type or a message parameter/response TypeName can point into either
// list, so both are searched.
export function findTypeDescriptor(packageName, typeName) {
    if (!typeName) {
        return null;
    }
    const descriptor = store.packagesDescriptors[packageName];
    if (!descriptor) {
        return null;
    }
    const match = (list) => (list || []).find((t) => t.TypeFullname === typeName || t.TypeName === typeName);
    return match(descriptor.TelemetryItemTypes) || match(descriptor.MessageHandlerTypes) || null;
}

export function hasTypeDescriptor(packageName, typeName) {
    return findTypeDescriptor(packageName, typeName) != null;
}

// Generic types come through as raw reflection strings (System.Nullable`1[[System.Int32, System.Private.CoreLib,
// Version=10.0.0.0, ...]]) - strip assembly-qualification, keep just Outer<Inner, ...>.
export function prettyGenericType(input) {
    if (!input) {
        return input;
    }
    const match = /(.*)`\d*\[(.*)\]/.exec(input);
    if (!match) {
        return input;
    }
    const paramTypes = match[2].split("],[").map((p) => p.replace("[", "").replace("]", "").split(",")[0]);
    return `${match[1]}<${paramTypes.join(", ")}>`;
}
