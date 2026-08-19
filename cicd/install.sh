#!/bin/bash
# Fresh install of OrbitMesh.Server (+ optionally OrbitMesh.Edge) and the Console on a systemd Linux
# host (e.g. a Raspberry Pi). Not an update tool - Server/Edge self-update once running.
#
# Usage: sudo ./install.sh
#   Interactive by default (Enter accepts the default). Non-interactive (piped) uses the defaults
#   below without prompting, and never overwrites an existing server/edge/console directory.
#   Every setting can be pre-filled via environment variable (INSTALL_DIR, PORT, INSTALL_EDGE,
#   SETUP_SERVICES, SERVICE_USER, UPDATE_SERVER).
#
# Installs under /opt, so needs root - run with sudo.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "This installs under /opt and sets up systemd services - run it with sudo." >&2
    exit 1
fi

UPDATE_SERVER="${UPDATE_SERVER:-https://updates.orbitmesh.org}"
DEFAULT_INSTALL_DIR="/opt/orbitmesh"
DEFAULT_PORT=8088
DOTNET_CHANNEL="10.0"
DEFAULT_SERVICE_USER="${SUDO_USER:-$(whoami)}"

for tool in curl unzip; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "$tool is required." >&2
        exit 1
    fi
done

# Regex is enough here: the update server's JSON is flat and known (see ReleaseServerClient.cs).
json_field() {
    local json="$1" field="$2"
    if [[ $json =~ \"$field\"[[:space:]]*:[[:space:]]*\"([^\"]*)\" ]]; then
        printf '%s' "${BASH_REMATCH[1]}"
    fi
}

ask() {
    local prompt="$1" default="$2" answer=""
    if [ -t 0 ]; then
        read -r -p "$prompt [$default]: " answer || true
    fi
    echo "${answer:-$default}"
}

is_yes() { case "$1" in [oOyY]|[oO][uU][iI]|[yY][eE][sS]) return 0 ;; *) return 1 ;; esac; }

echo "Fresh OrbitMesh install"
echo "======================="
INSTALL_DIR=$(ask "Install directory" "${INSTALL_DIR:-$DEFAULT_INSTALL_DIR}")
PORT=$(ask "Server listen port" "${PORT:-$DEFAULT_PORT}")
INSTALL_EDGE_ANSWER=$(ask "Also install OrbitMesh.Edge on this machine? (y/n)" "${INSTALL_EDGE:-y}")
SETUP_SERVICES_ANSWER=$(ask "Set up systemd services (auto-start)? (y/n)" "${SETUP_SERVICES:-y}")
SERVICE_USER=$(ask "System user owning the install and running the services (not root)" "${SERVICE_USER:-$DEFAULT_SERVICE_USER}")

INSTALL_EDGE=false
is_yes "$INSTALL_EDGE_ANSWER" && INSTALL_EDGE=true
SETUP_SERVICES=false
is_yes "$SETUP_SERVICES_ANSWER" && SETUP_SERVICES=true

if ! id "$SERVICE_USER" >/dev/null 2>&1; then
    echo "User '$SERVICE_USER' does not exist on this machine." >&2
    exit 1
fi

detect_dotnet() {
    if command -v dotnet >/dev/null 2>&1; then
        command -v dotnet
        return
    fi
    for candidate in "/usr/share/dotnet/dotnet" "/usr/local/share/dotnet/dotnet" "$HOME/dotnet/dotnet"; do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return
        fi
    done
    echo ""
}

install_dotnet() {
    local installer
    installer=$(mktemp --suffix=.sh)
    curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "$installer"
    chmod +x "$installer"
    # >&2 so the installer's progress log doesn't end up inside this function's own $(...) capture.
    "$installer" --channel "$DOTNET_CHANNEL" --runtime aspnetcore --install-dir "/usr/share/dotnet" >&2
    rm -f "$installer"
    echo "/usr/share/dotnet/dotnet"
}

DOTNET_PATH=$(detect_dotnet)
if [ -z "$DOTNET_PATH" ]; then
    echo "dotnet not found (not on PATH, not in the usual locations)."
    INSTALL_DOTNET_ANSWER=$(ask "Install .NET $DOTNET_CHANNEL now via Microsoft's official script? (y/n)" "$([ -t 0 ] && echo y || echo n)")
    if is_yes "$INSTALL_DOTNET_ANSWER"; then
        DOTNET_PATH=$(install_dotnet)
    else
        echo "dotnet is required - install it and re-run this script." >&2
        exit 1
    fi
fi
echo "dotnet found: $DOTNET_PATH"

mkdir -p "$INSTALL_DIR"

# Returns 0 if installed, 1 if skipped (already present, unavailable, or declined).
fetch_component() {
    local slug="$1" subdir="$2" target="$INSTALL_DIR/$2"

    echo ""
    echo "==> $slug"

    if [ -e "$target" ]; then
        if [ -t 0 ]; then
            local answer
            read -r -p "    $target already exists - overwrite it? (y/N) " answer || true
            if ! is_yes "${answer:-n}"; then
                echo "    skipped (existing directory kept)."
                return 1
            fi
            rm -rf "$target"
        else
            echo "    $target already exists - skipped (non-interactive, never overwrites without confirmation)."
            return 1
        fi
    fi

    local latest_json
    if ! latest_json=$(curl -fsSL "$UPDATE_SERVER/api/$slug/latest.json" 2>/dev/null); then
        echo "    no response from the update server - skipped."
        return 1
    fi

    local version zip_url
    version=$(json_field "$latest_json" version)
    zip_url=$(json_field "$latest_json" zip_url)
    if [ -z "$version" ] || [ "$version" = "0" ] || [ -z "$zip_url" ]; then
        echo "    no version published yet - skipped."
        return 1
    fi
    echo "    version $version"

    # `if fetch_component ...; then` suspends errexit for this whole function call - curl/unzip
    # failures must be checked explicitly or this would report success regardless.
    local tmp_zip
    tmp_zip=$(mktemp --suffix=.zip)
    if ! curl -fsSL "$zip_url" -o "$tmp_zip"; then
        echo "    download failed ($zip_url) - skipped. Check that this file actually exists on the update server." >&2
        rm -f "$tmp_zip"
        return 1
    fi
    mkdir -p "$target"
    if ! unzip -q -o "$tmp_zip" -d "$target"; then
        echo "    extraction failed - skipped. The downloaded file wasn't a valid zip." >&2
        rm -f "$tmp_zip"
        rm -rf "$target"
        return 1
    fi
    rm -f "$tmp_zip"
    echo "    installed in $target"
    return 0
}

SERVER_INSTALLED=false
EDGE_INSTALLED=false
UPDATER_INSTALLED=false

if fetch_component orbitmesh-server server; then SERVER_INSTALLED=true; fi
if $INSTALL_EDGE; then
    if fetch_component orbitmesh-edge edge; then EDGE_INSTALLED=true; fi
fi
fetch_component orbitmesh-console console || true
# Shared by Server's and Edge's self-update (ResolveUpdaterPath looks for a sibling "updater" dir).
if fetch_component orbitmesh-updater updater; then UPDATER_INSTALLED=true; fi

# Distinct from SERVER_INSTALLED/EDGE_INSTALLED (fresh-fetch-only): reflects what's on disk either
# way, so the systemd unit/sudoers rule still get (re)written on a re-run that kept existing binaries.
SERVER_PRESENT=false
[ -d "$INSTALL_DIR/server" ] && SERVER_PRESENT=true
EDGE_PRESENT=false
if $INSTALL_EDGE; then
    [ -d "$INSTALL_DIR/edge" ] && EDGE_PRESENT=true
fi

# A release zip shouldn't contain appsettings.json, but never trust it either - always regenerate
# for a fresh install (see ServerSelfUpdater/StaticSiteUpdater).
if $SERVER_INSTALLED; then
    echo ""
    echo "==> server configuration"
    cat > "$INSTALL_DIR/server/appsettings.json" <<EOF
{
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }
  },
  "AllowedHosts": "*",
  "OrbitMesh": {
    "packagesRootDirectory": "packages",
    "listenUrls": [ "http://+:${PORT}" ],
    "fileServers": [
      {
        "enable": true,
        "localhostOnly": false,
        "path": "/console-next",
        "physicalPath": "../console",
        "updateProjectSlug": "orbitmesh-console",
        "preserveFiles": [],
        "isSpa": true
      }
    ],
    "allowedOrigins": [],
    "recoveryOptions": {
      "restartAfterFailure": true,
      "numberOfRetry": 3,
      "resetCounterAfterMinutes": 15,
      "restartPackageAfterSeconds": 30
    },
    "credentials": [],
    "edges": [],
    "variables": [],
    "updateOptions": {
      "serverUrl": "${UPDATE_SERVER}",
      "projectSlug": "orbitmesh-server",
      "token": null,
      "checkIntervalMinutes": 5,
      "publicKeys": [ "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAqNU78UjlPKzWjCHkllAI\nBimgx61ipo7mfb3ytLXd0sZZ9qTNs0IZBDsNY+Nm/tXAQvO0otEslMavj+sCUN6E\nt58wMRVVvqb29QVBQ5JlKkBQzeJTTM3tFADdbbJBq0Aer+wDo79ahTvojkaE8oTn\nlqT8A+YrUoO0cCu6AH9fOflg51v5jhkJTBNre2W1T2nFXgJ0WWRgMgjY6N7MZ56F\n+zwGn7fRPpMx/G7izvVtJ8HMBZjHNxFdXIB1ieVlRAxnm7k5hmziQ1hzTw0N1re0\naoAdc/x4FsYwqG2eaJuBtboAOVG3X8Q8zKimx3bdZEpMIxAAZ/KJpX0GXEygQbqJ\n6QIDAQAB\n-----END PUBLIC KEY-----" ],
      "serviceOrUnitName": "orbitmesh-server",
      "updaterExecutablePath": null
    },
    "nuGetFeeds": [
      { "name": "OrbitMesh", "serviceIndexUrl": "https://nuget.orbitmesh.org/feeds/OrbitMesh/v3/index.json", "enable": true }
    ]
  }
}
EOF
    echo "    appsettings.json written - no credentials preconfigured, the Console will offer to create the administrator on first access."
fi

if $EDGE_INSTALLED; then
    echo ""
    echo "==> edge configuration"
    cat > "$INSTALL_DIR/edge/appsettings.json" <<EOF
{
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.Hosting.Lifetime": "Information" }
  },
  "Edge": {
    "OrbitMeshServerUri": "http://localhost:${PORT}",
    "OrbitMeshAccessKey": "",
    "LocalPackagesDirectory": "Packages",
    "EdgeName": null,
    "ReportPackageUsageIntervalMs": 1000,
    "ShutdownPackageTimeoutMs": 10000,
    "UpdateOptions": {
      "serverUrl": "${UPDATE_SERVER}",
      "projectSlug": "orbitmesh-edge",
      "token": null,
      "checkIntervalMinutes": 60,
      "publicKeys": [ "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAqNU78UjlPKzWjCHkllAI\nBimgx61ipo7mfb3ytLXd0sZZ9qTNs0IZBDsNY+Nm/tXAQvO0otEslMavj+sCUN6E\nt58wMRVVvqb29QVBQ5JlKkBQzeJTTM3tFADdbbJBq0Aer+wDo79ahTvojkaE8oTn\nlqT8A+YrUoO0cCu6AH9fOflg51v5jhkJTBNre2W1T2nFXgJ0WWRgMgjY6N7MZ56F\n+zwGn7fRPpMx/G7izvVtJ8HMBZjHNxFdXIB1ieVlRAxnm7k5hmziQ1hzTw0N1re0\naoAdc/x4FsYwqG2eaJuBtboAOVG3X8Q8zKimx3bdZEpMIxAAZ/KJpX0GXEygQbqJ\n6QIDAQAB\n-----END PUBLIC KEY-----" ],
      "serviceOrUnitName": "orbitmesh-edge",
      "updaterExecutablePath": null
    }
  }
}
EOF
    echo "    appsettings.json written - no AccessKey yet, approve this Edge from the Console once the server is up (see below)."
fi

# Everything above was written as root (needed for /opt) - the service runs as SERVICE_USER and
# needs to write its own directory at runtime (config, packages/, self-update staging - a SIBLING
# of server/edge, so INSTALL_DIR itself needs the chown, not just the subdirectories).
echo ""
echo "==> permissions ($SERVICE_USER)"
chown -R "$SERVICE_USER" "$INSTALL_DIR"
echo "    $INSTALL_DIR now owned by $SERVICE_USER"

write_systemd_unit() {
    local name="$1" dll="$2" workdir="$3"
    tee "/etc/systemd/system/${name}.service" > /dev/null <<EOF
[Unit]
Description=${name}
After=network.target

[Service]
Type=notify
ExecStart=${DOTNET_PATH} ${dll}
WorkingDirectory=${workdir}
Restart=on-failure
RestartSec=5
User=${SERVICE_USER}
# libicu isn't always present (e.g. a fresh Raspberry Pi OS) and its package name varies by distro -
# invariant mode avoids needing it. Safe here: this codebase only does ordinal string comparisons.
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
# Default (control-group) would also kill OrbitMesh.Updater, spawned as this process's own child
# for the self-update handoff - "process" only signals the main PID.
KillMode=process

[Install]
WantedBy=multi-user.target
EOF
}

# systemctl start/stop is normally root-only, which breaks OrbitMesh.Updater's restart step
# (SERVICE_USER is non-root). Sudoers over polkit: polkit isn't reliably present (missing on
# minimal images and inside WSL).
write_sudoers_rule() {
    if [ "$SERVICE_USER" = "root" ]; then
        return
    fi
    # readlink -f: sudo resolves via its own secure_path, which can differ from root's $PATH at
    # install time (e.g. /bin/systemctl vs /usr/bin/systemctl) - sudoers matches the path literally.
    local systemctl_path
    systemctl_path=$(readlink -f "$(command -v systemctl)")
    local rules_file="/etc/sudoers.d/orbitmesh"
    : > "$rules_file"
    if $SERVER_PRESENT; then
        echo "$SERVICE_USER ALL=(root) NOPASSWD: ${systemctl_path} start orbitmesh-server, ${systemctl_path} stop orbitmesh-server" >> "$rules_file"
    fi
    if $EDGE_PRESENT; then
        echo "$SERVICE_USER ALL=(root) NOPASSWD: ${systemctl_path} start orbitmesh-edge, ${systemctl_path} stop orbitmesh-edge" >> "$rules_file"
    fi
    chmod 440 "$rules_file"
    if ! visudo -c -f "$rules_file" >/dev/null 2>&1; then
        rm -f "$rules_file"
        echo "    could not write a valid sudoers rule - self-update's restart step will need systemctl access set up by hand." >&2
        return
    fi
    echo "    sudoers rule written - $SERVICE_USER can restart its own service(s) without a password."
}

# Proves the rule above actually works, now, instead of only finding out mid self-update. Checks
# stderr for sudo's auth-failure wording, not the exit code - is-active itself exits non-zero for a
# merely-inactive unit.
verify_sudoers_rule() {
    local unit="$1"
    if [ "$SERVICE_USER" = "root" ]; then
        return
    fi
    local systemctl_path sudo_stderr
    systemctl_path=$(readlink -f "$(command -v systemctl)")
    sudo_stderr=$(su - "$SERVICE_USER" -c "sudo -n '$systemctl_path' is-active '$unit'" 2>&1 >/dev/null)
    if echo "$sudo_stderr" | grep -qi "password is required\|terminal is required"; then
        echo "    WARNING: $SERVICE_USER can't run 'sudo systemctl ... $unit' without a password -" >&2
        echo "    self-update's restart step will fail. Check /etc/sudoers.d/orbitmesh and 'sudo -l -U $SERVICE_USER'." >&2
    else
        echo "    verified: $SERVICE_USER can run 'sudo systemctl start/stop $unit' without a password."
    fi
}

if $SETUP_SERVICES; then
    write_sudoers_rule
    if $SERVER_PRESENT; then
        echo ""
        echo "==> orbitmesh-server systemd service"
        write_systemd_unit orbitmesh-server "$INSTALL_DIR/server/OrbitMesh.Server.dll" "$INSTALL_DIR/server"
        systemctl daemon-reload
        systemctl enable --now orbitmesh-server
        echo "    installed and started."
        verify_sudoers_rule orbitmesh-server
    fi
    if $EDGE_PRESENT; then
        echo ""
        echo "==> orbitmesh-edge systemd service"
        write_systemd_unit orbitmesh-edge "$INSTALL_DIR/edge/OrbitMesh.Edge.dll" "$INSTALL_DIR/edge"
        systemctl daemon-reload
        systemctl enable --now orbitmesh-edge
        echo "    installed and started (will show as pending in the Console until approved - see below)."
        verify_sudoers_rule orbitmesh-edge
    fi
fi

echo ""
echo "Done. Install directory: $INSTALL_DIR"
if $SERVER_INSTALLED; then
    echo ""
    echo "1. Open http://<this-machine>:${PORT}/console-next/ (trailing slash included) and create the"
    echo "   administrator account - no credentials are preconfigured."
fi
if $EDGE_INSTALLED; then
    echo "2. Go to Edges > Pending edges and approve this Edge - the AccessKey is applied automatically,"
    echo "   no manual configuration needed."
fi
if ! $UPDATER_INSTALLED; then
    echo ""
    echo "Note: orbitmesh-updater isn't published on this update server yet, so self-update won't work"
    echo "until it is - see cicd/release-updater.ps1."
fi
