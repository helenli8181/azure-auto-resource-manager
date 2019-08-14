using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using Microsoft.Azure.Management.Fluent;
using Newtonsoft.Json;
using AzResourceInsights;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Text;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.IO;
using SendGrid;
using SendGrid.Helpers.Mail;

public class ResourceGroupManager
{
    private string subscriptionId;
    private string clientSecret;
    private string SendGridKey;
    public ResourceGroupManager(IAzure azure, string clientSecret, string SendGridKey)
    {
        subscriptionId = azure.SubscriptionId;
        this.clientSecret = clientSecret;
        this.SendGridKey = SendGridKey;
    }

    
    public async Task Run(IAzure azure)
    {
        var settings = GetConfigJson();
        var client = CreateAuthenticatedHttpClient(settings, clientSecret);

        await processResourceGroups(azure, client, subscriptionId, settings, SendGridKey);

    }

    static private HttpClient CreateAuthenticatedHttpClient(ResourceGroupSettings settings, string clientSecret)
    {
        Console.WriteLine("Authenticating...");

        string authority = settings.user["authority"];
        string resource = "https://management.core.windows.net/";
        string clientId = settings.user["clientId"];
        string accessToken = GetAccessToken(authority, resource, clientId, clientSecret)
            .GetAwaiter().GetResult();

        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", accessToken);

        return client;
    }

    static async Task processResourceGroups(IAzure azure, HttpClient client, string subscriptionId, ResourceGroupSettings settings, string SendGridKey)
    {
        var resourceGroups = await GetResourceGroups(client, subscriptionId);

        Console.WriteLine("Checking for tags...");

        foreach (var resourceGroup in resourceGroups)
        {
            string resourceGroupName = resourceGroup.name;
            var activityLogs = await GetActivityLogs(client, subscriptionId, resourceGroupName);
            var creationLog = GetCreationLog(activityLogs);

            if (creationLog != null &&  (resourceGroup.tags == null || !resourceGroup.tags.ContainsKey("createdBy") || !resourceGroup.tags.ContainsKey("createdDate") || !resourceGroup.tags.ContainsKey("expiryDate")))
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

                if (DateTime.Now.CompareTo(DateTime.Parse(creationLog.eventTimestamp).AddDays(Int32.Parse(settings.resourceGroup["expiryDuration"]))) < 0)
                {
                    expiryDate = DateTime.Parse(creationLog.eventTimestamp).AddDays(Int32.Parse(settings.resourceGroup["expiryDuration"])).ToString("yyyy-MM-dd");
                }
                else
                {
                    expiryDate = DateTime.Now.AddDays(Int32.Parse(settings.resourceGroup["graceDuration"])).ToString("yyyy-MM-dd");
                }

                var tags = CreateTags(creator, createdDate, expiryDate);

                await PatchTags(client, subscriptionId, resourceGroupName, tags);
            }
        }
        await ManageExpiredResourceGroups(azure, resourceGroups, client, settings, subscriptionId, SendGridKey);
        
        
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

    static async Task PatchTags(HttpClient client, string subscriptionId, string resourceGroupName, Dictionary<string, string> tags)
    {
        var requestBody = new Dictionary<string, object>()
            {
                {"tags", tags }
            };

        string requestJson = JsonConvert.SerializeObject(requestBody);

        await client.PatchAsync(
        $"https://management.azure.com/subscriptions/{subscriptionId}/resourcegroups/{resourceGroupName}?api-version=2019-05-10",
        new StringContent(requestJson, Encoding.UTF8, "application/json"));

        Console.WriteLine("Successfully tagged" + resourceGroupName);
    }

    public ResourceGroupSettings GetConfigJson()
    {
        using (StreamReader reader = new StreamReader("config.json"))
        {
            string json = reader.ReadToEnd();
            ResourceGroupSettings settings = JsonConvert.DeserializeObject<ResourceGroupSettings>(json);
            return settings;
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

    public static async Task ManageExpiredResourceGroups(IAzure azure, ResourceGroups[] resourceGroups, HttpClient client, ResourceGroupSettings settings, string subscriptionId, string SendGridKey)
    {
        Console.WriteLine("Checking for expired resource groups...");

        foreach (var resourceGroup in resourceGroups)
        {
            //1. read expirydate tag
            //if resourceGroup has reached expiry date, and has no activity logs from the last 60 days:
            //send email notifying creator to manually extend expiry date if RG still needed
            //otherwise, call delete function three days after and send email notifying delete

            string value = "";
            resourceGroup.tags.TryGetValue("expiryDate", out value);

            if ( value == "do not delete")
            {
                Console.WriteLine(resourceGroup.name + " has been exempt.");

            } else
            {
                var resourceGroupName = resourceGroup.name;
                var activityLogs = await GetActivityLogs(client, subscriptionId, resourceGroupName);
                var creationLog = GetCreationLog(activityLogs);
                var creator = resourceGroup.tags["createdBy"];
                var createdDate = resourceGroup.tags["createdDate"];
                var expiryDate = resourceGroup.tags["expiryDate"];

                resourceGroup.tags.TryGetValue("expiryDate", out value);

                if (resourceGroup.tags.TryGetValue("expiryDate", out value) && DateTime.Now.CompareTo(Convert.ToDateTime(value)) >= 0)
                {
                    DeleteResourceGroup(azure, resourceGroupName);
                }
                else if (resourceGroup.tags.TryGetValue("expiryDate", out value) && DateTime.Now.AddDays(3).CompareTo(Convert.ToDateTime(value)) >= 0)
                {
                    //send email notification
                    SendEmailNotification(creationLog, client, settings, subscriptionId, resourceGroupName, activityLogs, resourceGroup, creator, createdDate, expiryDate, SendGridKey);

                    Console.WriteLine(resourceGroup.name + " will expire soon.");
                }
                else
                {
                    Console.WriteLine(resourceGroup.name + " has not expired.");
                }
            } 
        }
    }

    //NOT DONE YET
    //todo: don't run this if rg has email sent tag
    public static void SendEmailNotification(Value creationLog, HttpClient client, ResourceGroupSettings settings, string subscriptionId, string resourceGroupName, List<Value> activityLogs, ResourceGroups resourceGroup, string creator, string createdDate, string expiryDate, string SendGridKey)
    {

        var apiKey = SendGridKey;

        var emailClient = new SendGridClient(apiKey);
        var from = new EmailAddress(settings.user["fromEmail"], "Auto ARM");
        var subject = resourceGroupName + " about to expire";
        var to = new EmailAddress(GetEmailAddress(creationLog, activityLogs), "Example User");
        var plainTextContent = "Hello, your resource group " + resourceGroupName + " will expire on " + expiryDate + ". Please change the expiryDate tag if you do not wish to delete it.";
        var htmlContent = "";
        var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
        var response = emailClient.SendEmailAsync(msg).Result;
        string resp1 = response.StatusCode.ToString();
        string resp2 = response.Body.ReadAsStringAsync().Result.ToString();
        string resp3 = response.Headers.ToString();

        PatchEmailSentTag(client, subscriptionId, resourceGroupName, creator, createdDate, expiryDate).Wait();
    }

    public static string GetEmailAddress(Value creationLog, List<Value> activityLogs)
    {
        if (IsValidEmail(creationLog.caller))
        {
            return creationLog.caller;
        }
        else if (IsValidEmail(GetLastCaller(activityLogs)))
        {
            return GetLastCaller(activityLogs);
        }
        else
        {
            return null;
        }
    }

    public static string GetLastCaller(List<Value> activityLogs)
    {
        var lastAttributedLog = activityLogs
            .Where(al => IsValidEmail(al.caller))
            .OrderBy(al => al.eventTimestamp)
            .LastOrDefault();

        return lastAttributedLog.caller;
    }

    public static async Task PatchEmailSentTag(HttpClient client, string subscriptionId, string resourceGroupName, string creator, string createdDate, string expiryDate)
    {
        var tag = new Dictionary<string, string>()
        {
            {"createdBy", creator },
            {"createdDate", createdDate },
            {"expiryDate", expiryDate },
            { "expiryWhenEmailSent", expiryDate }
        };
        var requestBody = new Dictionary<string, object>()
            {
                {"tags", tag}
            };

        string requestJson = JsonConvert.SerializeObject(requestBody);

        await client.PatchAsync(
        $"https://management.azure.com/subscriptions/{subscriptionId}/resourcegroups/{resourceGroupName}?api-version=2019-05-10",
        new StringContent(requestJson, Encoding.UTF8, "application/json"));
    }

    public static void DeleteResourceGroup(IAzure azure, string resourceGroupName)
    {
        azure.ResourceGroups.DeleteByName(resourceGroupName);
    }
}
