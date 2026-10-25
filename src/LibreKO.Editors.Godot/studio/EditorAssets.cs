using System.Text.Json.Nodes;

namespace LibreKO.Editors.Desktop;
public sealed class EditorAssets(EditorSession session)
{
    bool attempted;
    public bool Available { get; private set; }
    public string Failure { get; private set; } = "";

    public void EnsureLoaded()
    {
        if (attempted)
            return;
        attempted = true;
        try
        {
            ContentMount.Mount(session.Profile.AssetRoot, ContentMount.Patches(Path.Combine(session.Profile.AssetRoot, "content/patches")));
            Available = true;
        }
        catch (Exception e)
        {
            Failure = e.Message;
        }
    }

    public JsonObject Json(string path) => Available ? ContentMount.Json(path) : new();
}
