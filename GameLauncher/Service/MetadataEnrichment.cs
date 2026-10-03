using System.IO;
using System.Reflection;
using System.Text.Json;
using GameLauncher.models;
namespace GameLauncher.Service;
public static class MetadataEnrichment
{
    private static readonly Dictionary<string,string> TagNames = LoadTags();
    private static Dictionary<string,string> LoadTags()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("GameLauncher.Data.f95-tags.json");
        return stream==null?new():JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??new();
    }
    public static List<string> ReadTags(JsonElement root)
    {
        var values=new List<string>();
        foreach(string key in new[]{"tags","unknown_tags"})
        {
            if(!root.TryGetProperty(key,out var tags))continue;
            if(tags.ValueKind==JsonValueKind.String)
            {
                try { using var decoded=JsonDocument.Parse(tags.GetString()??"[]"); Add(decoded.RootElement); }
                catch(JsonException) { values.AddRange((tags.GetString()??"").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)); }
            }
            else Add(tags);
        }
        return values.Where(s=>s.Length>0&&s.Length<=100).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
        void Add(JsonElement value)
        {
            if(value.ValueKind!=JsonValueKind.Array)return;
            foreach(var item in value.EnumerateArray().Take(100))
            {
                string token=item.ToString().Trim();
                if(TagNames.TryGetValue(token,out var name)) values.Add(name);
                else if(!int.TryParse(token,out _)) values.Add(token);
            }
        }
    }
    public static void Apply(Game game,F95Metadata metadata)
    {
        if(!game.UserEditedFields.Contains(nameof(Game.F95Developer)) && metadata.Developer.Length>0)game.F95Developer=metadata.Developer;
        if(!game.UserEditedFields.Contains(nameof(Game.Tags)))game.Tags=game.Tags.Concat(metadata.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
        if(!game.UserEditedFields.Contains(nameof(Game.Description)) && metadata.Description.Length>0)game.Description=metadata.Description;
        if(!game.UserEditedFields.Contains(nameof(Game.Engine)) && metadata.Engine.Length>0)game.Engine=metadata.Engine;
        game.MetadataUpdatedUtc=DateTime.UtcNow;
    }
}
