# Intune Admin Tool

A WPF desktop application built on .NET 8 for managing Microsoft Intune environments through the Microsoft Graph API.

## Features

### Device Management
- View and manage enrolled devices with filtering by OS
- Device details including user, OS, compliance state, serial number, model, manufacturer, managed by, and device category
- Edit device properties: name, ownership, device category, and notes
- Assign/remove device owners and registered users
- Stale device detection (30+ days since last sync)

### App Management
- Browse deployed applications with search and filtering
- Filter by **OS** (Windows, iOS/iPadOS, macOS, Android, Cross-platform)
- Filter by **Type** (Win32, Microsoft Store (new), Microsoft 365, Web link, etc.)
- App types displayed as shown in the Intune admin center
- Correctly distinguishes Win32 apps from Microsoft Store (new) apps
- **Assigned** column showing assignment status per app
- App details panel with logo, assignment details, and group information

### Compliance Policies
- Monitor device compliance status
- View compliance state across all managed devices

### Configuration Profiles
- Review device configuration policies from all sources:
  - Device Configurations
  - Administrative Templates
  - Settings Catalog
  - Endpoint Security
- Platform filter with Windows/Windows10 normalization
- Profile type filter
- Policy settings viewer
- **Assignment details** showing which groups each profile is assigned to

### Autopilot
- View Autopilot device identities with search by serial number or device name
- Deployment profiles with detailed OOBE settings and assignment info
- Enrollment Status Pages with full configuration details and assignment info
- Safe loading — individual tab failures don't prevent other data from loading

### Windows Updates Dashboard
- **Update Rings** — Windows Update for Business policies with quality/feature deferral periods and servicing channel
- **Feature Updates** — Windows Feature Update deployment profiles with target versions and rollout settings
- **Driver Updates** — Windows Driver Update profiles with approval types

### Reports
- Built-in custom reports with CSV export and cancel support:
  - Device Compliance Summary
  - Non-Compliant Devices
  - Devices by OS
  - Devices by Manufacturer
  - Stale Devices (30+ days)
  - Unassigned Apps
  - Apps by Type
  - Windows Devices Not Encrypted (BitLocker)
  - BitLocker Encryption Status (all Windows devices)
  - Google Chrome Versions (with out-of-date detection)
  - Mozilla Firefox Versions (with out-of-date detection)
  - Microsoft Edge Versions (with out-of-date detection)
  - Microsoft 365 Apps Versions (with out-of-date detection)
- Cancel running reports at any time

### Settings
- Configure Client ID and Tenant ID (editable when signed out, locked when signed in)
- Shows active authentication details when signed in
- Accessible from the bottom of the navigation pane and from the login screen

## Tech Stack

| Layer | Technology |
|-------|------------|
| Framework | .NET 8 (Windows) |
| UI | WPF |
| Architecture | MVVM (CommunityToolkit.Mvvm) |
| DI | Microsoft.Extensions.DependencyInjection |
| Auth | Microsoft Identity Client (MSAL) |
| API | Microsoft Graph SDK |

## Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- An Azure AD app registration with appropriate Microsoft Graph permissions for Intune (optional — defaults to Microsoft Graph Command Line Tools app)

### Required Microsoft Graph API Permissions

The following **delegated** permissions are required for full functionality:

| Permission | Type | Used For |
|------------|------|----------|
| `DeviceManagementManagedDevices.ReadWrite.All` | Delegated | View and edit managed devices, set device owners |
| `DeviceManagementConfiguration.Read.All` | Delegated | Read configuration profiles, compliance policies, Windows Update policies |
| `DeviceManagementApps.Read.All` | Delegated | Read mobile apps, detected apps, app assignments |
| `DeviceManagementServiceConfig.Read.All` | Delegated | Read Autopilot device identities, deployment profiles, enrollment configurations |
| `Group.Read.All` | Delegated | Resolve group names for assignment details |
| `User.Read.All` | Delegated | Resolve user details for device owners and registered users |
| `Directory.Read.All` | Delegated | Read Azure AD device objects for owner management |

> **Note:** If using the default Microsoft Graph Command Line Tools app (`14d82eec-204b-4c2f-b7e8-296a70dab67e`), these permissions are requested via consent prompt on first sign-in. If using a custom app registration, ensure these permissions are granted in the Azure portal.

## Getting Started

1. Clone the repository:
   ```bash
   git clone https://github.com/Ryan-Stadelman_vfl/Intune_Admin_Tool.git
   ```
2. Open `Intune_Admin_Tool.sln` in Visual Studio 2022+ or later.
3. Build and run the application.
4. (Optional) Configure a custom Azure AD app registration via **Settings** before signing in.
5. Sign in with your organizational account.

## Authentication

The application uses MSAL with interactive browser authentication. After successful sign-in:
- The browser tab auto-closes
- The application window automatically comes to the foreground

By default, the app uses the well-known Microsoft Graph Command Line Tools client ID (`14d82eec-204b-4c2f-b7e8-296a70dab67e`). You can configure a custom Client ID and Tenant ID in Settings before signing in.

## Performance

- **Caching** — Device and user data cached for 5 minutes to reduce redundant API calls
- **Parallel fetching** — Independent API calls run concurrently via `Task.WhenAll`
- **Centralized pagination** — Shared helper for beta API paged requests
- **Selective `$select`** — Only required fields fetched from Graph to reduce payload size

## Project Structure

```
IntuneAdminTool/
├── Services/          # Auth and Graph API services
├── ViewModels/        # MVVM view models
│   ├── DevicesViewModel.cs
│   ├── AppsViewModel.cs
│   ├── ComplianceViewModel.cs
│   ├── ConfigurationViewModel.cs
│   ├── AutopilotViewModel.cs
│   ├── WindowsUpdatesViewModel.cs
│   ├── ReportsViewModel.cs
│   ├── SettingsViewModel.cs
│   └── MainViewModel.cs
├── Views/             # WPF XAML views
├── Converters/        # Value converters
├── Images/            # App icons and logos
├── Properties/        # Resources (ClientId, TenantId, Scopes, etc.)
├── App.xaml           # Application entry point
└── MainWindow.xaml    # Main application window with navigation
```

## License

See [LICENSE](LICENSE) for details.
