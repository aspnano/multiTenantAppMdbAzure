﻿using Microsoft.AspNetCore.Mvc;
using multiTenantApp.Services.TenantService;
using multiTenantApp.Services.TenantService.DTOs;

namespace multiTenantApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TenantsController : ControllerBase
    {
        private readonly ITenantService _tenantService;

        public TenantsController(ITenantService tenantService)
        {
            _tenantService = tenantService;
        }

        // Create a new tenant
        [HttpPost]
        public async Task<IActionResult> Post(CreateTenantRequest request)
        {
            var result = await _tenantService.CreateTenant(request);
            return Ok(result);
        }
    }
}
