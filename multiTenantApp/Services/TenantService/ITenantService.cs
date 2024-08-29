using multiTenantApp.Models;
using multiTenantApp.Services.TenantService.DTOs;

namespace multiTenantApp.Services.TenantService
{
    public interface ITenantService
    {
        Task<Tenant> CreateTenant(CreateTenantRequest request);
    }
}
