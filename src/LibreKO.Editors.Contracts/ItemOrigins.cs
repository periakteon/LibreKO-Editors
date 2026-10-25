namespace LibreKO.Editors.Contracts;
public record ItemOriginQuery(int ItemId,int Page=0,int Size=25);
public record ItemOrigin(string Kind,WorldKey Owner,string OwnerName,WorldKey? Definition,int Slot,int BasisPoints,
    int? PoolId=null,int PoolMatches=1,int PoolSize=1,SourceFileKey? Source=null,int SourceRow=-1,int ShopGroup=0,int ShopLine=0);
public record ItemOriginPage(IReadOnlyList<ItemOrigin> Rows,int Total,int Page,int Size,IReadOnlyList<string> Diagnostics);
