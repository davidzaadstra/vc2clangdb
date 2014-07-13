vc2clangdb
==========

A command line tool to create a clang JSON compilation database (aka compile_commands.json) from a vcxproj.

# Example
Suppose you have a simple project with default settings in C:/Projects/test. Then
` vc2clangdb C:/Projects/test/test.vcxproj -i C:/Projects/test/Debug
will create a compile_commands.json in the project directory.

# How does this work / Limitations
This tool works by parsing the tlog intermediate files which include the compilation commands for your project's files. This tool does not (yet) parse the vcxproj, i.e. it has the following limitations:
* You either need the latest intermediate files, or you need to have Visual Studio installed. In the latter case, this tool will build your project
* You need to manually pass the intermediate directory (If you pass a non-existing one, the project will be rebuilt there)
* You need to manually pass in Configuration and Platform for build (just like Visual Studio would have to)

# Other limitations
* Currently only preprocessor definitions and include paths get converted to the clang format
* Only Visual Studio 2013 is supported right now, and I have only tested Express so far

# What this is good for
You need JSON compilation databases to use clang tools (e.g. clang_modernize) or any tool written using LibTooling
