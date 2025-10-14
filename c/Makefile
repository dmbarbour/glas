CC = gcc

CFLAGS_BASE = -Wall -Wextra -Werror -std=c11 -I api/ -fvisibility=hidden
LINK_FLAGS = -lpthreads
DEBUG_CFLAGS = $(CFLAGS_BASE) -g -Og -DDEBUG
RELEASE_CFLAGS = $(CFLAGS_BASE) -O3 -DNDEBUG

.PHONY: all debug release clean

all: release
debug: bin/debug/glas
release: bin/release/glas
clean:
	rm -rf build
	rm -rf bin

SOURCES = main.c glas.c
OBJECTS = $(SOURCES:.c=.o)
EXTRA_DEPS = api/glas.h src/glas_internal.h Makefile 

bin/debug/glas: $(addprefix build/debug/,$(OBJECTS))
	@mkdir -p bin/debug
	$(CC) $(DEBUG_CFLAGS) $^ -o $@

build/debug/%.o: src/%.c $(EXTRA_DEPS)
	@mkdir -p build/debug
	$(CC) $(DEBUG_CFLAGS) -c $< -o $@

bin/release/glas: $(addprefix build/release/,$(OBJECTS))
	@mkdir -p bin/release
	$(CC) $(RELEASE_CFLAGS) $^ -o $@

build/release/%.o: src/%.c $(EXTRA_DEPS)
	@mkdir -p build/release
	$(CC) $(RELEASE_CFLAGS) -c $< -o $@


