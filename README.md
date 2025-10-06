# ClashXW

A Windows WPF application for managing Clash proxy with system tray integration.

## Features

- 🌍 **Multi-language Support** - English and Simplified Chinese (简体中文)
- 🎛️ **Mode Switching** - Rule, Direct, and Global modes
- 🔌 **System Proxy Management** - Easy toggle for system-wide proxy
- 🚇 **TUN Mode Support** - Network-level proxy via TUN interface
- 📊 **Dashboard Integration** - Quick access to web dashboard
- ⚙️ **Configuration Management** - Multiple config file support
- 🔑 **Keyboard Shortcuts** - Quick access via hotkeys

## Language Support

The application supports multiple languages and automatically detects your system language:
- **English** - Default language
- **简体中文** - Simplified Chinese

You can change the language at runtime via the system tray menu: **Language** / **语言**

Language preference is saved and persists across application restarts.

For more details on localization, see [LOCALIZATION.md](LOCALIZATION.md).

## Development

### Prerequisites

-   [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Building

To build the project for debugging:

```sh
dotnet build
```

### Publishing

To create a release build:

```sh
dotnet publish --configuration Release
```

The self-contained application will be located in the `bin/Release/net8.0-windows/publish/` directory.