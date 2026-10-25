using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
using LibreKO.Editors.DataHost.World;

namespace LibreKO.Editors.DataHost.Sources;

public static class ServerSourceSchema
{
    public static IReadOnlyList<WorldFieldDescriptor> Fields(string filename)
        =>ServerJsonCatalog.Fields(ServerJsonCatalog.Format(filename) ?? throw new InvalidOperationException("Unsupported server source."),WorldSchema.Catalog());
    public static IReadOnlyList<ValidationIssue> Validate(JsonSourceDocument document)=>ServerJsonCatalog.Validate(document,WorldSchema.Catalog());
}
