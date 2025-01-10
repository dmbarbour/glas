# Glas Command Line Interface

The glas executable generally takes the first command line argument as a switch to interpret the remainder of the arguments. There are a few options for running applications from different sources, and extensible ad-hoc ops for managing the configuration and so on.

        glas --run ConfiguredApp Args To App
        glas --script(.FileExt)* FileName Args To App
        glas --cmd(.FileExt)+ "Command Text" Args To App 
        glas --conf ConfigOp
        # runtime extensible

A simple syntactic sugar supports user-defined operations:

        glas opname Args
          # implicitly rewrites to
        glas --run cli.opname Args

My vision and intention is that end users mostly operate through user-defined operations. To fit the aesthetic, we must avoid cluttering the command line with runtime switches. Instead, we'll push most configuration options into application 'settings', a configuration file, and environment variables.

## Configuration

The glas executable starts by reading a configuration file. This file is typically specified by the `GLAS_CONF` environment variable, otherwise we'll try an OS-specific default, such as `"~/.config/glas/conf.g"` in Linux or `"%AppData%\glas\conf.g"` on Windows. If the configuration file does not exist, users must create one, perhaps interactively via `glas --config init` or external tools.

A typical user configuration will import a community or company configuration, then override a few definitions to integrate user preferences, projects, authorities, or resources. The community or company configuration is typically imported from DVCS and will define applications, tools, and reusable components through further imports. Each DVCS reference will specify a tag or version hash. Effectively, every glas configuration doubles as a package manager and virtual envrionment. This results in very large configurations, but potential problems are mitigated by lazy loading and caching.

Many runtime options should be application-specific. To support this, applications define an ad-hoc 'settings' method. These settings are not directly observed by the runtime. Instead, they should be queried and interpreted by the configuration when evaluating application-specific options. Intriguingly, some queries to 'settings' may be higher-order, receiving access to some methods to query the configuration. This allows for more flexible adaptation, where settings may depend on the configuration or runtime features.

In addition to application settings, the configuration can query OS environment variables or ask the runtime about its version or feature set. This enables a carefully designed configuration to be portable across multiple systems.

Configurable features may include:

* *persistent state* - where to store and share structured data
* *environment* - overrides the OS environment, and supports structured data
* *filesystem roots* - restrict an app to role-named folders
* *mirroring* - a distributed runtime for scaling or partitioning tolerance
* *logging* - whether and where to record progress and problems
* *profiling* - whether and where to record performance statistics
* *RPC registries* - where to publish and access RPC APIs
* *run mode* - how to interpret an application

The glas executable should not add runtime switches to the command line or directly observing environment variables other than `GLAS_CONF`. Instead, staged applications compute 'settings' of the next stage based on the arguments, and configurations may read some environment variables when evaluating runtime options.

## Run Mode and Staging

The glas executable may support multiple run modes, e.g. transaction loops, conventional `int main(args)`, and staged programs. Run mode is necessarily application-specific and should be configured based on application settings. 

A staged run mode should let users 'compile' command-line arguments into next-stage application, similar to a language module. This is convenient for problem-specific command-line languages, or where users wish to abstract and override 'settings' through the command line. To simplify caching, staged applications should support staged arguments, e.g. use '--' to divide arguments, passing everything after to the next-stage app.

## Running Applications

The `--run`, `--cmd`, and `--script` operations each build and run an application, just from different sources. 

* `--run ConfiguredApp Args` - lookup definition ConfiguredApp in the configuration.
* `--cmd.FileExt ScriptText Args` - compile ScriptText as if a file with the given file extension. 
* `--script(.FileExt)? ScriptFile Args` - compile ScriptFile based on given extension, or actual if none given.

After recognizing the application, the system might analyze, optimize, and further compile it down to lower level code. These steps are subject to caching, allowing us to save work when the same application is run many times, even for scripts or commands.

## Command Shells and Interactive Development

Very long term, I envision users mostly 'living within' a live-coded glas system instead of using lots of glas commands on a command line. Instead of running specific applications on the command line, user actions would manipulate a live coding environment, adding and removing active 'applications' at runtime. This seems feasible via [REPL or Notebook interface](GlasNotebooks.md). 

## Built-in Tooling

The glas executable may be extended with useful built-in tools, insofar as they don't add much bloat. Some tools that might prove useful:

* `--conf` - inspect and debug the configuration, perhaps initialize one
* `--db` - query, watch, or edit persistent data
* `--cache` - manage storage used for persistent memoization and incremental compilation
* `--rpc` - inspect RPC registries, perhaps issue some RPC calls directly from command line
* `--app` - debug or manipulate running apps through runtime provided HTTP or RPC APIs

Integrating application-specific resources need some attention. It is awkward to specify an application at `--db`, especially in context of anonymous scripts or staged run mode. To mitigate this, we might add a little indirection, e.g. based on application settings we might select a database that is independently defined in the configuration. Then, one argument to `--db` might name the database. Of course, it would be even simpler to configure one database for all the apps.

## Bootstrap

Early implementations of the glas executable might support a limited subset of the effects API such as console IO, an in-memory database, and the HTTP interface. We can introduce a specialized run mode just for bootstrapping, perhaps restricted to writing a binary to stdout. Then, with suitable application definitions that compile 'glas' within the system, we could bootstrap.

    # build
    /usr/bin/glas --run glas-bin > ~/.glas/tmp/glas
    chmod +x ~/.glas/tmp/glas

    # verify
    ~/.glas/tmp/glas --run glas-bin | cmp ~/.glas/tmp/glas

    # install
    sudo mv ~/.glas/tmp/glas /usr/bin/glas

Of course, we should abstract over the install paths and such. Also, early bootstrap might output to C or LLVM or something that must be further compiled instead of directly to executable format. 
