# NCrunch Console Tool

[![NuGet](https://img.shields.io/nuget/v/NCrunch.ConsoleTool.svg)](https://nuget.org/packages/NCrunch.ConsoleTool) 

This script downloads the latest stable version from [the official site](https://www.ncrunch.net/download) and pushes it to nuget.org.
If you use [Paket](https://fsprojects.github.io/Paket) the console tool path is `./packages/NCrunch.ConsoleTool/tools/NCrunch.exe`.
Usage guide can be found [here](https://www.ncrunch.net/documentation/V3/guides_console-tool-usage).

## Script usage

Set `NUGET_API_KEY` environment variable and run `./fake.sh run`.
