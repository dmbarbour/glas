# Glas Modules

This folder contains an initial set of global glas mdoules intended to support bootstrap and early system development. The global module search path for glas must be configured to include this folder. 

To configure the global module search path, set the GLAS_HOME environment variable (or use the default such as `~/.config/glas`) then edit GLAS_HOME/sources.txt to contain this folder. For example:

        # initial sources.txt
        # edit path based on where you're developing
        dir /home/dmbarbour/projects/glas/glas-src

I hope to eventually shift glas systems to mostly depend on networked repositories. But for now, the focus is filesystem-local module representation.
