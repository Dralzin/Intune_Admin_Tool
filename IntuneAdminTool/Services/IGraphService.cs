namespace IntuneAdminTool.Services;

using IntuneAdminTool.ViewModels;
using Microsoft.Graph.Models;

public interface IGraphService
{
    void ResetClient();
    Task<List<ManagedDevice>> GetManagedDevicesAsync();
    Task<ManagedDevice?> GetDeviceAsync(string deviceId);
    Task UpdateManagedDeviceAsync(string deviceId, ManagedDevice device);
    Task<List<DeviceCategory>> GetDeviceCategoriesAsync();
    Task SetDeviceCategoryAsync(string managedDeviceId, string categoryId);
    Task<List<DeviceConfiguration>> GetDeviceConfigurationsAsync();
    Task<List<AdministrativeTemplateProfile>> GetAdministrativeTemplatesAsync();
    Task<List<SettingsCatalogProfile>> GetSettingsCatalogPoliciesAsync();
    Task<List<EndpointSecurityProfile>> GetEndpointSecurityPoliciesAsync();
    Task<List<PolicySetting>> GetDeviceConfigurationSettingsAsync(string configId);
    Task<List<PolicySetting>> GetAdministrativeTemplateSettingsAsync(string configId);
    Task<List<PolicySetting>> GetSettingsCatalogSettingsAsync(string policyId);
    Task<List<PolicySetting>> GetEndpointSecuritySettingsAsync(string intentId);
    Task<List<ProfileAssignment>> GetProfileAssignmentsAsync(string profileId, ConfigProfileSource source);
    Task<List<AutopilotDeviceItem>> GetAutopilotDevicesAsync();
    Task<List<AutopilotProfileItem>> GetAutopilotDeploymentProfilesAsync();
    Task<List<AutopilotEspItem>> GetEnrollmentStatusPagesAsync();
    Task<List<ProfileAssignment>> GetAutopilotProfileAssignmentsAsync(string profileId);
    Task<List<ProfileAssignment>> GetEspAssignmentsAsync(string espId);
    Task<List<WindowsUpdateRing>> GetWindowsUpdateRingsAsync();
    Task<List<WindowsFeatureUpdate>> GetWindowsFeatureUpdatesAsync();
    Task<List<WindowsDriverUpdate>> GetWindowsDriverUpdatesAsync();
    Task<List<DetectedAppDevice>> GetDetectedAppDevicesAsync(string appName);
    Task<List<AutopilotPrepPolicyItem>> GetDevicePreparationPoliciesAsync();
    Task<List<MobileApp>> GetMobileAppsAsync();
    Task<List<AppAssignmentInfo>> GetMobileAppAssignmentInfoAsync();
    Task<byte[]?> GetMobileAppLogoAsync(string appId);
    Task<List<ProfileAssignment>> GetMobileAppAssignmentsAsync(string appId);
    Task<List<User>> GetUsersAsync();
    Task<User?> GetUserByUpnAsync(string userPrincipalName);
    Task SetIntunePrimaryUserAsync(string managedDeviceId, string userId);
    Task RemoveIntunePrimaryUserAsync(string managedDeviceId);
    Task<string?> GetAzureAdDeviceIdAsync(string managedDeviceId);
    Task AddAzureAdDeviceOwnerAsync(string azureAdDeviceId, string userId);
    Task RemoveAzureAdDeviceOwnerAsync(string azureAdDeviceId, string ownerId);
    Task<List<DirectoryObject>> GetAzureAdDeviceOwnersAsync(string azureAdDeviceId);
    Task AddAzureAdDeviceUserAsync(string azureAdDeviceId, string userId);
    Task RemoveAzureAdDeviceUserAsync(string azureAdDeviceId, string userId);
    Task<List<DirectoryObject>> GetAzureAdDeviceUsersAsync(string azureAdDeviceId);
}

public record AdministrativeTemplateProfile(
    string? Id,
    string? DisplayName,
    string? Description,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime);

public record SettingsCatalogProfile(
    string? Id,
    string? Name,
    string? Description,
    string? Platforms,
    string? Technologies,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime);

public record EndpointSecurityProfile(
    string? Id,
    string? DisplayName,
    string? Description,
    string? TemplateId,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime);

public record PolicySetting(string Name, string? Value);

public record AutopilotDeviceItem(
    string? Id,
    string? SerialNumber,
    string? Manufacturer,
    string? Model,
    string? GroupTag,
    string? ManagedDeviceId,
    string? DisplayName,
    string? UserPrincipalName,
    string? DeploymentProfileAssignmentStatus,
    DateTimeOffset? LastContactedDateTime,
    DateTimeOffset? EnrollmentDateTime);

public record AutopilotProfileItem(
    string? Id,
    string? DisplayName,
    string? Description,
    bool? EnableWhiteGlove,
    bool? ExtractHardwareHash,
    string? DeviceNameTemplate,
    string? Language,
    string? OutOfBoxExperienceSettingsHidePrivacySettings,
    string? OutOfBoxExperienceSettingsHideEula,
    string? OutOfBoxExperienceSettingsUserType,
    string? OutOfBoxExperienceSettingsSkipKeyboardSelectionPage,
    string? DeviceType,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime);

public record AutopilotEspItem(
    string? Id,
    string? DisplayName,
    string? Description,
    bool? ShowInstallationProgress,
    bool? BlockDeviceSetupRetryByUser,
    bool? AllowDeviceResetOnInstallFailure,
    bool? AllowLogCollectionOnInstallFailure,
    bool? AllowNonBlockingAppInstallation,
    bool? TrackInstallProgressForAutopilotOnly,
    bool? DisableUserStatusTrackingAfterFirstUser,
    string? CustomErrorMessage,
    int? InstallProgressTimeoutInMinutes,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime);

public record AutopilotPrepPolicyItem(
    string? Id,
    string? DisplayName,
    string? Description,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime);

public record ProfileAssignment(
    string? GroupId,
    string? GroupDisplayName,
    string? AssignmentType);

public record AppAssignmentInfo(string? Id, bool IsAssigned, string? OdataType, string? PackageIdentifier);

public record WindowsUpdateRing(
    string? Id,
    string? DisplayName,
    string? Description,
    int? QualityUpdatesDeferralPeriodInDays,
    int? FeatureUpdatesDeferralPeriodInDays,
    bool? AutomaticUpdateMode,
    string? BusinessReadyUpdatesOnly,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime);

public record WindowsFeatureUpdate(
    string? Id,
    string? DisplayName,
    string? Description,
    string? FeatureUpdateVersion,
    string? RolloutSettings,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime);

public record WindowsDriverUpdate(
    string? Id,
    string? DisplayName,
    string? Description,
    string? ApprovalType,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime);

public record DetectedAppDevice(
    string? DeviceName,
    string? DeviceId,
    string? AppVersion,
    string? AppDisplayName);
