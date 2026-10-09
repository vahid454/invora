using System.Globalization;
using System.Text.Json;
namespace Invora.IntegrationTests;
internal static class JsonTestExtensions
{
    public static decimal Amount(this JsonElement value)=>decimal.Parse(value.ToString(),CultureInfo.InvariantCulture);
}
