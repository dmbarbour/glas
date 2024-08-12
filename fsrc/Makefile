
.PHONY: all glas install test watch-test clean

all: glas test install

glas:
	dotnet publish -c release -r linux-x64 --self-contained true -p:PublishTrimmed=true -o bin/ src/

test:
	dotnet test test/

watch-test:
	dotnet watch test --project test/ 

clean:
	rm -rf bin src/bin src/obj test/bin test/obj
