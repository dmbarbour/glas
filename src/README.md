# Glas Bootstrap

This implementation of the `glas` command-line tool is written in F#, using dotnet core with minimal external dependencies. This is intended as a bootstrap implementation and experimentation platform for early development of Glas, until Glas is ready to fully self host.

Most relevant material:

* [Glas CLI](../docs/GlasCLI.md) - design of the command line executable
* [Glas Object](../docs/GlasObject.md) - serialized data representation, guides in-memory rep and rope representation, might eventually be used for caching.
* [The g0 language](../glas-src/language-g0/README.md) - the bootstrap language, has implementation within F#

This bootstrap implementation currently doesn't bother with stowage systems or most effects for command line apps.
