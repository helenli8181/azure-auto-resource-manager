using System;
using Microsoft.Azure.Management.Fluent;

namespace AzureAutoResourceManager
{
    class Program
    {
        static void Main(string[] args)
        {
            var azure = Azure.Authenticate("auth.txt").WithDefaultSubscription();

            var resourceGroupTagger = new ResourceGroupTagger(azure);

            resourceGroupTagger.Run().Wait();
        }
    }
}
