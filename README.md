# TET.NET - Blazor Edition

Game Boy style Tetris, Blazor WebAssembly (.NET 8). No server code, runs fully in the browser.

## Run
    dotnet run
then open the URL it prints (usually http://localhost:5000).

## Publish as static site
    dotnet publish -c Release
Serve `bin/Release/net8.0/publish/wwwroot` from any static host.

Notes: high scores and key bindings are saved in browser localStorage.
The "Press Start 2P" font loads from Google Fonts (falls back to a monospace font offline).
