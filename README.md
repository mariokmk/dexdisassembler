dedex
=====

Is a command line tool for disassembling Android DEX files.

Usage
-----

Type "mono dedex.exe -d all <path to a DEX or APK file>". This command will disassemble the supplied DEX file and write the output to stdout. dedex will use the default language to display the output. See the help file for other supported languages.

Use the -c to limit the classes displayed. For example, if you want to see all classes with Installer in the name use 'mono dedex.exe -c "*Installer*" <DEX>".

See the dex.net project for a description of the output languages.

Development
-----------

dedex uses the dex.net library which is configured as a git submodule. You must initialize the submodules before compiling dedex.

License
-------

Dex.NET is provided under the [Apache 2.0 License](http://www.apache.org/licenses/LICENSE-2.0)

