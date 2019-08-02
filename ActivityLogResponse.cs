using System;
using System.Collections.Generic;
using System.Text;

namespace AzResourceInsights
{

    public class ActivityLogs
    {
        public string nextLink { get; set; }
        public Value[] value { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
    }

    public class Value
    {
        public string caller { get; set; }
        public Eventname eventName { get; set; }
        public Category category { get; set; }
        public string id { get; set; }
        public string resourceGroupName { get; set; }
        public string resourceId { get; set; }
        public Resourcetype resourceType { get; set; }
        public Operationname operationName { get; set; }
        public Properties properties { get; set; }
        public Status status { get; set; }
        public Substatus subStatus { get; set; }
        public string eventTimestamp { get; set; }
        public string subscriptionId { get; set; }
        
    }

    public class Eventname
    {
        public string value { get; set; }
        public string localizedValue { get; set; }
    }

    public class Category
    {
        public string value { get; set; }
        public string localizedValue { get; set; }
    }

    public class Resourcetype
    {
        public string value { get; set; }
        public string localizedValue { get; set; }
    }

    public class Operationname
    {
        public string value { get; set; }
        public string localizedValue { get; set; }
    }

    public class Properties
    {
        public string statusCode { get; set; }
        public string responseBody { get; set; }
    }

    public class Status
    {
        public string value { get; set; }
        public string localizedValue { get; set; }
    }

    public class Substatus
    {
        public string value { get; set; }
        public string localizedValue { get; set; }
    }

}
