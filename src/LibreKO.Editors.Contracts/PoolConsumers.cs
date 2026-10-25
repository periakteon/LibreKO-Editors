namespace LibreKO.Editors.Contracts;
public record PoolConsumerQuery(int PoolId,int Page=0,int Size=50);
public record PoolConsumer(WorldKey DropGroup,WorldKey? Owner,string OwnerName);
