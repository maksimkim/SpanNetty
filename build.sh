#!/usr/bin/env bash
##########################################################################
# This is the Fake bootstrapper script for Linux and OS X.
##########################################################################

# Define directories.
SCRIPT_DIR=$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )
TOOLS_DIR=$SCRIPT_DIR/tools
INCREMENTALIST_DIR=$TOOLS_DIR/incrementalist
INCREMENTALIST_EXE=$INCREMENTALIST_DIR/Incrementalist.Cmd.exe
FAKE_TOOL_PATH=$TOOLS_DIR/fake
DOTNET_EXE=$SCRIPT_DIR/.dotnet/dotnet
DOTNETCORE_VERSION=3.1.411
DOTNET_VERSION=5.0.302
DOTNET_INSTALLER_URL=https://dot.net/v1/dotnet-install.sh
DOTNET_CHANNEL=LTS
PROTOBUF_VERSION=3.4.0
INCREMENTALIST_VERSION=0.4.0

# Define default arguments.
TARGET="All"
CONFIGURATION="Debug"
VERBOSITY="verbose"
DRYRUN=
SCRIPT_ARGUMENTS=()

# Parse arguments.
for i in "$@"; do
    case $1 in
        -t|--target) TARGET="$2"; shift ;;
        -c|--configuration) CONFIGURATION="$2"; shift ;;
        -v|--verbosity) VERBOSITY="$2"; shift ;;
        -d|--dryrun) DRYRUN="-dryrun" ;;
        --) shift; SCRIPT_ARGUMENTS+=("$@"); break ;;
        *) SCRIPT_ARGUMENTS+=("$1") ;;
    esac
    shift
done

# Make sure the tools folder exist.
if [ ! -d "$TOOLS_DIR" ]; then
  mkdir "$TOOLS_DIR"
fi

###########################################################################
# INSTALL FAKE
###########################################################################

if [ ! -f "$FAKE_TOOL_PATH/fake" ]; then
    dotnet tool install fake-cli --version 6.1.4 --tool-path "$FAKE_TOOL_PATH"
    if [ $? -ne 0 ]; then
        echo "An error occurred while installing fake-cli."
        exit 1
    fi
fi

# Make sure that Fake has been installed.
if [ ! -f "$FAKE_TOOL_PATH/fake" ]; then
    echo "Could not find fake at '$FAKE_TOOL_PATH/fake'."
    exit 1
fi

###########################################################################
# INSTALL Incrementalist
###########################################################################
if [ ! -f "$INCREMENTALIST_EXE" ]; then
    dotnet tool install Incrementalist.Cmd --version $INCREMENTALIST_VERSION --tool-path "$INCREMENTALIST_DIR"
    if [ $? -ne 0 ]; then
        echo "Incrementalist already installed."
    fi
fi

###########################################################################
# RUN BUILD SCRIPT
###########################################################################

# Use first positional argument as target if provided
if [ ${#SCRIPT_ARGUMENTS[@]} -gt 0 ]; then
    TARGET="${SCRIPT_ARGUMENTS[0]}"
fi

# Start Fake
export configuration=$CONFIGURATION
exec "$FAKE_TOOL_PATH/fake" run build.fsx -t "$TARGET"
