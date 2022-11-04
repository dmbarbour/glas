# Glas Bootstrap

This implementation of the `glas` command-line tool is written in F#, using dotnet core with minimal external dependencies. This is intended as a bootstrap implementation and experimentation platform for early development of Glas, until Glas is ready to fully self host.

Most relevant material:

* [Glas CLI](../docs/GlasCLI.md) - design of the command line executable
* [Glas Object](../docs/GlasObject.md) - a serialized data representation, guides in-memory data representation and describes a useful pattern for finger-tree ropes. 
* [The g0 language](../glas-src/language-g0/README.md) - the bootstrap language, requires implementation within

This bootstrap implementation currently doesn't bother with stowage systems or most effects for command line apps.
