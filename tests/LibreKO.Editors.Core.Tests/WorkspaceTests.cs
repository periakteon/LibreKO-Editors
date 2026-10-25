using LibreKO.Editors.Core;
public class WorkspaceTests {
 [Theory][InlineData("../escape")][InlineData("C:/escape")][InlineData("/escape")]public void RejectsTraversal(string path)=>Assert.Throws<InvalidOperationException>(()=>WorkspacePaths.ResolveOwnedPath(Path.GetTempPath(),path));
}
