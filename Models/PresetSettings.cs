using System.Collections.Generic;

namespace LinqqXrayVPN.Models
{
    public class PresetSettings
    {
        public List<SubscriptionEntry>? Subscriptions { get; set; }
        public List<CustomRoutingRule>? CustomRules { get; set; }
    }
}
