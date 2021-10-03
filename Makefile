
.PHONY: all glas install test watch-test

all: glas test install

glas:
	dotnet publish -c release -r linux-x64 src/

test:
	dotnet test test/

watch-test:
	dotnet watch test --project test/ 
