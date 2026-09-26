.PHONY: test check run scenario godot-test gen fmt host-test host-publish

DOTNET_HOME := $(HOME)/.dotnet
ifneq ($(wildcard $(DOTNET_HOME)/dotnet),)
export DOTNET_ROOT := $(DOTNET_HOME)
export PATH := $(DOTNET_HOME):$(PATH)
DOTNET := $(DOTNET_HOME)/dotnet
else
DOTNET := dotnet
endif

.PHONY: asset-build asset-install

test:
	$(DOTNET) test sim/tests/Sim.Tests.csproj --nologo

check:
	_scripts/check.sh

gen:
	_scripts/gen-indexes.sh

fmt:
	$(DOTNET) format RTS.sln

asset-build:
	@test -n "$(ASSET_ID)" || (echo "Set ASSET_ID, e.g. make asset-build ASSET_ID=rifleman" >&2; exit 2)
	art/build-asset.sh "$(ASSET_ID)"

asset-install:
	@test -n "$(ASSET_ID)" || (echo "Set ASSET_ID, e.g. make asset-install ASSET_ID=rifleman" >&2; exit 2)
	art/build-asset.sh "$(ASSET_ID)" --install

scenario:
	$(DOTNET) run --project tools/scenario/Scenario.csproj -- $(S)

host-test:
	$(DOTNET) test tools/sim-host.tests/SimHost.Tests.csproj --nologo

host-publish:
	$(DOTNET) publish tools/sim-host/SimHost.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist/sim-host-linux-x64
	$(DOTNET) publish tools/sim-host/SimHost.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/sim-host-win-x64

run:
	godot --path game

godot-test:
	godot --path game --headless --import --quit
	godot --path game --headless --quit
	godot --path game --headless -- --smoke | grep -q "SMOKE PASS"
