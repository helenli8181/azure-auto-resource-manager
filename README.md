# azure-auto-resource-manager
Setting Up
Configuration File
https://docs.microsoft.com/en-us/azure/active-directory/develop/msal-client-application-configuration
-	Authority: https://login.microsoftonline.com/TENANT_ID
-	Client ID
-	fromEmail: the email address to send notifications from. Sending from @microsoft to Gmail is blocked, while from @outlook works fine. From @microsoft to @microsoft works but will end up in junk folder. 
-	expiryDuration: number of days from creation date to set as expiration date on all resource groups
-	graceDuration: number of days to keep resource groups that have exceeded expiration before 

Store Secrets using Secret Manager
Create userSecretsId:
Open the AzureAutoResourceManager.csproj file in Notepad and edit this line:
<UserSecretsId>User Secret GUID Here</UserSecretsId>
secrets.json:
Store secrets in the secrets.json file, which can be found at %APPDATA%\microsoft\UserSecrets\<userSecretsId>\secrets.json
Secrets.json is stored locally on your machine, independent of the project.
Make secrets.json look like this:
{
  "AARMSecrets": {
    "clientSecret": "secret here",
    "SendGridKey": "SendGrid key here"
  }
}
Changing an expiration date:
Three days before a resource group expires, the owner will receive an email. It will prompt you to change the expiration date if you want to keep the resource group. Manually edit the expiryDate tag following the YYYY-MM-DD format.
Exempting a Resource Group:
Set the value of the “expiryDate” tag to “do not delete”. AARM will skip over this resource group and it will not be deleted. 

