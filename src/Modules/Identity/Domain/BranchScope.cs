using System.Text.Json.Serialization;

namespace Courier.Identity.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BranchScope
{
    SpecificBranches,
    AllBranches
}
