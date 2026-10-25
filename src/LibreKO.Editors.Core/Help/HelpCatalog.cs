using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Help;

public sealed record HelpGuide(string Id,string Title,string Summary,string Markdown);

public static class HelpCatalog
{
    public static IReadOnlyList<HelpGuide> All { get; }=Array.AsReadOnly(new[]
    {
        Read("overview","Start here · where changes are saved","Choose the correct production workflow and understand drafts, database saves and file review."),
        Read("players","Player characters & accounts","Offline inventory, character, GM role, Knight Cash and premium updates."),
        Read("database","Database configuration","Items, NPCs, drops, spawns, zones and event settings; caches and startup reseeding."),
        Read("seeds","Server seed JSON","Deploy complete database-backed seed definitions without overwriting unrelated live changes."),
        Read("shops","Merchant shops & service files","ItemSells.json and direct-load exchange/mining/fishing configuration."),
        Read("maps","Server maps · SMD","Deploy map height, collision, events and actual warp destinations."),
        Read("quests","Server quests","Validate and deploy quest scripts, includes and manifest membership."),
        Read("client","Client release · visuals & terrain","Save, build, verify and distribute a complete updated game client."),
        Read("release","Combined release & rollback","Coordinate database, server files and client assets; verify and recover."),
        Read("controls","Editor controls & troubleshooting","Navigation, inventory, maps, loading, source windows and missing content.")
    });
    static HelpGuide Read(string id,string title,string summary)
    {
        using var stream=typeof(HelpCatalog).Assembly.GetManifestResourceStream("LibreKO.Help."+id+".md")??throw new InvalidOperationException("Missing help guide: "+id);
        using var reader=new StreamReader(stream);return new(id,title,summary,reader.ReadToEnd());
    }
    public static IReadOnlyList<HelpGuide> Search(string? query)
    {
        var words=(query??"").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        return words.Length==0?All:All.Where(g=>words.All(w=>(g.Title+"\n"+g.Summary+"\n"+g.Markdown).Contains(w,StringComparison.OrdinalIgnoreCase))).ToArray();
    }
    public static string ForSource(SourceRoot root,string path)
    {
        var name=path.Replace('\\','/').Split('/').Last();
        return root switch
        {
            SourceRoot.ServerMaps=>"maps",SourceRoot.ServerQuests=>"quests",
            SourceRoot.ServerData=>new[]{"ItemSells.json","ItemExchangeExps.json","MiningExchanges.json","MiningFishingItems.json"}.Contains(name,StringComparer.OrdinalIgnoreCase)?"shops":"seeds",
            _=>"client"
        };
    }
    public static string ForChanges(IEnumerable<SourceFileKey> files,bool database)
    {
        var topics=files.Select(f=>ForSource(f.Root,f.RelativePath)).Distinct().ToArray();
        return topics.Length==0?database?"database":"overview":topics.Length>1||database?"release":topics[0];
    }
}
