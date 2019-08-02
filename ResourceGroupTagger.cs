using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.Fluent;
using Newtonsoft.Json;
using AzResourceInsights;
using Microsoft.Azure.Services.AppAuthentication;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json.Linq;
using System.Text;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.AppService.Fluent.WebAppSourceControl.Definition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Net.Mail;
using System.Runtime.InteropServices.ComTypes;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Web.Administration;

public class ResourceGroupTagger
{
    private string subscriptionId;
    public ResourceGroupTagger(IAzure azure)
    {
        subscriptionId = azure.SubscriptionId;
    }

    public async Task Run()
    {
        var client = CreateAuthenticatedHttpClient();

        await processResourceGroups(client, subscriptionId);
    }

    static private HttpClient CreateAuthenticatedHttpClient()
    {
        /*
        // Instantiate a new KeyVaultClient object, with an access token to Key Vault
        var azureServiceTokenProvider1 = new AzureServiceTokenProvider();
        var kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider1.KeyVaultTokenCallback));
        Console.WriteLine(kv.GetKeysAsync("HelenKeyVault"));
        */

        string authority = "https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47";
        string resource = "https://management.core.windows.net/";
        string clientId = "ad4fb233-5f2a-4e8e-825e-ce78cd808d3e";
        //todo: method to get clientSecret from keyvault
        string clientSecret = "w_rIEPNYg]]zal9N/yIDY3cM2X6VLeV2";
        string accessToken = GetAccessToken(authority, resource, clientId, clientSecret)
            .GetAwaiter().GetResult();

        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", accessToken);

        return client;
    }

    static async Task processResourceGroups(HttpClient client, string subscriptionId)
    {
        var resourceGroups = await GetResourceGroups(client, subscriptionId);

        foreach (var resourceGroup in resourceGroups)
        {
            string resourceGroupName = resourceGroup.name;
            var activityLogs = await GetActivityLogs(client, subscriptionId, resourceGroupName);
            var creationLog = GetCreationLog(activityLogs);

            if (creationLog != null && (!resourceGroup.tags.ContainsKey("creator") || !resourceGroup.tags.ContainsKey("createdDate") || !resourceGroup.tags.ContainsKey("expiryDate")))
            {
                string creator;

                if (IsValidEmail(creationLog.caller))
                {
                    creator = creationLog.caller;
                }
                else
                {
                    creator = "unknown";
                }

                string createdDate = DateTime.Parse(creationLog.eventTimestamp).ToUniversalTime().ToString("yyyy-MM-dd");
                string expiryDate;

                if (DateTime.Now.CompareTo(DateTime.Parse(creationLog.eventTimestamp).AddDays(180)) < 0)
                {
                    expiryDate = DateTime.Parse(creationLog.eventTimestamp).AddDays(180).ToString("yyyy-MM-dd");
                }
                else
                {
                    //change to use grace period of 3 days instead
                    expiryDate = DateTime.Now.AddDays(180).ToString("yyyy-MM-dd");
                }

                var tags = CreateTags(creator, createdDate, expiryDate);

                await PostTags(client, subscriptionId, resourceGroupName, tags);
            }
        }
        GetExpiredResourceGroups(resourceGroups);
    }

    static async Task<string> GetAccessToken(string authority, string resource, string clientId, string clientSecret)
    {
        var clientCredential = new ClientCredential(clientId, clientSecret);
        AuthenticationContext context = new AuthenticationContext(authority, false);
        AuthenticationResult authenticationResult = await context.AcquireTokenAsync(
            resource,
            clientCredential);
        return authenticationResult.AccessToken;
    }

    static async Task<ResourceGroups[]> GetResourceGroups(HttpClient client, string subscriptionId)
    {
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/resourcegroups?api-version=2019-05-10";
        var response = client.GetAsync(url).Result;
        var result = await response.Content.ReadAsStringAsync();
        var resourceGroups = JsonConvert.DeserializeObject<ResourceGroupsResponse>(result);
        return resourceGroups.value;
    }

    //fix organization here; 
    static async Task<List<Value>> GetActivityLogs(HttpClient client, string subscriptionId, string resourceGroupName)
    {
        var activityLogs = new List<Value>();
        //ActivityLogs activityLogs;
        var toDate = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");
        var fromDate = DateTime.Now.AddDays(-88).ToString("yyyy-MM-dd");

        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/microsoft.insights/eventtypes/management/values?api-version=2015-04-01&%24filter=eventTimestamp%20ge%20%27{fromDate}T04%3A36%3A37.6407898Z%27%20and%20eventTimestamp%20le%20%27{toDate}T04%3A36%3A37.6407898Z%27%20and%20resourceGroupName%20eq%20%27{resourceGroupName}%27&%24";

        do
        {
            var response = client.GetAsync(url).Result;
            var result = await response.Content.ReadAsStringAsync();
            ActivityLogs activityLogsPage = JsonConvert.DeserializeObject<ActivityLogs>(result);

            if (activityLogsPage.value != null)
            {
                activityLogs.AddRange(activityLogsPage.value);
            }
            url = activityLogsPage.nextLink;
        } while (url != null);

        //GetCreationLog(activityLogs);
        return activityLogs;
    }

    static Value GetCreationLog(List<Value> activityLogs)
    {
        var creationLog = activityLogs
            .Where(al => al.operationName.value.ToLower() == "microsoft.resources/subscriptions/resourcegroups/write" && al.properties.statusCode == "Created")
            .OrderBy(al => al.eventTimestamp)
            .FirstOrDefault();

        return creationLog;
    }

    static Dictionary<string, string> CreateTags(string creator, string createdDate, string expiryDate)
    {
        var tags = new Dictionary<string, string>()
                {
                    {"createdBy", creator },
                    {"createdDate", createdDate },
                    {"expiryDate", expiryDate }
                };

        return tags;
    }

    static async Task PostTags(HttpClient client, string subscriptionId, string resourceGroupName, Dictionary<string, string> tags)
    {
        var requestBody = new Dictionary<string, object>()
            {
                {"tags", tags }
            };

        string requestJson = JsonConvert.SerializeObject(requestBody);

        var response = await client.PatchAsync(
        $"https://management.azure.com/subscriptions/{subscriptionId}/resourcegroups/{resourceGroupName}?api-version=2019-05-10",
        new StringContent(requestJson, Encoding.UTF8, "application/json"));
    }

    static void GetExpiredResourceGroups(ResourceGroups[] resourceGroups)
    {
        foreach (var resourceGroup in resourceGroups)
        {
            //1. read expirydate tag
            //if resourceGroup has reached expiry date, and has no activity logs from the last 60 days:
            //send email notifying creator to manually extend expiry date if RG still needed
            //otherwise, call delete function three days after and send email notifying delete
            string value = "";

            if (resourceGroup.tags.TryGetValue("expiryDate", out value) && DateTime.Now.CompareTo(Convert.ToDateTime(value)) >= 0)
            {
                //send email notification
                Console.WriteLine(resourceGroup.name + " has expired.");
            }
            else
            {
                Console.WriteLine(resourceGroup.name + " has not expired.");
            }
        }
    }


    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            // Normalize the domain
            email = Regex.Replace(email, @"(@)(.+)$", DomainMapper,
                                  RegexOptions.None, TimeSpan.FromMilliseconds(200));

            // Examines the domain part of the email and normalizes it.
            string DomainMapper(Match match)
            {
                // Use IdnMapping class to convert Unicode domain names.
                var idn = new IdnMapping();

                // Pull out and process domain name (throws ArgumentException on invalid)
                var domainName = idn.GetAscii(match.Groups[2].Value);

                return match.Groups[1].Value + domainName;
            }
        }
        catch (RegexMatchTimeoutException e)
        {
            return false;
        }
        catch (ArgumentException e)
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(email,
                @"^(?("")("".+?(?<!\\)""@)|(([0-9a-z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)(?<=[0-9a-z])@))" +
                @"(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-z][-0-9a-z]*[0-9a-z]*\.)+[a-z0-9][\-a-z0-9]{0,22}[a-z0-9]))$",
                RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

}
