# ClashXW

## Development & Building

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Configuration

1.  **Clash Executable Path**: Before building, you must edit the `appsettings.json` file and set the `Clash:ExecutablePath` to the correct absolute path of your `clash-binary.exe`.

    ```json
    {
      "Clash": {
        "ExecutablePath": "C:\\path\\to\\your\\clash-binary.exe"
      }
    }
    ```

2.  **YAML Configurations**: The application stores its configuration files in `%AppData%\Roaming\ClashXW\Config\`. On first run, it will automatically create a default `config.yaml` in this directory if one doesn't exist.

### Building

To build the project, run the following command from the root directory:

```sh
dotnet build --configuration Release
```

The compiled application will be located in the `bin/Release/net8.0-windows/` directory.
