.PHONY: test check run scenario godot-test gen fmt

test:
	dotnet test sim/tests/Sim.Tests.csproj --nologo

check:
	_scripts/check.sh

gen:
	_scripts/gen-indexes.sh

fmt:
	dotnet format RTS.sln

scenario:
	dotnet run --project tools/scenario/Scenario.csproj -- $(S)

run:
	godot --path game

godot-test:
	godot --path game --headless --import --quit
	godot --path game --headless --quit
