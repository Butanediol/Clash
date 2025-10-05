# ClashXW

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