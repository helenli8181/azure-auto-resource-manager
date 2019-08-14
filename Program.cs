using System;
using System.IO;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.Administration;

namespace AzureAutoResourceManager
{
    class Program
    {

        public static IConfigurationRoot Configuration { get; set; }
        static void Main(string[] args)
        {
            var devEnvironmentVariable = Environment.GetEnvironmentVariable("NETCORE_ENVIRONMENT");
            var isDevelopment = string.IsNullOrEmpty(devEnvironmentVariable) || devEnvironmentVariable.ToLower() == "development";
            //Determines the working environment as IHostingEnvironment is unavailable in a console app

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config.json", optional: false, reloadOnChange: true);

            if (isDevelopment) //only add secrets in development
            {
                builder.AddUserSecrets<Program>();
            }

            Configuration = builder.Build();

            var clientSecret = Configuration["AARMSecrets:clientSecret"];
            var SendGridKey = Configuration["AARMSecrets:SendGridKey"];
            var azure = Azure.Authenticate("auth.txt").WithDefaultSubscription();
            var resourceGroupManager = new ResourceGroupManager(azure, clientSecret, SendGridKey);
            resourceGroupManager.Run(azure).Wait();

        }

    }


}
