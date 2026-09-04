using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.DTOs.MaintenancePlan;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MaintenancePlanController : ControllerBase
    {
        private readonly IMaintenancePlanService _service;
        public MaintenancePlanController(IMaintenancePlanService service) => _service = service;

        [HttpPost]
        public async Task<IActionResult> LapKeHoach(LapKeHoachDto dto)
        {
            var (ok, loi) = await _service.LapKeHoachAsync(dto);
            return ok ? Ok() : BadRequest(new { loi });
        }

        [HttpGet("cho-tao-ho-so")]
        public async Task<IActionResult> GetChoTaoHoSo() => Ok(await _service.GetChiTietChuaCoHoSoAsync());
    }
}