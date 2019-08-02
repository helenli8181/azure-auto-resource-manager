using System.Collections.Generic;

public class ResourceGroups
{
    public string id { get; set; }
    public string name { get; set; }
    public string type { get; set; }
    public string location { get; set; }
    public Dictionary<string, string> tags { get; set; }
    public Dictionary<string, string> properties { get; set; }
}
