using Microsoft.EntityFrameworkCore;
using multiTenantApp.Models;
using multiTenantApp.Persistence.Contexts;
using multiTenantApp.Services.TenantService.DTOs;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Azure;
using Microsoft.Data.SqlClient;
using Azure.ResourceManager.Resources;


namespace multiTenantApp.Services.TenantService
{
    public class TenantService : ITenantService
    {

        private readonly TenantDbContext _tenantDbContext; // base database context
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWebHostEnvironment _environment;

        public TenantService(TenantDbContext tenantDbContext, IConfiguration configuration, IServiceProvider serviceProvider, IWebHostEnvironment environment)
        {
            _tenantDbContext = tenantDbContext;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _environment = environment;
        }

        public async Task<Tenant> CreateTenant(CreateTenantRequest request)
        {

            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            SqlConnectionStringBuilder builder = new(connectionString);
            string mainDatabaseName = builder.InitialCatalog; // retrieve the database name
            string tenantDbName = mainDatabaseName + "-" + request.Id;
            builder.InitialCatalog = tenantDbName; // set new database name
            string modifiedConnectionString = builder.ConnectionString; // create new connection string

            Tenant tenant = new() // create a new tenant entity
            {
                Id = request.Id,
                Name = request.Name,
                ConnectionString = request.Isolated ? modifiedConnectionString : null,
            };


            // create a new tenant database and bring current with any pending migrations from ApplicationDbContext
            try
            {
                _ = await _tenantDbContext.Tenants.AddAsync(tenant);

                if (request.Isolated == true)
                {
                    try
                    {
                        IServiceScope scopeTenant = _serviceProvider.CreateScope();
                        ApplicationDbContext applicationDbContext = scopeTenant.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        applicationDbContext.Database.SetConnectionString(modifiedConnectionString);

                        if (_environment.IsDevelopment()) // localhost code
                        {
                            if (applicationDbContext.Database.GetPendingMigrations().Any())
                            {
                                Console.ForegroundColor = ConsoleColor.Blue;
                                Console.WriteLine($"Applying ApplicationDB Migrations for New '{request.Id}' tenant.");
                                Console.ResetColor();
                                await applicationDbContext.Database.MigrateAsync();
                            }
                        }
                        else // azure code
                        {
                            IConfigurationSection azureConfig = _configuration.GetSection("Azure");
                            DefaultAzureCredential credential = new();
                            ArmClient armClient = new(credential, azureConfig["SubscriptionId"]);
                            Azure.Response<SqlServerResource> sqlServer = await armClient.GetResourceGroupResource(
                                    new ResourceIdentifier(
                                        $"/subscriptions/{azureConfig["SubscriptionId"]}/resourceGroups/{azureConfig["ResourceGroupName"]}"))
                                .GetAsync()
                                .Result
                                .Value.GetSqlServerAsync(azureConfig["SqlServerName"]);

                            // Create a new database in the Elastic Pool
                            ArmOperation<SqlDatabaseResource> databaseResponse = await sqlServer.Value
                                .GetSqlDatabases().CreateOrUpdateAsync(WaitUntil.Completed,
                                    tenantDbName,
                                    new SqlDatabaseData(new AzureLocation(sqlServer.Value.Data.Location))
                                    {
                                        ElasticPoolId =
                                            (await sqlServer.Value.GetElasticPoolAsync(
                                                azureConfig["ElasticPoolName"]))
                                            .Value.Data.Id
                                    });

                            // apply migrations to the new database
                            await applicationDbContext.Database.MigrateAsync();
                            if (databaseResponse.Value.Data.Id == null)
                            {
                                throw new Exception("Couldnt create DB on azure");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }



            _ = await _tenantDbContext.SaveChangesAsync(); // tenant save changes to baseDb


            return tenant;
        }
    }
}
