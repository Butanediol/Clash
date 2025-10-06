# Localization Implementation

This document describes the localization support added to ClashXW.

## Overview

The application now supports multiple languages with English (default) and Simplified Chinese (简体中文) included. The localization system is based on .NET resource files (.resx) and provides:

- Automatic language detection based on system settings
- Manual language switching via UI menu
- Persistent language preference storage
- Support for easily adding new languages

## Architecture

### Components

1. **Resource Files** (`Resources/`)
   - `Strings.resx` - Default English translations
   - `Strings.zh-CN.resx` - Simplified Chinese translations
   - `Strings.Designer.cs` - Auto-generated strongly-typed resource class

2. **LocalizationManager** (`LocalizationManager.cs`)
   - Manages current language selection
   - Handles language switching
   - Persists user language preference in `%AppData%\ClashXW\settings.json`
   - Provides `GetString()` methods for retrieving localized strings

3. **UI Integration**
   - XAML uses `{x:Static res:Strings.Key}` bindings for static menu items
   - Code-behind uses `LocalizationManager.GetString()` for dynamic messages
   - Language menu allows runtime language switching

## Localized Strings

All user-facing strings have been localized including:

- Menu items (Mode, Configuration, Language, Exit, etc.)
- Mode options (Rule, Direct, Global)
- Actions (Set System Proxy, TUN Mode, etc.)
- Error messages and dialogs
- Status messages

## Language Detection

On first launch, the application:
1. Checks for saved language preference in settings
2. If none exists, detects system language
3. If system language starts with "zh", uses Simplified Chinese
4. Otherwise defaults to English

## Adding New Languages

To add a new language:

1. Create a new resource file: `Resources/Strings.[culture-code].resx`
   - Example: `Strings.fr-FR.resx` for French
   - Copy all entries from `Strings.resx` and translate the values

2. Update the project file to include the new resource:
   ```xml
   <EmbeddedResource Update="Resources\Strings.fr-FR.resx">
     <DependentUpon>Strings.resx</DependentUpon>
   </EmbeddedResource>
   ```

3. Add the language to the menu in `MainWindow.xaml.cs`:
   ```csharp
   var languages = new Dictionary<string, string>
   {
       { "en", "English" },
       { "zh-CN", "简体中文" },
       { "fr-FR", "Français" }  // Add this line
   };
   ```

4. Rebuild the application

## Usage

Users can change the language by:
1. Right-clicking the system tray icon
2. Selecting "Language" (or "语言" in Chinese)
3. Choosing their preferred language
4. The preference is saved and persists across restarts

## Technical Details

- Uses .NET's `ResourceManager` for string lookup
- `CultureInfo` handles culture-specific formatting
- Settings stored as JSON in `%AppData%\ClashXW\settings.json`
- Language changes trigger UI updates via event handlers
- Static bindings in XAML update automatically through resource system
- Dynamic strings are updated via the `LanguageChanged` event

## Current Languages

1. **English (en)** - Default language
2. **Simplified Chinese (zh-CN)** - 简体中文

All menu items, error messages, and user-facing text are translated in both languages.
