using Microsoft.AspNetCore.Mvc;
using OPC.MaintenanceAPI.DTOs.Equipment;
using OPC.MaintenanceAPI.Services.Interfaces;

namespace OPC.MaintenanceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EquipmentController : ControllerBase
    {
        private readonly IEquipmentService _service;
        public EquipmentController(IEquipmentService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var r = await _service.GetByIdAsync(id);
            return r == null ? NotFound() : Ok(r);
        }

        [HttpPost]
        public async Task<IActionResult> Create(TaoThietBiDto dto)
        {
            var (ok, loi) = await _service.TaoMoiAsync(dto);
            return ok ? Ok() : BadRequest(new { loi });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, CapNhatThietBiDto dto)
        {
            var (ok, loi) = await _service.CapNhatAsync(id, dto);
            return ok ? Ok(new { canhBao = loi }) : BadRequest(new { loi });
        }

        [HttpGet("{id}/lich-su")]
        public async Task<IActionResult> GetLichSu(int id) => Ok(await _service.GetLichSuAsync(id));
    }
}