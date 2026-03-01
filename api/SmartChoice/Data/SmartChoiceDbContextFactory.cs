using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartChoice.Data;

public sealed class SmartChoiceDbContextFactory : IDesignTimeDbContextFactory<SmartChoiceDbContext>
{
    public SmartChoiceDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SmartChoiceDbContext>();

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                               ?? "Server=localhost;Port=3306;Database=smart_choice;User=smart_choice;Password=smart_choice_dev;";

        optionsBuilder.UseMySQL(connectionString);

        return new SmartChoiceDbContext(optionsBuilder.Options);
    }
}
