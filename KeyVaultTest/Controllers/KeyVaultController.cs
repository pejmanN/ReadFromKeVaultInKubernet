using KeyVaultTest.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KeyVaultTest.Controllers
{
    [ApiController]
    [Route("KeyVault")]
    public class KeyVaultController : ControllerBase
    {
        private readonly UserSetting _userSetting;
        private readonly ILogger<KeyVaultController> _logger;
        public KeyVaultController(ILogger<KeyVaultController> logger,
                                  IOptions<UserSetting> userSettingOptions)
        {
            _logger = logger;
            _userSetting = userSettingOptions.Value;

        }

        [HttpGet]
        public IActionResult Get()
        {
            var secret = _userSetting.MySecret;
            return Ok(new { Secret = secret });
        }
    }
}
