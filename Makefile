
.PHONY: all glas install test watch-test

all: glas test install

glas:
	dotnet publish src/

test:
	dotnet test test/

watch-test:
	dotnet watch test --project test/ 

install: glas
	mkdir -p ~/.local/bin
	cp src/bin/Debug/net5.0/linux-x64/publish/Glas.dll ~/.local/bin
	cp src/bin/Debug/net5.0/linux-x64/publish/Glas ~/.local/bin/glas
