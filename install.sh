#!/bin/bash
set -e

REPO="iOfficeAI/OfficeCli"
BINARY_NAME="officecli"

# Detect platform
OS=$(uname -s | tr '[:upper:]' '[:lower:]')
ARCH=$(uname -m)

case "$OS" in
    darwin)
        case "$ARCH" in
            arm64) ASSET="officecli-mac-arm64" ;;
            x86_64) ASSET="officecli-mac-x64" ;;
            *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
        esac
        ;;
    linux)
        # Detect musl libc (Alpine, etc.)
        LIBC="gnu"
        if command -v ldd >/dev/null 2>&1 && ldd --version 2>&1 | grep -qi musl; then
            LIBC="musl"
        elif [ -f /etc/alpine-release ]; then
            LIBC="musl"
        fi
        case "$ARCH" in
            x86_64)
                if [ "$LIBC" = "musl" ]; then
                    ASSET="officecli-linux-alpine-x64"
                else
                    ASSET="officecli-linux-x64"
                fi
                ;;
            aarch64|arm64)
                if [ "$LIBC" = "musl" ]; then
                    ASSET="officecli-linux-alpine-arm64"
                else
                    ASSET="officecli-linux-arm64"
                fi
                ;;
            *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
        esac
        ;;
    *)
        echo "Unsupported OS: $OS"
        echo "For Windows, download from: https://github.com/$REPO/releases"
        exit 1
        ;;
esac

SOURCE=""

# Step 1: Try downloading from GitHub
DOWNLOAD_URL="https://github.com/$REPO/releases/latest/download/$ASSET"
CHECKSUM_URL="https://github.com/$REPO/releases/latest/download/SHA256SUMS"
echo "Downloading OfficeCli ($ASSET)..."
if curl -fsSL "$DOWNLOAD_URL" -o "/tmp/$BINARY_NAME" 2>/dev/null; then
    # Verify checksum if available
    CHECKSUM_OK=false
    if curl -fsSL "$CHECKSUM_URL" -o "/tmp/officecli-SHA256SUMS" 2>/dev/null; then
        EXPECTED=$(grep "$ASSET" "/tmp/officecli-SHA256SUMS" | awk '{print $1}')
        if [ -n "$EXPECTED" ]; then
            if command -v sha256sum >/dev/null 2>&1; then
                ACTUAL=$(sha256sum "/tmp/$BINARY_NAME" | awk '{print $1}')
            else
                ACTUAL=$(shasum -a 256 "/tmp/$BINARY_NAME" | awk '{print $1}')
            fi
            if [ "$EXPECTED" = "$ACTUAL" ]; then
                CHECKSUM_OK=true
                echo "Checksum verified."
            else
                echo "Checksum mismatch! Expected: $EXPECTED, Got: $ACTUAL"
                rm -f "/tmp/$BINARY_NAME" "/tmp/officecli-SHA256SUMS"
                exit 1
            fi
        fi
        rm -f "/tmp/officecli-SHA256SUMS"
    fi
    if [ "$CHECKSUM_OK" = false ]; then
        echo "Checksum file not available, skipping verification."
    fi
    chmod +x "/tmp/$BINARY_NAME"
    SOURCE="/tmp/$BINARY_NAME"
else
    echo "Download failed."
fi

# Step 2: Fallback to local files
if [ -z "$SOURCE" ]; then
    echo "Looking for local binary..."
    for candidate in "./$ASSET" "./$BINARY_NAME" "./bin/$ASSET" "./bin/$BINARY_NAME" "./bin/release/$ASSET" "./bin/release/$BINARY_NAME"; do
        if [ -f "$candidate" ]; then
            if [ ! -x "$candidate" ]; then
                chmod +x "$candidate"
            fi
            if "$candidate" --version >/dev/null 2>&1; then
                SOURCE="$candidate"
                echo "Found valid binary at $candidate"
                break
            fi
        fi
    done
fi

if [ -z "$SOURCE" ]; then
    echo "Error: Could not find a valid OfficeCli binary."
    echo "Download manually from: https://github.com/$REPO/releases"
    exit 1
fi

# Step 3: Install
EXISTING=$(command -v "$BINARY_NAME" 2>/dev/null || true)
if [ -n "$EXISTING" ]; then
    INSTALL_DIR=$(dirname "$EXISTING")
    echo "Found existing installation at $EXISTING, upgrading..."
else
    INSTALL_DIR="$HOME/.local/bin"
fi

mkdir -p "$INSTALL_DIR"
cp "$SOURCE" "$INSTALL_DIR/$BINARY_NAME"
chmod +x "$INSTALL_DIR/$BINARY_NAME"

rm -f "/tmp/$BINARY_NAME"

echo "OfficeCli installed successfully!"
echo "Binary path: $INSTALL_DIR/$BINARY_NAME"
echo "No environment or agent configuration has been changed."
echo "Optional next step: $INSTALL_DIR/$BINARY_NAME setup"

if [ -t 0 ] && [ -t 1 ]; then
    printf "Run optional setup now? [y/N]: "
    read -r RUN_SETUP
    case "$RUN_SETUP" in
        y|Y|yes|YES)
            "$INSTALL_DIR/$BINARY_NAME" setup
            ;;
        *)
            echo "Skipped setup. You can run '$INSTALL_DIR/$BINARY_NAME setup' later."
            ;;
    esac
else
    echo "Non-interactive install detected. Run '$INSTALL_DIR/$BINARY_NAME setup' later if you want PATH, skills, MCP, macOS compatibility tweaks, or auto-update."
fi
