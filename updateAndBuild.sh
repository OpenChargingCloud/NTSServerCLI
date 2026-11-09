#!/bin/bash
#
# Pull everything and build it.

set -e

cd "$(dirname "$0")"

git pull --ff-only
git submodule update --init --recursive
git submodule foreach git checkout master
git submodule foreach git pull
# Nothing but .NET: there is no frontend to build yet.
#dotnet build NTSServerCLI.slnx --configuration Release
dotnet build NTSServerCLI.slnx
