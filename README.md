# Glas Language System

Glas is a programming language system designed for reflective, purely functional metaprogramming with a tower of languages.

## Glas Overview

Unlike most conventional programming languages, Glas does not have a fixed syntax, implementation hiding, or a runtime behavior model.

Instead of runtime behavior, a top-level Glas module may compute a binary executable that may be extracted to a file. The 'compiler' logic is thus entirely shifted into the Glas static evaluation and module system. 

The primary contribution of Glas is the design work for the module system, data model, static evaluation model, and parser model. The data model is very simple, consisting only of dictionaries, lists, and natural numbers. Programs for static evaluation and parsing are based on a combinatory logic to avoid the complications of encoding and identifying variables.

There is careful design work for software development at large scales, such as effective memoization, provenance tracking, or consistent error reporting by user-defined parsers.

See the [design doc](docs/GlasDesign.md) for more detail.

## Project Goals

This project has limited goals:

* define the Glas language system
* define standard Glas types
* define the Glas object notation
* bootstrap a command-line compiler

Desiderata include keeping this project small and largely self-contained.

*Aside:* Glas will leverage Nix or Guix package managers instead of defining a new one.

## Status of Language

Glas has been re-envisioned in April 2020, focusing on the GADT path instead of session types. So, at the moment, it's undergoing a major overhaul.
