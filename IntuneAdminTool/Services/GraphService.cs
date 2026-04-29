namespace IntuneAdminTool.Services;

using System.Text.Json;
using Azure.Core;
using IntuneAdminTool.ViewModels;
using Microsoft.Graph;
using Microsoft.Graph.Models;

public class GraphService : IGraphService
{
    private readonly IAuthService _authService;
    private GraphServiceClient? _graphClient;

    // Caching for frequently-used data
    private List<ManagedDevice>? _cachedDevices;
    private List<User>? _cachedUsers;
    private Dictionary<string, string>? _groupLookup;
    private DateTime _devicesCacheTime;
    private DateTime _usersCacheTime;
    private DateTime _groupLookupCacheTime;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LongCacheDuration = TimeSpan.FromMinutes(15);

    // Throttling for parallel requests
    private static readonly SemaphoreSlim _throttle = new(4, 4);

    public GraphService(IAuthService authService)
    {
        _authService = authService;
    }

    public void ResetClient()
    {
        _graphClient = null;
        _cachedDevices = null;
        _cachedUsers = null;
        _groupLookup = null;
    }

    private GraphServiceClient GetClient()
    {
        if (_graphClient == null)
        {
            var credential = new DelegateTokenCredential(_authService);
            _graphClient = new GraphServiceClient(credential);
        }
        return _graphClient;
    }

    /// <summary>
    /// Helper to fetch all pages from a raw beta/v1.0 URL and parse JSON items.
    /// Includes retry logic for throttled (429) responses.
    /// </summary>
    private async Task<List<JsonElement>> FetchAllPagesRawAsync(string url)
    {
        var client = GetClient();
        var results = new List<JsonElement>();

        while (!string.IsNullOrEmpty(url))
        {
            await _throttle.WaitAsync().ConfigureAwait(false);
            try
            {
                var requestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
                {
                    HttpMethod = Microsoft.Kiota.Abstractions.Method.GET,
                    UrlTemplate = url
                };

                System.IO.Stream? stream = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        stream = await client.RequestAdapter.SendPrimitiveAsync<System.IO.Stream>(requestInfo).ConfigureAwait(false);
                        break;
                    }
                    catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == 429)
                    {
                        var delay = (attempt + 1) * 2;
                        await Task.Delay(TimeSpan.FromSeconds(delay)).ConfigureAwait(false);
                    }
                }

                if (stream == null) break;

                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                var root = doc.RootElement;

                if (root.TryGetProperty("value", out var valueArray))
                {
                    foreach (var item in valueArray.EnumerateArray())
                        results.Add(item.Clone());
                }

                url = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString()! : null!;
            }
            finally
            {
                _throttle.Release();
            }
        }

        return results;
    }

    private const int PageSize = 999;

    public async Task<List<ManagedDevice>> GetManagedDevicesAsync()
    {
        if (_cachedDevices != null && DateTime.UtcNow - _devicesCacheTime < CacheDuration)
            return _cachedDevices;

        var client = GetClient();
        var response = await client.DeviceManagement.ManagedDevices.GetAsync(config =>
        {
            config.QueryParameters.Top = PageSize;
            config.QueryParameters.Select = [
                "id", "deviceName", "userDisplayName", "userPrincipalName",
                "operatingSystem", "osVersion", "complianceState",
                "lastSyncDateTime", "managedDeviceOwnerType", "managementAgent",
                "emailAddress", "serialNumber", "model", "manufacturer",
                "deviceCategoryDisplayName", "isEncrypted"
            ];
        }).ConfigureAwait(false);

        var devices = new List<ManagedDevice>(response?.Value?.Count ?? 64);

        if (response?.Value != null)
            devices.AddRange(response.Value);

        while (response?.OdataNextLink != null)
        {
            response = await client.DeviceManagement.ManagedDevices
                .WithUrl(response.OdataNextLink)
                .GetAsync().ConfigureAwait(false);
            if (response?.Value != null)
                devices.AddRange(response.Value);
        }

        _cachedDevices = devices;
        _devicesCacheTime = DateTime.UtcNow;
        return devices;
    }

    public async Task<ManagedDevice?> GetDeviceAsync(string deviceId)
    {
        var client = GetClient();
        return await client.DeviceManagement.ManagedDevices[deviceId].GetAsync().ConfigureAwait(false);
    }

    public async Task UpdateManagedDeviceAsync(string deviceId, ManagedDevice device)
    {
        var client = GetClient();
        await client.DeviceManagement.ManagedDevices[deviceId].PatchAsync(device).ConfigureAwait(false);
    }

    public async Task<List<DeviceCategory>> GetDeviceCategoriesAsync()
    {
        var client = GetClient();
        var response = await client.DeviceManagement.DeviceCategories.GetAsync().ConfigureAwait(false);
        return response?.Value ?? [];
    }

    public async Task SetDeviceCategoryAsync(string managedDeviceId, string categoryId)
    {
        var client = GetClient();
        var body = new ReferenceCreate
        {
            OdataId = $"https://graph.microsoft.com/v1.0/deviceManagement/deviceCategories/{categoryId}"
        };
        var requestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
        {
            HttpMethod = Microsoft.Kiota.Abstractions.Method.PUT,
            UrlTemplate = $"https://graph.microsoft.com/v1.0/deviceManagement/managedDevices/{managedDeviceId}/deviceCategory/$ref"
        };
        requestInfo.SetContentFromParsable(client.RequestAdapter, "application/json", body);
        await client.RequestAdapter.SendNoContentAsync(requestInfo).ConfigureAwait(false);
    }

    public async Task<List<DeviceConfiguration>> GetDeviceConfigurationsAsync()
    {
        var client = GetClient();
        var response = await client.DeviceManagement.DeviceConfigurations.GetAsync(config =>
        {
            config.QueryParameters.Top = PageSize;
        }).ConfigureAwait(false);

        var configs = new List<DeviceConfiguration>(response?.Value?.Count ?? 64);
        if (response?.Value != null)
            configs.AddRange(response.Value);

        while (response?.OdataNextLink != null)
        {
            response = await client.DeviceManagement.DeviceConfigurations
                .WithUrl(response.OdataNextLink)
                .GetAsync().ConfigureAwait(false);
            if (response?.Value != null)
                configs.AddRange(response.Value);
        }

        return configs;
    }

    public async Task<List<PolicySetting>> GetDeviceConfigurationSettingsAsync(string configId)
    {
        var client = GetClient();
        // Fetch the full device configuration object — the typed properties contain the settings
        var requestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
        {
            HttpMethod = Microsoft.Kiota.Abstractions.Method.GET,
            UrlTemplate = $"https://graph.microsoft.com/v1.0/deviceManagement/deviceConfigurations/{configId}"
        };

        var stream = await client.RequestAdapter.SendPrimitiveAsync<System.IO.Stream>(requestInfo).ConfigureAwait(false);
        if (stream == null) return [];

        using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var root = doc.RootElement;

        var settings = new List<PolicySetting>();
        var skipProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "@odata.type", "@odata.context", "id", "displayName", "description",
            "createdDateTime", "lastModifiedDateTime", "version",
            "roleScopeTagIds", "supportsScopeTags", "deviceManagementApplicabilityRuleOsEdition",
            "deviceManagementApplicabilityRuleOsVersion", "deviceManagementApplicabilityRuleDeviceMode"
        };

        foreach (var prop in root.EnumerateObject())
        {
            if (skipProperties.Contains(prop.Name)) continue;

            var value = FormatJsonValue(prop.Value);
            if (value == null) continue;

            settings.Add(new PolicySetting(FormatSettingName(prop.Name), value));
        }

        return settings;
    }

    public async Task<List<PolicySetting>> GetAdministrativeTemplateSettingsAsync(string configId)
    {
        var client = GetClient();
        var results = new List<PolicySetting>();
        var url = $"https://graph.microsoft.com/beta/deviceManagement/groupPolicyConfigurations/{configId}/definitionValues?$expand=definition";

        while (!string.IsNullOrEmpty(url))
        {
            var requestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
            {
                HttpMethod = Microsoft.Kiota.Abstractions.Method.GET,
                UrlTemplate = url
            };

            var stream = await client.RequestAdapter.SendPrimitiveAsync<System.IO.Stream>(requestInfo).ConfigureAwait(false);
            if (stream == null) break;

            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var valueArray))
            {
                foreach (var item in valueArray.EnumerateArray())
                {
                    var enabled = item.TryGetProperty("enabled", out var en) ? en.GetBoolean() : (bool?)null;
                    string? displayName = null;
                    string? categoryPath = null;

                    if (item.TryGetProperty("definition", out var def))
                    {
                        displayName = def.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
                        categoryPath = def.TryGetProperty("categoryPath", out var cp) ? cp.GetString() : null;
                    }

                    var name = displayName ?? "Unknown setting";
                    if (!string.IsNullOrEmpty(categoryPath))
                        name = $"{categoryPath} \\ {name}";

                    var value = enabled switch
                    {
                        true => "Enabled",
                        false => "Disabled",
                        _ => "Not configured"
                    };

                    results.Add(new PolicySetting(name, value));
                }
            }

            url = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }

        return results;
    }

    private static string FormatSettingName(string name)
    {
        // camelCase -> "Camel Case"
        var spaced = System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
        return char.ToUpper(spaced[0]) + spaced[1..];
    }

    private static string? FormatJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.GetArrayLength() == 0 ? null : element.GetRawText(),
            JsonValueKind.Object => element.GetRawText(),
            _ => element.GetRawText()
        };
    }

    public async Task<List<AdministrativeTemplateProfile>> GetAdministrativeTemplatesAsync()
    {
        var items = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/groupPolicyConfigurations?$top=999");
        return items.Select(item => new AdministrativeTemplateProfile(
            item.TryGetProperty("id", out var id) ? id.GetString() : null,
            item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
            item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            item.TryGetProperty("createdDateTime", out var created) && created.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(created.GetString()!) : null,
            item.TryGetProperty("lastModifiedDateTime", out var modified) && modified.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(modified.GetString()!) : null
        )).ToList();
    }

    public async Task<List<SettingsCatalogProfile>> GetSettingsCatalogPoliciesAsync()
    {
        var items = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/configurationPolicies?$top=999");
        return items.Select(item => new SettingsCatalogProfile(
            item.TryGetProperty("id", out var id) ? id.GetString() : null,
            item.TryGetProperty("name", out var name) ? name.GetString() : null,
            item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            item.TryGetProperty("platforms", out var plat) ? plat.GetString() : null,
            item.TryGetProperty("technologies", out var tech) ? tech.GetString() : null,
            item.TryGetProperty("createdDateTime", out var created) && created.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(created.GetString()!) : null,
            item.TryGetProperty("lastModifiedDateTime", out var modified) && modified.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(modified.GetString()!) : null
        )).ToList();
    }

    public async Task<List<EndpointSecurityProfile>> GetEndpointSecurityPoliciesAsync()
    {
        var items = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/intents?$top=999");
        return items.Select(item => new EndpointSecurityProfile(
            item.TryGetProperty("id", out var id) ? id.GetString() : null,
            item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
            item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            item.TryGetProperty("templateId", out var tid) ? tid.GetString() : null,
            item.TryGetProperty("createdDateTime", out var created) && created.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(created.GetString()!) : null,
            item.TryGetProperty("lastModifiedDateTime", out var modified) && modified.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(modified.GetString()!) : null
        )).ToList();
    }

    public async Task<List<PolicySetting>> GetSettingsCatalogSettingsAsync(string policyId)
    {
        var client = GetClient();
        var results = new List<PolicySetting>();
        var url = $"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies/{policyId}/settings";

        while (!string.IsNullOrEmpty(url))
        {
            var requestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
            {
                HttpMethod = Microsoft.Kiota.Abstractions.Method.GET,
                UrlTemplate = url
            };

            var stream = await client.RequestAdapter.SendPrimitiveAsync<System.IO.Stream>(requestInfo).ConfigureAwait(false);
            if (stream == null) break;

            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var valueArray))
            {
                foreach (var item in valueArray.EnumerateArray())
                {
                    if (!item.TryGetProperty("settingInstance", out var instance)) continue;

                    var definitionId = instance.TryGetProperty("settingDefinitionId", out var defId) ? defId.GetString() : "Unknown";
                    var settingName = FormatSettingDefinitionId(definitionId);

                    string? settingValue = null;
                    if (instance.TryGetProperty("simpleSettingValue", out var simple))
                        settingValue = simple.TryGetProperty("value", out var v) ? v.GetRawText().Trim('"') : null;
                    else if (instance.TryGetProperty("choiceSettingValue", out var choice))
                        settingValue = choice.TryGetProperty("value", out var cv) ? FormatSettingDefinitionId(cv.GetString()) : null;
                    else if (instance.TryGetProperty("simpleSettingCollectionValue", out var coll))
                        settingValue = coll.GetRawText();
                    else
                        settingValue = "(complex value)";

                    results.Add(new PolicySetting(settingName ?? "Unknown", settingValue));
                }
            }

            url = root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }

        return results;
    }

    public async Task<List<PolicySetting>> GetEndpointSecuritySettingsAsync(string intentId)
    {
        var client = GetClient();
        var results = new List<PolicySetting>();

        // Get categories for the intent
        var catUrl = $"https://graph.microsoft.com/beta/deviceManagement/intents/{intentId}/categories";
        var catRequestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
        {
            HttpMethod = Microsoft.Kiota.Abstractions.Method.GET,
            UrlTemplate = catUrl
        };

        var catStream = await client.RequestAdapter.SendPrimitiveAsync<System.IO.Stream>(catRequestInfo).ConfigureAwait(false);
        if (catStream == null) return results;

        using var catDoc = await JsonDocument.ParseAsync(catStream).ConfigureAwait(false);
        if (!catDoc.RootElement.TryGetProperty("value", out var categories)) return results;

        foreach (var cat in categories.EnumerateArray())
        {
            var catId = cat.TryGetProperty("id", out var cid) ? cid.GetString() : null;
            if (catId == null) continue;

            // Get settings for this category
            var settingsUrl = $"https://graph.microsoft.com/beta/deviceManagement/intents/{intentId}/categories/{catId}/settings";
            var settingsRequestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
            {
                HttpMethod = Microsoft.Kiota.Abstractions.Method.GET,
                UrlTemplate = settingsUrl
            };

            var settingsStream = await client.RequestAdapter.SendPrimitiveAsync<System.IO.Stream>(settingsRequestInfo).ConfigureAwait(false);
            if (settingsStream == null) continue;

            using var settingsDoc = await JsonDocument.ParseAsync(settingsStream).ConfigureAwait(false);
            if (!settingsDoc.RootElement.TryGetProperty("value", out var settingsArray)) continue;

            foreach (var setting in settingsArray.EnumerateArray())
            {
                var defId = setting.TryGetProperty("definitionId", out var did) ? did.GetString() : "Unknown";
                var name = FormatSettingDefinitionId(defId);

                string? value = null;
                if (setting.TryGetProperty("value", out var val))
                    value = FormatJsonValue(val);
                else if (setting.TryGetProperty("valueJson", out var vj))
                    value = vj.GetString();

                results.Add(new PolicySetting(name ?? "Unknown", value));
            }
        }

        return results;
    }

    private static string? FormatSettingDefinitionId(string? definitionId)
    {
        if (string.IsNullOrEmpty(definitionId)) return null;
        // e.g. "device_vendor_msft_policy_config_defender_allowrealtimemonitoring" -> last segment readable
        var lastSegment = definitionId.Split('_').LastOrDefault() ?? definitionId;
        var spaced = System.Text.RegularExpressions.Regex.Replace(lastSegment, "([a-z])([A-Z])", "$1 $2");
        return char.ToUpper(spaced[0]) + spaced[1..];
    }

    public async Task<List<ProfileAssignment>> GetProfileAssignmentsAsync(string profileId, ConfigProfileSource source)
    {
        var url = source switch
        {
            ConfigProfileSource.DeviceConfiguration =>
                $"https://graph.microsoft.com/v1.0/deviceManagement/deviceConfigurations/{profileId}/assignments",
            ConfigProfileSource.AdministrativeTemplate =>
                $"https://graph.microsoft.com/beta/deviceManagement/groupPolicyConfigurations/{profileId}/assignments",
            ConfigProfileSource.SettingsCatalog =>
                $"https://graph.microsoft.com/beta/deviceManagement/configurationPolicies/{profileId}/assignments",
            ConfigProfileSource.EndpointSecurity =>
                $"https://graph.microsoft.com/beta/deviceManagement/intents/{profileId}/assignments",
            _ => null
        };

        if (url == null) return [];

        var items = await FetchAllPagesRawAsync(url);
        var assignments = new List<ProfileAssignment>();

        foreach (var item in items)
        {
            string? groupId = null;
            string? assignmentType = "Include";

            if (item.TryGetProperty("target", out var target))
            {
                var odataType = target.TryGetProperty("@odata.type", out var ot) ? ot.GetString() : null;
                groupId = target.TryGetProperty("groupId", out var gid) ? gid.GetString() : null;

                assignmentType = odataType switch
                {
                    "#microsoft.graph.exclusionGroupAssignmentTarget" => "Exclude",
                    "#microsoft.graph.allDevicesAssignmentTarget" => "All Devices",
                    "#microsoft.graph.allLicensedUsersAssignmentTarget" => "All Users",
                    _ => "Include"
                };

                if (assignmentType == "All Devices" || assignmentType == "All Users")
                {
                    assignments.Add(new ProfileAssignment(null, assignmentType, assignmentType));
                    continue;
                }
            }

            // Resolve group display name
            string? groupName = null;
            if (!string.IsNullOrEmpty(groupId))
            {
                try
                {
                    var client = GetClient();
                    var group = await client.Groups[groupId].GetAsync(config =>
                    {
                        config.QueryParameters.Select = ["displayName"];
                    }).ConfigureAwait(false);
                    groupName = group?.DisplayName;
                }
                catch
                {
                    groupName = groupId;
                }
            }

            assignments.Add(new ProfileAssignment(groupId, groupName ?? groupId ?? "Unknown", assignmentType));
        }

        return assignments;
    }

    public async Task<List<AutopilotDeviceItem>> GetAutopilotDevicesAsync()
    {
        var items = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/windowsAutopilotDeviceIdentities?$top=999");
        return items.Select(item => new AutopilotDeviceItem(
            item.TryGetProperty("id", out var id) ? id.GetString() : null,
            item.TryGetProperty("serialNumber", out var sn) ? sn.GetString() : null,
            item.TryGetProperty("manufacturer", out var mfr) ? mfr.GetString() : null,
            item.TryGetProperty("model", out var model) ? model.GetString() : null,
            item.TryGetProperty("groupTag", out var gt) ? gt.GetString() : null,
            item.TryGetProperty("managedDeviceId", out var mdid) ? mdid.GetString() : null,
            item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
            item.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() : null,
            item.TryGetProperty("deploymentProfileAssignmentStatus", out var dpas) ? dpas.GetString() : null,
            item.TryGetProperty("lastContactedDateTime", out var lcd) && lcd.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(lcd.GetString()!) : null,
            null
        )).ToList();
    }

    public async Task<List<AutopilotProfileItem>> GetAutopilotDeploymentProfilesAsync()
    {
        var items = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/windowsAutopilotDeploymentProfiles?$expand=assignments&$top=999");
        return items.Select(item =>
        {
            // Extract OOBE settings from nested object
            string? hidePrivacy = null, hideEula = null, userType = null, skipKeyboard = null;
            if (item.TryGetProperty("outOfBoxExperienceSettings", out var oobe) && oobe.ValueKind == JsonValueKind.Object)
            {
                hidePrivacy = oobe.TryGetProperty("hidePrivacySettings", out var hp) ? hp.ToString() : null;
                hideEula = oobe.TryGetProperty("hideEULA", out var he) ? he.ToString() : null;
                userType = oobe.TryGetProperty("userType", out var ut) ? ut.GetString() : null;
                skipKeyboard = oobe.TryGetProperty("skipKeyboardSelectionPage", out var sk) ? sk.ToString() : null;
            }
            // Also check outOfBoxExperienceSetting (singular, used by some profile types)
            if (hidePrivacy == null && item.TryGetProperty("outOfBoxExperienceSetting", out var oobe2) && oobe2.ValueKind == JsonValueKind.Object)
            {
                hidePrivacy = oobe2.TryGetProperty("hidePrivacySettings", out var hp2) ? hp2.ToString() : null;
                hideEula = oobe2.TryGetProperty("hideEULA", out var he2) ? he2.ToString() : null;
                userType = oobe2.TryGetProperty("userType", out var ut2) ? ut2.GetString() : null;
                skipKeyboard = oobe2.TryGetProperty("skipKeyboardSelectionPage", out var sk2) ? sk2.ToString() : null;
            }

            return new AutopilotProfileItem(
                item.TryGetProperty("id", out var id) ? id.GetString() : null,
                item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
                item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                item.TryGetProperty("enableWhiteGlove", out var ewg) && ewg.ValueKind == JsonValueKind.True,
                item.TryGetProperty("extractHardwareHash", out var ehh) && ehh.ValueKind == JsonValueKind.True,
                item.TryGetProperty("deviceNameTemplate", out var dnt) ? dnt.GetString() : null,
                item.TryGetProperty("language", out var lang) ? lang.GetString() : null,
                hidePrivacy,
                hideEula,
                userType,
                skipKeyboard,
                item.TryGetProperty("deviceType", out var dt) ? dt.GetString() : null,
                item.TryGetProperty("createdDateTime", out var created) && created.ValueKind != JsonValueKind.Null
                    ? DateTimeOffset.Parse(created.GetString()!) : null,
                item.TryGetProperty("lastModifiedDateTime", out var modified) && modified.ValueKind != JsonValueKind.Null
                    ? DateTimeOffset.Parse(modified.GetString()!) : null
            );
        }).ToList();
    }

    public async Task<List<AutopilotEspItem>> GetEnrollmentStatusPagesAsync()
    {
        var items = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/deviceEnrollmentConfigurations?$expand=assignments&$top=999");
        return items
            .Where(item => item.TryGetProperty("@odata.type", out var ot) &&
                           (ot.GetString()?.Contains("EnrollmentCompletionPage", StringComparison.OrdinalIgnoreCase) == true ||
                            ot.GetString()?.Contains("EnrollmentStatusPage", StringComparison.OrdinalIgnoreCase) == true))
            .Select(item => new AutopilotEspItem(
            item.TryGetProperty("id", out var id) ? id.GetString() : null,
            item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
            item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            item.TryGetProperty("showInstallationProgress", out var sip) && sip.ValueKind == JsonValueKind.True,
            item.TryGetProperty("blockDeviceSetupRetryByUser", out var bdr) && bdr.ValueKind == JsonValueKind.True,
            item.TryGetProperty("allowDeviceResetOnInstallFailure", out var adr) && adr.ValueKind == JsonValueKind.True,
            item.TryGetProperty("allowLogCollectionOnInstallFailure", out var alc) && alc.ValueKind == JsonValueKind.True,
            item.TryGetProperty("allowNonBlockingAppInstallation", out var anb) && anb.ValueKind == JsonValueKind.True,
            item.TryGetProperty("trackInstallProgressForAutopilotOnly", out var tip) && tip.ValueKind == JsonValueKind.True,
            item.TryGetProperty("disableUserStatusTrackingAfterFirstUser", out var dust) && dust.ValueKind == JsonValueKind.True,
            item.TryGetProperty("customErrorMessage", out var cem) ? cem.GetString() : null,
            item.TryGetProperty("installProgressTimeoutInMinutes", out var ipt) && ipt.ValueKind == JsonValueKind.Number
                ? ipt.GetInt32() : null,
            item.TryGetProperty("createdDateTime", out var created) && created.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(created.GetString()!) : null,
            item.TryGetProperty("lastModifiedDateTime", out var modified) && modified.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(modified.GetString()!) : null
        )).ToList();
    }

    public async Task<List<ProfileAssignment>> GetAutopilotProfileAssignmentsAsync(string profileId)
    {
        return await GetAssignmentsFromUrlAsync(
            $"https://graph.microsoft.com/beta/deviceManagement/windowsAutopilotDeploymentProfiles/{profileId}/assignments");
    }

    public async Task<List<ProfileAssignment>> GetEspAssignmentsAsync(string espId)
    {
        return await GetAssignmentsFromUrlAsync(
            $"https://graph.microsoft.com/beta/deviceManagement/deviceEnrollmentConfigurations/{espId}/assignments");
    }

    private async Task<List<ProfileAssignment>> GetAssignmentsFromUrlAsync(string url)
    {
        var items = await FetchAllPagesRawAsync(url);
        var assignments = new List<ProfileAssignment>();
        var groupIdsToResolve = new HashSet<string>();

        // First pass: collect all group IDs
        foreach (var item in items)
        {
            if (item.TryGetProperty("target", out var target))
            {
                var groupId = target.TryGetProperty("groupId", out var gid) ? gid.GetString() : null;
                if (!string.IsNullOrEmpty(groupId))
                    groupIdsToResolve.Add(groupId);
            }
        }

        // Resolve all group names via cached lookup
        var groupNames = await ResolveGroupNamesAsync(groupIdsToResolve);

        // Second pass: build assignments
        foreach (var item in items)
        {
            string? groupId = null;
            string? assignmentType = "Include";

            if (item.TryGetProperty("target", out var target))
            {
                var odataType = target.TryGetProperty("@odata.type", out var ot) ? ot.GetString() : null;
                groupId = target.TryGetProperty("groupId", out var gid) ? gid.GetString() : null;

                assignmentType = odataType switch
                {
                    "#microsoft.graph.exclusionGroupAssignmentTarget" => "Exclude",
                    "#microsoft.graph.allDevicesAssignmentTarget" => "All Devices",
                    "#microsoft.graph.allLicensedUsersAssignmentTarget" => "All Users",
                    _ => "Include"
                };

                if (assignmentType is "All Devices" or "All Users")
                {
                    assignments.Add(new ProfileAssignment(null, assignmentType, assignmentType));
                    continue;
                }
            }

            var groupName = !string.IsNullOrEmpty(groupId) && groupNames.TryGetValue(groupId, out var name)
                ? name : groupId ?? "Unknown";

            assignments.Add(new ProfileAssignment(groupId, groupName, assignmentType));
        }

        return assignments;
    }

    /// <summary>
    /// Resolves group IDs to display names using a cached lookup table.
    /// Fetches all groups once and caches for LongCacheDuration.
    /// </summary>
    private async Task<Dictionary<string, string>> ResolveGroupNamesAsync(IEnumerable<string> groupIds)
    {
        await EnsureGroupLookupCacheAsync();
        return _groupLookup!;
    }

    private async Task EnsureGroupLookupCacheAsync()
    {
        if (_groupLookup != null && DateTime.UtcNow - _groupLookupCacheTime < LongCacheDuration)
            return;

        var client = GetClient();
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var response = await client.Groups.GetAsync(config =>
        {
            config.QueryParameters.Top = PageSize;
            config.QueryParameters.Select = ["id", "displayName"];
        }).ConfigureAwait(false);

        if (response?.Value != null)
        {
            foreach (var g in response.Value)
            {
                if (g.Id != null && g.DisplayName != null)
                    lookup[g.Id] = g.DisplayName;
            }
        }

        while (response?.OdataNextLink != null)
        {
            response = await client.Groups
                .WithUrl(response.OdataNextLink)
                .GetAsync().ConfigureAwait(false);
            if (response?.Value != null)
            {
                foreach (var g in response.Value)
                {
                    if (g.Id != null && g.DisplayName != null)
                        lookup[g.Id] = g.DisplayName;
                }
            }
        }

        _groupLookup = lookup;
        _groupLookupCacheTime = DateTime.UtcNow;
    }

    public async Task<List<WindowsUpdateRing>> GetWindowsUpdateRingsAsync()
    {
        var items = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/deviceConfigurations?$filter=isof('microsoft.graph.windowsUpdateForBusinessConfiguration')&$top=999");
        if (items.Count == 0)
        {
            // Fallback: fetch all and filter client-side
            var allConfigs = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/deviceConfigurations?$top=999");
            items = allConfigs
                .Where(i => i.TryGetProperty("@odata.type", out var ot) &&
                            ot.GetString()?.Contains("windowsUpdateForBusiness", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        return items.Select(item => new WindowsUpdateRing(
            item.TryGetProperty("id", out var id) ? id.GetString() : null,
            item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
            item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            item.TryGetProperty("qualityUpdatesDeferralPeriodInDays", out var qd) && qd.ValueKind == JsonValueKind.Number ? qd.GetInt32() : null,
            item.TryGetProperty("featureUpdatesDeferralPeriodInDays", out var fd) && fd.ValueKind == JsonValueKind.Number ? fd.GetInt32() : null,
            item.TryGetProperty("automaticUpdateMode", out var aum) && aum.ValueKind != JsonValueKind.Null,
            item.TryGetProperty("businessReadyUpdatesOnly", out var bru) ? bru.GetString() : null,
            item.TryGetProperty("createdDateTime", out var created) && created.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(created.GetString()!) : null,
            item.TryGetProperty("lastModifiedDateTime", out var modified) && modified.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(modified.GetString()!) : null
        )).ToList();
    }

    public async Task<List<WindowsFeatureUpdate>> GetWindowsFeatureUpdatesAsync()
    {
        var items = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/windowsFeatureUpdateProfiles?$top=999");
        return items.Select(item => new WindowsFeatureUpdate(
            item.TryGetProperty("id", out var id) ? id.GetString() : null,
            item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
            item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            item.TryGetProperty("featureUpdateVersion", out var fuv) ? fuv.GetString() : null,
            item.TryGetProperty("rolloutSettings", out var rs) && rs.ValueKind == JsonValueKind.Object
                ? (rs.TryGetProperty("offerStartDateTimeInUTC", out var osd) ? osd.GetString() : null) : null,
            item.TryGetProperty("createdDateTime", out var created) && created.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(created.GetString()!) : null,
            item.TryGetProperty("lastModifiedDateTime", out var modified) && modified.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(modified.GetString()!) : null
        )).ToList();
    }

    public async Task<List<WindowsDriverUpdate>> GetWindowsDriverUpdatesAsync()
    {
        var items = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/windowsDriverUpdateProfiles?$top=999");
        return items.Select(item => new WindowsDriverUpdate(
            item.TryGetProperty("id", out var id) ? id.GetString() : null,
            item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
            item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            item.TryGetProperty("approvalType", out var at) ? at.GetString() : null,
            item.TryGetProperty("createdDateTime", out var created) && created.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(created.GetString()!) : null,
            item.TryGetProperty("lastModifiedDateTime", out var modified) && modified.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(modified.GetString()!) : null
        )).ToList();
    }

    public async Task<List<AppProtectionPolicyItem>> GetAppProtectionPoliciesAsync()
    {
        var results = new List<AppProtectionPolicyItem>();

        // iOS/iPadOS App Protection Policies
        try
        {
            var iosPolicies = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceAppManagement/iosManagedAppProtections?$top=999");
            results.AddRange(iosPolicies.Select(item => ParseAppProtectionPolicy(item, "iOS/iPadOS", "App Protection")));
        }
        catch { }

        // Android App Protection Policies
        try
        {
            var androidPolicies = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceAppManagement/androidManagedAppProtections?$top=999");
            results.AddRange(androidPolicies.Select(item => ParseAppProtectionPolicy(item, "Android", "App Protection")));
        }
        catch { }

        // Windows Information Protection (WIP) policies
        try
        {
            var wipPolicies = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceAppManagement/windowsInformationProtectionPolicies?$top=999");
            results.AddRange(wipPolicies.Select(item => ParseAppProtectionPolicy(item, "Windows", "Information Protection")));
        }
        catch { }

        // MDM Windows Information Protection policies
        try
        {
            var mdmWipPolicies = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceAppManagement/mdmWindowsInformationProtectionPolicies?$top=999");
            results.AddRange(mdmWipPolicies.Select(item => ParseAppProtectionPolicy(item, "Windows (MDM)", "Information Protection")));
        }
        catch { }

        return results;
    }

    private static AppProtectionPolicyItem ParseAppProtectionPolicy(System.Text.Json.JsonElement item, string platform, string policyType)
    {
        return new AppProtectionPolicyItem(
            item.TryGetProperty("id", out var id) ? id.GetString() : null,
            item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
            item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            platform,
            policyType,
            item.TryGetProperty("isAssigned", out var ia) && ia.ValueKind == JsonValueKind.True,
            item.TryGetProperty("pinRequired", out var pr) ? pr.ToString() : null,
            item.TryGetProperty("managedBrowser", out var mb) ? mb.GetString() : null,
            item.TryGetProperty("allowedDataStorageLocations", out var adsl) && adsl.ValueKind == JsonValueKind.Array
                ? string.Join(", ", adsl.EnumerateArray().Select(a => a.GetString())) : null,
            item.TryGetProperty("minimumRequiredOsVersion", out var minOs) ? minOs.GetString() : null,
            item.TryGetProperty("maximumRequiredOsVersion", out var maxOs) ? maxOs.GetString() : null,
            item.TryGetProperty("createdDateTime", out var created) && created.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(created.GetString()!) : null,
            item.TryGetProperty("lastModifiedDateTime", out var modified) && modified.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(modified.GetString()!) : null
        );
    }

    public async Task<List<ProfileAssignment>> GetAppProtectionPolicyAssignmentsAsync(string policyId, string policyType)
    {
        var endpoint = policyType switch
        {
            "iOS/iPadOS" => $"https://graph.microsoft.com/beta/deviceAppManagement/iosManagedAppProtections/{policyId}/assignments",
            "Android" => $"https://graph.microsoft.com/beta/deviceAppManagement/androidManagedAppProtections/{policyId}/assignments",
            "Windows" => $"https://graph.microsoft.com/beta/deviceAppManagement/windowsInformationProtectionPolicies/{policyId}/assignments",
            "Windows (MDM)" => $"https://graph.microsoft.com/beta/deviceAppManagement/mdmWindowsInformationProtectionPolicies/{policyId}/assignments",
            _ => null
        };

        if (endpoint == null) return [];
        return await GetAssignmentsFromUrlAsync(endpoint);
    }

    public async Task<List<DetectedAppDevice>> GetDetectedAppDevicesAsync(string appName)
    {
        // Get detected apps matching the name
        var encodedName = Uri.EscapeDataString(appName);
        var detectedApps = await FetchAllPagesRawAsync(
            $"https://graph.microsoft.com/beta/deviceManagement/detectedApps?$filter=contains(displayName,'{encodedName}')&$top=999");

        if (detectedApps.Count == 0)
        {
            // Fallback: fetch all and filter client-side
            detectedApps = await FetchAllPagesRawAsync(
                "https://graph.microsoft.com/beta/deviceManagement/detectedApps?$top=999");
            detectedApps = detectedApps
                .Where(a => a.TryGetProperty("displayName", out var dn) &&
                            dn.GetString()?.Contains(appName, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        var results = new List<DetectedAppDevice>();

        foreach (var app in detectedApps)
        {
            var appId = app.TryGetProperty("id", out var id) ? id.GetString() : null;
            var appDisplayName = app.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
            var appVersion = app.TryGetProperty("version", out var ver) ? ver.GetString() : null;

            if (appId == null) continue;

            // Get devices with this app installed
            var devices = await FetchAllPagesRawAsync(
                $"https://graph.microsoft.com/beta/deviceManagement/detectedApps/{appId}/managedDevices?$select=id,deviceName&$top=999");

            foreach (var device in devices)
            {
                results.Add(new DetectedAppDevice(
                    device.TryGetProperty("deviceName", out var devName) ? devName.GetString() : null,
                    device.TryGetProperty("id", out var devId) ? devId.GetString() : null,
                    appVersion,
                    appDisplayName
                ));
            }
        }

        return results;
    }

    public async Task<List<AutopilotPrepPolicyItem>> GetDevicePreparationPoliciesAsync()
    {
        // Try the dedicated endpoint first, fall back to configurationPolicies with devicePreparation filter
        var items = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/windowsAutopilotDevicePreparationPolicies?$top=999");
        if (items.Count == 0)
        {
            // Fallback: device preparation policies are also stored as configurationPolicies with templateFamily
            var allPolicies = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/configurationPolicies?$top=999&$filter=templateReference/templateFamily eq 'devicePreparation'");
            if (allPolicies.Count == 0)
            {
                // Second fallback without filter (filter may not be supported)
                allPolicies = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceManagement/configurationPolicies?$top=999");
                allPolicies = allPolicies
                    .Where(item => item.TryGetProperty("templateReference", out var tr) &&
                                   tr.ValueKind == JsonValueKind.Object &&
                                   tr.TryGetProperty("templateFamily", out var tf) &&
                                   tf.GetString()?.Equals("devicePreparation", StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
            }
            items = allPolicies;
        }

        return items.Select(item => new AutopilotPrepPolicyItem(
            item.TryGetProperty("id", out var id) ? id.GetString() : null,
            item.TryGetProperty("displayName", out var dn) ? dn.GetString() :
                (item.TryGetProperty("name", out var name) ? name.GetString() : null),
            item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            item.TryGetProperty("createdDateTime", out var created) && created.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(created.GetString()!) : null,
            item.TryGetProperty("lastModifiedDateTime", out var modified) && modified.ValueKind != JsonValueKind.Null
                ? DateTimeOffset.Parse(modified.GetString()!) : null
        )).ToList();
    }

    public async Task<List<MobileApp>> GetMobileAppsAsync()
    {
        var client = GetClient();
        var response = await client.DeviceAppManagement.MobileApps.GetAsync(config =>
        {
            config.QueryParameters.Top = PageSize;
        }).ConfigureAwait(false);

        var apps = new List<MobileApp>(response?.Value?.Count ?? 64);
        if (response?.Value != null)
            apps.AddRange(response.Value);

        while (response?.OdataNextLink != null)
        {
            response = await client.DeviceAppManagement.MobileApps
                .WithUrl(response.OdataNextLink)
                .GetAsync().ConfigureAwait(false);
            if (response?.Value != null)
                apps.AddRange(response.Value);
        }

        return apps;
    }

    public async Task<List<AppAssignmentInfo>> GetMobileAppAssignmentInfoAsync()
    {
        var items = await FetchAllPagesRawAsync("https://graph.microsoft.com/beta/deviceAppManagement/mobileApps?$top=999");
        return items.Select(item => new AppAssignmentInfo(
            item.TryGetProperty("id", out var id) ? id.GetString() : null,
            item.TryGetProperty("isAssigned", out var ia) && ia.ValueKind == System.Text.Json.JsonValueKind.True,
            item.TryGetProperty("@odata.type", out var ot) ? ot.GetString() : null,
            item.TryGetProperty("packageIdentifier", out var pi) ? pi.GetString() : null
        )).ToList();
    }

    public async Task<byte[]?> GetMobileAppLogoAsync(string appId)
    {
        var client = GetClient();
        var app = await client.DeviceAppManagement.MobileApps[appId].GetAsync(config =>
        {
            config.QueryParameters.Select = ["largeIcon"];
        }).ConfigureAwait(false);

        return app?.LargeIcon?.Value;
    }

    public async Task<List<ProfileAssignment>> GetMobileAppAssignmentsAsync(string appId)
    {
        var items = await FetchAllPagesRawAsync(
            $"https://graph.microsoft.com/v1.0/deviceAppManagement/mobileApps/{appId}/assignments");

        var assignments = new List<ProfileAssignment>();
        foreach (var item in items)
        {
            string? groupId = null;
            string? assignmentType = "Include";

            if (item.TryGetProperty("target", out var target))
            {
                var odataType = target.TryGetProperty("@odata.type", out var ot) ? ot.GetString() : null;
                groupId = target.TryGetProperty("groupId", out var gid) ? gid.GetString() : null;

                assignmentType = odataType switch
                {
                    "#microsoft.graph.exclusionGroupAssignmentTarget" => "Exclude",
                    "#microsoft.graph.allDevicesAssignmentTarget" => "All Devices",
                    "#microsoft.graph.allLicensedUsersAssignmentTarget" => "All Users",
                    _ => "Include"
                };

                if (assignmentType is "All Devices" or "All Users")
                {
                    assignments.Add(new ProfileAssignment(null, assignmentType, assignmentType));
                    continue;
                }
            }

            string? groupName = null;
            if (!string.IsNullOrEmpty(groupId))
            {
                try
                {
                    var client = GetClient();
                    var group = await client.Groups[groupId].GetAsync(config =>
                    {
                        config.QueryParameters.Select = ["displayName"];
                    }).ConfigureAwait(false);
                    groupName = group?.DisplayName;
                }
                catch
                {
                    groupName = groupId;
                }
            }

            assignments.Add(new ProfileAssignment(groupId, groupName ?? groupId ?? "Unknown", assignmentType));
        }

        return assignments;
    }

    public async Task<List<User>> GetUsersAsync()
    {
        if (_cachedUsers != null && DateTime.UtcNow - _usersCacheTime < CacheDuration)
            return _cachedUsers;

        var client = GetClient();
        var response = await client.Users.GetAsync(config =>
        {
            config.QueryParameters.Top = PageSize;
            config.QueryParameters.Select = ["id", "userPrincipalName", "employeeId", "displayName"];
        }).ConfigureAwait(false);

        var users = new List<User>(response?.Value?.Count ?? 64);

        if (response?.Value != null)
            users.AddRange(response.Value);

        while (response?.OdataNextLink != null)
        {
            response = await client.Users
                .WithUrl(response.OdataNextLink)
                .GetAsync().ConfigureAwait(false);
            if (response?.Value != null)
                users.AddRange(response.Value);
        }

        _cachedUsers = users;
        _usersCacheTime = DateTime.UtcNow;
        return users;
    }

    public async Task<User?> GetUserByUpnAsync(string userPrincipalName)
    {
        var client = GetClient();
        return await client.Users[userPrincipalName].GetAsync().ConfigureAwait(false);
    }

    public async Task SetIntunePrimaryUserAsync(string managedDeviceId, string userId)
    {
        var client = GetClient();
        // Use raw HTTP via the Graph client's request adapter
        var requestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
        {
            HttpMethod = Microsoft.Kiota.Abstractions.Method.POST,
            UrlTemplate = $"https://graph.microsoft.com/v1.0/deviceManagement/managedDevices/{managedDeviceId}/users/$ref"
        };
        requestInfo.SetContentFromParsable(client.RequestAdapter, "application/json",
            new ReferenceCreate { OdataId = $"https://graph.microsoft.com/v1.0/users/{userId}" });

        // Remove existing primary user first
        try { await RemoveIntunePrimaryUserAsync(managedDeviceId); }
        catch { /* No existing user */ }

        await client.RequestAdapter.SendNoContentAsync(requestInfo).ConfigureAwait(false);
    }

    public async Task RemoveIntunePrimaryUserAsync(string managedDeviceId)
    {
        var client = GetClient();
        // Get current primary user via users list
        var requestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
        {
            HttpMethod = Microsoft.Kiota.Abstractions.Method.DELETE,
            UrlTemplate = $"https://graph.microsoft.com/v1.0/deviceManagement/managedDevices/{managedDeviceId}/users/$ref"
        };
        await client.RequestAdapter.SendNoContentAsync(requestInfo).ConfigureAwait(false);
    }

    public async Task<string?> GetAzureAdDeviceIdAsync(string managedDeviceId)
    {
        var client = GetClient();
        var device = await client.DeviceManagement.ManagedDevices[managedDeviceId].GetAsync(config =>
        {
            config.QueryParameters.Select = ["azureADDeviceId"];
        }).ConfigureAwait(false);

        if (string.IsNullOrEmpty(device?.AzureADDeviceId)) return null;

        // Look up the directory device object by azureADDeviceId
        var result = await client.Devices.GetAsync(config =>
        {
            config.QueryParameters.Filter = $"deviceId eq '{device.AzureADDeviceId}'";
            config.QueryParameters.Select = ["id"];
        }).ConfigureAwait(false);

        return result?.Value?.FirstOrDefault()?.Id;
    }

    public async Task AddAzureAdDeviceOwnerAsync(string azureAdDeviceId, string userId)
    {
        var client = GetClient();
        var body = new ReferenceCreate
        {
            OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{userId}"
        };
        await client.Devices[azureAdDeviceId].RegisteredOwners.Ref
            .PostAsync(body).ConfigureAwait(false);
    }

    public async Task RemoveAzureAdDeviceOwnerAsync(string azureAdDeviceId, string ownerId)
    {
        var client = GetClient();
        await client.Devices[azureAdDeviceId].RegisteredOwners[ownerId].Ref
            .DeleteAsync().ConfigureAwait(false);
    }

    public async Task<List<DirectoryObject>> GetAzureAdDeviceOwnersAsync(string azureAdDeviceId)
    {
        var client = GetClient();
        var response = await client.Devices[azureAdDeviceId].RegisteredOwners
            .GetAsync().ConfigureAwait(false);
        return response?.Value ?? [];
    }

    public async Task AddAzureAdDeviceUserAsync(string azureAdDeviceId, string userId)
    {
        var client = GetClient();
        var body = new ReferenceCreate
        {
            OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{userId}"
        };
        await client.Devices[azureAdDeviceId].RegisteredUsers.Ref
            .PostAsync(body).ConfigureAwait(false);
    }

    public async Task RemoveAzureAdDeviceUserAsync(string azureAdDeviceId, string userId)
    {
        var client = GetClient();
        await client.Devices[azureAdDeviceId].RegisteredUsers[userId].Ref
            .DeleteAsync().ConfigureAwait(false);
    }

    public async Task<List<DirectoryObject>> GetAzureAdDeviceUsersAsync(string azureAdDeviceId)
    {
        var client = GetClient();
        var response = await client.Devices[azureAdDeviceId].RegisteredUsers
            .GetAsync().ConfigureAwait(false);
        return response?.Value ?? [];
    }
}

/// <summary>
/// Bridges MSAL token acquisition to Azure.Core TokenCredential for Graph SDK v5.
/// </summary>
internal class DelegateTokenCredential : TokenCredential
{
    private readonly IAuthService _authService;

    public DelegateTokenCredential(IAuthService authService)
    {
        _authService = authService;
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var token = _authService.GetAccessTokenAsync().GetAwaiter().GetResult();
        return new AccessToken(token, DateTimeOffset.UtcNow.AddMinutes(55));
    }

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var token = await _authService.GetAccessTokenAsync();
        return new AccessToken(token, DateTimeOffset.UtcNow.AddMinutes(55));
    }
}
