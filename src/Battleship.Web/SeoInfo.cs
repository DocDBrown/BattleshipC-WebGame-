// SeoInfo.cs
using System.Text.Json.Nodes;

namespace Battleship.Web;

public static class SeoInfo
{
    public const string SiteName = "Battleship";
    public const string RepositoryUrl = "https://github.com/DocDBrown/BattleshipC-WebGame-";
    public const string ImagePath = "battleship1.webp";
    public const string ImageAlt = "Battleship web game screenshot showing two 10x10 boards";

    public static string Absolute(string baseUri, string path) =>
        new Uri(new Uri(baseUri), path.TrimStart('/')).ToString();

    public static string ToJsonLd(JsonNode node) => node.ToJsonString();
}