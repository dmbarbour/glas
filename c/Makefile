CC = gcc

CFLAGS_BASE = -Wall -Wextra -std=c11 -I api/ -fvisibility=hidden
DEBUG_CFLAGS = $(CFLAGS_BASE) -g -Og -DDEBUG
RELEASE_CFLAGS = $(CFLAGS_BASE) -O3 -DNDEBUG

# TBD: snaps or AppImage for glas

.PHONY: all debug release clean

# Default target: build the executable
all: release
debug: bin/debug/glas
release: bin/release/glas
clean:
	rm -rf build
	rm -rf bin

SOURCES = main.c lib.c
OBJECTS = $(SOURCES:.c=.o)
HEADERS = api/glas.h src/impl.h


bin/debug/glas: $(addprefix build/debug/,$(OBJECTS))
	@mkdir -p bin/debug
	$(CC) $(DEBUG_CFLAGS) $^ -o $@

build/debug/%.o: src/%.c $(HEADERS)
	@mkdir -p build/debug
	$(CC) $(DEBUG_CFLAGS) -c $< -o $@

bin/release/glas: $(addprefix build/release/,$(OBJECTS))
	@mkdir -p bin/release
	$(CC) $(RELEASE_CFLAGS) $^ -o $@

build/release/%.o: src/%.c $(HEADERS)
	@mkdir -p build/release
	$(CC) $(RELEASE_CFLAGS) -c $< -o $@


