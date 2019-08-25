# Welcome to Azure Auto Resource Manager
## What is this?
Writing a bunch of tags on every resource group is tedious, but there aren't many good ways to organize resource groups in the Azure portal. AARM is a tool that will help make tags for you, which are detailed below.

Tags:
* createdBy: indicates owner of the RG in the form of an email. Displays "unknown" if creator is not a valid email address. 
* createdDate: indicates date the RG was created
* expiryDate: indicates calculated expiration date in the YYYY-MM-DD format which is createdDate + expiryDuration, which is set by user in config file. User should manually change value of this tag if a different expiration date is needed

As shown above, AARM calculates an expiration date for each resource group. Another feature of AARM is to delete old resource groups that are no longer needed. This improves organizat

## Setting Up

### Configuration File

Read this for a brief overview of some fields: https://docs.microsoft.com/en-us/azure/active-directory/develop/msal-client-application-configuration

config.json requires the user to fill in the following fields:
*	Authority: https://login.microsoftonline.com/TENANT_ID
*	Client ID of user
*	fromEmail: the email address to send notifications from. Sending from @microsoft to Gmail is blocked, while from @outlook works fine. From @microsoft to @microsoft works but will end up in junk folder. 
*	expiryDuration: number of days from creation date to set as expiration date on all resource groups
*	graceDuration: number of days to keep resource groups that have exceeded expiration before 

###Store Secrets using Secret Manager

To create the userSecretsId, Open the AzureAutoResourceManager.csproj file in Notepad and edit this line:
```<UserSecretsId>User Secret GUID Here</UserSecretsId>```

Store secrets in the secrets.json file, which can be found at %APPDATA%\microsoft\UserSecrets\<userSecretsId>\secrets.json
Secrets.json is stored locally on your machine, independent of the project.

Make secrets.json look like this:

```{
  "AARMSecrets": {
    "clientSecret": "secret here",
    "SendGridKey": "SendGrid key here"
  }
}```

### Changing an Expiration Date
Three days before a resource group expires, the owner will receive an email. It will prompt you to change the expiration date if you want to keep the resource group. Manually edit the expiryDate tag following the YYYY-MM-DD format.

### Exempting a Resource Group
Set the value of the “expiryDate” tag to “do not delete”. AARM will skip over this resource group and it will not be deleted. 



##Areas for Improvement
1. As of now there is no support for how to deal with transferring ownership of an RG to a different person, which would be useful in an organization with frequently changing members.
