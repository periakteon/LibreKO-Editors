using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Common.Domain.Entities.GameData;
using Microsoft.EntityFrameworkCore;
static class FixtureRows {
 public static async Task EnsureItem(){const string cs="Server=127.0.0.1;Port=33307;Database=libreko_editor_test_native;User=root;Pooling=false;";await using var context=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseMySql(cs,new MySqlServerVersion(new Version(8,2,0))).Options);await context.Database.EnsureCreatedAsync();if(!await context.Items.AnyAsync(i=>i.Num==123450001)){context.Items.Add(new ItemData{Num=123450001,Name="Test fixture",Damage=10,Weight=7});await context.SaveChangesAsync();}}
}
